using System;
using System.Collections.Generic;
using System.IO;

namespace NowOverlayer
{
    // Minimal GIF89a/87a decoder: all frames composited to full-size RGBA (top row first), with delays.
    // Pure C#, so it can run off the main thread.
    public static class Gif
    {
        public class Frame
        {
            public byte[] Rgba; // width * height * 4, row 0 = top
            public int DelayMs;
        }

        public class Result
        {
            public int Width, Height;
            public List<Frame> Frames = new List<Frame>();
        }

        public static bool IsGif(byte[] d) => d.Length > 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F';

        public static Result Decode(byte[] data, int maxFrames = 400)
        {
            var s = new MemoryStream(data);
            var br = new BinaryReader(s);
            br.ReadBytes(6); // GIF89a
            int w = br.ReadUInt16(), h = br.ReadUInt16();
            byte flags = br.ReadByte();
            int bgIndex = br.ReadByte();
            br.ReadByte(); // aspect
            byte[] gct = (flags & 0x80) != 0 ? br.ReadBytes(3 * (1 << ((flags & 7) + 1))) : null;

            var res = new Result { Width = w, Height = h };
            var canvas = new byte[w * h * 4];
            byte[] previous = null;
            int delay = 100, transparent = -1, disposal = 0;

            while (s.Position < s.Length && res.Frames.Count < maxFrames)
            {
                int block = br.ReadByte();
                if (block == 0x3B) break; // trailer
                if (block == 0x21)
                {
                    int label = br.ReadByte();
                    if (label == 0xF9)
                    {
                        br.ReadByte(); // size 4
                        byte pf = br.ReadByte();
                        disposal = (pf >> 2) & 7;
                        delay = br.ReadUInt16() * 10;
                        int ti = br.ReadByte();
                        transparent = (pf & 1) != 0 ? ti : -1;
                        br.ReadByte(); // terminator
                    }
                    else
                    {
                        SkipSubBlocks(br);
                    }
                    continue;
                }
                if (block != 0x2C) break;

                int fx = br.ReadUInt16(), fy = br.ReadUInt16(), fw = br.ReadUInt16(), fh = br.ReadUInt16();
                byte iflags = br.ReadByte();
                byte[] lct = (iflags & 0x80) != 0 ? br.ReadBytes(3 * (1 << ((iflags & 7) + 1))) : null;
                bool interlaced = (iflags & 0x40) != 0;
                byte[] pal = lct ?? gct;
                int minCode = br.ReadByte();
                byte[] lzw = ReadSubBlocks(br);
                byte[] idx = Lzw(lzw, minCode, fw * fh);

                if (disposal == 3) previous = (byte[])canvas.Clone();
                for (int row = 0; row < fh; row++)
                {
                    int srcRow = interlaced ? InterlacedRow(row, fh) : row;
                    int y = fy + srcRow;
                    if (y >= h) continue;
                    for (int col = 0; col < fw; col++)
                    {
                        int x = fx + col;
                        if (x >= w) continue;
                        int ci = idx[row * fw + col];
                        if (ci == transparent || pal == null || ci * 3 + 2 >= pal.Length) continue;
                        int o = (y * w + x) * 4;
                        canvas[o] = pal[ci * 3];
                        canvas[o + 1] = pal[ci * 3 + 1];
                        canvas[o + 2] = pal[ci * 3 + 2];
                        canvas[o + 3] = 255;
                    }
                }
                res.Frames.Add(new Frame { Rgba = (byte[])canvas.Clone(), DelayMs = delay < 20 ? 100 : delay });

                if (disposal == 2)
                {
                    for (int row = 0; row < fh; row++)
                    for (int col = 0; col < fw; col++)
                    {
                        int x = fx + col, y = fy + row;
                        if (x < w && y < h) Array.Clear(canvas, (y * w + x) * 4, 4);
                    }
                }
                else if (disposal == 3 && previous != null)
                {
                    canvas = previous;
                }
                transparent = -1;
                disposal = 0;
                delay = 100;
            }
            return res;
        }

        // Pass order of interlaced rows: every 8th from 0, every 8th from 4, every 4th from 2, every 2nd from 1.
        private static int InterlacedRow(int i, int h)
        {
            int p1 = (h + 7) / 8, p2 = (h + 3) / 8, p3 = (h + 1) / 4;
            if (i < p1) return i * 8;
            i -= p1;
            if (i < p2) return i * 8 + 4;
            i -= p2;
            if (i < p3) return i * 4 + 2;
            i -= p3;
            return i * 2 + 1;
        }

        private static void SkipSubBlocks(BinaryReader br)
        {
            int n;
            while ((n = br.ReadByte()) > 0) br.ReadBytes(n);
        }

        private static byte[] ReadSubBlocks(BinaryReader br)
        {
            var ms = new MemoryStream();
            int n;
            while ((n = br.ReadByte()) > 0) ms.Write(br.ReadBytes(n), 0, n);
            return ms.ToArray();
        }

        private static byte[] Lzw(byte[] data, int minCode, int count)
        {
            var output = new byte[count];
            int clear = 1 << minCode, eoi = clear + 1;
            var prefix = new int[4096];
            var suffix = new byte[4096];
            var stack = new byte[4097];
            int codeSize = minCode + 1, mask = (1 << codeSize) - 1, next = clear + 2;
            int old = -1, first = 0, bits = 0, cur = 0, pos = 0, outPos = 0;
            for (int i = 0; i < clear; i++) suffix[i] = (byte)i;

            while (outPos < count)
            {
                while (bits < codeSize)
                {
                    if (pos >= data.Length) return output;
                    cur |= data[pos++] << bits;
                    bits += 8;
                }
                int code = cur & mask;
                cur >>= codeSize;
                bits -= codeSize;

                if (code == clear)
                {
                    codeSize = minCode + 1;
                    mask = (1 << codeSize) - 1;
                    next = clear + 2;
                    old = -1;
                    continue;
                }
                if (code == eoi) break;
                if (old == -1)
                {
                    output[outPos++] = suffix[code];
                    old = code;
                    first = code;
                    continue;
                }
                int inCode = code, sp = 0;
                if (code >= next)
                {
                    stack[sp++] = (byte)first;
                    code = old;
                }
                while (code > clear)
                {
                    stack[sp++] = suffix[code];
                    code = prefix[code];
                }
                first = suffix[code];
                stack[sp++] = (byte)first;
                if (next < 4096)
                {
                    prefix[next] = old;
                    suffix[next] = (byte)first;
                    next++;
                    if ((next & mask) == 0 && next < 4096)
                    {
                        codeSize++;
                        mask = (1 << codeSize) - 1;
                    }
                }
                old = inCode;
                while (sp > 0 && outPos < count) output[outPos++] = stack[--sp];
            }
            return output;
        }
    }
}
