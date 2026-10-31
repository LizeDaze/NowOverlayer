# NowOverlayer

얼불춤(A Dance of Fire and Ice) 방송 · 녹화용 오버레이 모드 (UnityModManager)

## 기능
- 진행도 · X-정확도 · 정확도 · 음악 시간 · BPM/KPS · 판정 · 콤보 · 시도 횟수 등 오버레이 항목
- 직접 쓰는 텍스트 (`{Title}`, `{XAcc}`, `{Combo}` 같은 값 넣기)
- 이미지 · 웹페이지(방송 위젯) · 웹캠 항목 (회전 · 반전 · 자르기)
- 포토샵 같은 화면 편집기: 8방향 크기 조절, 자석 정렬, 레이어, 실행 취소, 화면 맞춤
- 항목마다 글꼴 지정 (Windows 글꼴, 모드 `Font` 폴더, 또는 파일 경로)
- 리듬게임 스타일 결과 카드 (XPerfect 지원, 랭크 표시)
- 디스코드 상태 표시, OBS 장면 전환 · 녹화 시작/중지 (obs-websocket v5)
- 프리셋 · 단축키
- 자동 업데이트 (GitHub 릴리스, 다음 실행 때 적용)
- 최신 버전과 Steam 베타 `older` 버전 모두 지원

## 설치
릴리스의 `NowOverlayer_Setup_v*.exe` 를 실행하거나, 빌드한 `NowOverlayer.dll` 과 `Info.json` 을
`A Dance of Fire and Ice\Mods\NowOverlayer` 에 넣으세요. 설정 창은 `Ctrl+F10` 입니다.

## 빌드
- .NET SDK, 얼불춤 + UnityModManager 설치 필요
- `NowOverlayer.csproj` 의 `AdofaiDir` 를 게임 폴더로 맞춘 뒤 `dotnet build -c Release`
- 웹페이지 항목 도우미(`web/`)는 WebView2 를 쓰는 별도 exe 입니다

## 글꼴
기본 글꼴은 넥슨 **메이플스토리 Bold**, 판정 · 곡 정보는 **고도 마음체(godoMaum)** 입니다.
설치 파일과 릴리스에는 들어 있고, 저장소에는 포함하지 않으니 빌드할 때는 `Font\` 에 넣어 주세요.
