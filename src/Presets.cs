using System.Collections.Generic;
using UnityEngine;

namespace NowOverlayer
{
    // A saved overlay layout. Switch with the preset hotkeys (Hotkeys.cs) or from the mod window.
    public class Preset
    {
        public string Name = "";
        public List<ElementConfig> Elements = new List<ElementConfig>();
        public float Size = 1f;
        public AnimType Anim = AnimType.SlideIn;
        public float AnimDuration = 0.6f, AnimStagger = 0.05f;
        public string FontPath = "";
    }

    public static class Presets
    {
        private static List<ElementConfig> Copy(List<ElementConfig> src)
        {
            var list = new List<ElementConfig>(src.Count);
            foreach (ElementConfig c in src)
            {
                ElementConfig n = c.Clone();
                n.Id = c.Id;
                list.Add(n);
            }
            return list;
        }

        public static Preset Capture(string name)
        {
            Settings s = Main.Settings;
            return new Preset
            {
                Name = name, Elements = Copy(s.Elements), Size = s.Size, Anim = s.Anim,
                AnimDuration = s.AnimDuration, AnimStagger = s.AnimStagger, FontPath = s.FontPath ?? ""
            };
        }

        public static void Overwrite(Preset p)
        {
            Preset n = Capture(p.Name);
            p.Elements = n.Elements;
            p.Size = n.Size;
            p.Anim = n.Anim;
            p.AnimDuration = n.AnimDuration;
            p.AnimStagger = n.AnimStagger;
            p.FontPath = n.FontPath;
            Main.Settings.Save(Main.Mod);
        }

        public static void Apply(Preset p)
        {
            Settings s = Main.Settings;
            s.Elements = Copy(p.Elements);
            s.Size = p.Size;
            s.Anim = p.Anim;
            s.AnimDuration = p.AnimDuration;
            s.AnimStagger = p.AnimStagger;
            s.ActivePreset = p.Name;
            Overlay o = Overlay.Instance;
            if (o)
            {
                if ((p.FontPath ?? "") != (s.FontPath ?? "")) o.SetFont(p.FontPath);
                else o.Rebuild();
                o.ShowToast("프리셋 · " + p.Name);
            }
            s.Save(Main.Mod);
        }
    }
}
