using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace WinDeck
{
    public static class ActionTypes
    {
        public const string None = "none";
        public const string Folder = "folder";
        public const string Url = "url";
        public const string Text = "text";
        public const string Hotkey = "hotkey";
        public const string Run = "run";
        public const string Page = "page";

        public static readonly string[] All = { None, Folder, Url, Text, Hotkey, Run, Page };

        public static readonly string[] Names =
        {
            "없음", "폴더 열기", "링크(웹사이트) 열기", "문장 입력", "단축키 실행", "프로그램·파일 실행", "페이지 이동"
        };

        public static string DisplayName(string action)
        {
            int i = Array.IndexOf(All, action);
            return i < 0 ? Names[0] : Names[i];
        }
    }

    public class DeckButton
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public string Args { get; set; }
        public bool PressEnter { get; set; }
        public bool UsePaste { get; set; }
        /// <summary>버튼 이미지(PNG, base64). 백업 파일 하나로 옮길 수 있도록 설정에 직접 저장한다.</summary>
        public string IconData { get; set; }
        public string BackColor { get; set; }
        public string ForeColor { get; set; }
        /// <summary>패널을 보지 않고 이 버튼을 실행하는 전역 단축키. 빈 문자열이면 없음.</summary>
        public string Hotkey { get; set; }

        public DeckButton()
        {
            Title = "";
            Description = "";
            Action = ActionTypes.None;
            Target = "";
            Args = "";
            IconData = "";
            BackColor = "";
            ForeColor = "";
            Hotkey = "";
        }

        [ScriptIgnore]
        public bool IsEmpty
        {
            get
            {
                return Action == ActionTypes.None
                    && string.IsNullOrWhiteSpace(Title)
                    && string.IsNullOrEmpty(IconData);
            }
        }

        public DeckButton Clone()
        {
            return (DeckButton)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Title == null) Title = "";
            if (Description == null) Description = "";
            if (Target == null) Target = "";
            if (Args == null) Args = "";
            if (IconData == null) IconData = "";
            if (BackColor == null) BackColor = "";
            if (ForeColor == null) ForeColor = "";
            if (Hotkey == null) Hotkey = "";
            IconData = IconLibrary.ToReference(IconData);
            if (Array.IndexOf(ActionTypes.All, Action) < 0) Action = ActionTypes.None;
        }
    }

    public class DeckPage
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>이 페이지로 바로 이동하는 전역 단축키. 빈 문자열이면 없음.</summary>
        public string Hotkey { get; set; }
        public List<DeckButton> Buttons { get; set; }

        public DeckPage()
        {
            Id = NewId();
            Name = "";
            Hotkey = "";
            Buttons = new List<DeckButton>();
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        public static DeckPage CreateEmpty(string name)
        {
            var p = new DeckPage { Name = name };
            for (int i = 0; i < AppConfig.MaxKeyCount; i++) p.Buttons.Add(new DeckButton());
            return p;
        }
    }

    /// <summary>패널 배경 (가로·세로 배치마다 하나).</summary>
    public class PanelBackground
    {
        public const string ModeDefault = "default";
        public const string ModeColor = "color";
        public const string ModeImage = "image";
        public const string FitFill = "fill";
        public const string FitFit = "fit";
        public const string FitStretch = "stretch";

        public string Mode { get; set; }
        public string Color { get; set; }
        /// <summary>패널 크기에 맞게 줄인 JPEG (base64). 원본은 보관하지 않는다.</summary>
        public string ImageData { get; set; }
        public string Fit { get; set; }
        /// <summary>이미지 위에 덮는 어둡기 (0~80%).</summary>
        public int Dim { get; set; }

        public PanelBackground()
        {
            Mode = ModeDefault;
            Color = "";
            ImageData = "";
            Fit = FitFill;
            Dim = 30;
        }

        public PanelBackground Clone()
        {
            return (PanelBackground)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Color == null) Color = "";
            if (ImageData == null) ImageData = "";
            if (Mode != ModeColor && Mode != ModeImage) Mode = ModeDefault;
            if (Mode == ModeImage && ImageData.Length == 0) Mode = ModeDefault;
            if (Fit != FitFit && Fit != FitStretch) Fit = FitFill;
            Dim = Math.Max(0, Math.Min(80, Dim));
        }
    }

    public class AppConfig
    {
        /// <summary>페이지마다 보관하는 버튼 칸 수 (보이는 칸은 ButtonCount).</summary>
        public const int MaxKeyCount = 16;
        public static readonly int[] ButtonCounts = { 8, 12, 16 };
        public const string DefaultHotkey = "Ctrl+`";
        public const string DefaultPrevPageHotkey = "Ctrl+Alt+Left";
        public const string DefaultNextPageHotkey = "Ctrl+Alt+Right";

        public int Version { get; set; }
        public string Hotkey { get; set; }
        public string PrevPageHotkey { get; set; }
        public string NextPageHotkey { get; set; }
        /// <summary>보이기 단축키로 패널을 열 때 마우스 커서 옆에 표시.</summary>
        public bool ShowAtCursor { get; set; }
        public int KeySize { get; set; }
        public bool Vertical { get; set; }
        public int Opacity { get; set; }
        public bool TopMost { get; set; }
        public bool HasPosition { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public int CurrentPage { get; set; }
        /// <summary>모든 페이지 공통 버튼 수 8·12·16. 줄여도 뒤쪽 버튼은 지우지 않고 숨겨서 보관한다.</summary>
        public int ButtonCount { get; set; }
        public PanelBackground BackgroundH { get; set; }
        public PanelBackground BackgroundV { get; set; }
        /// <summary>세로 배치에도 가로 배치 배경을 쓴다 (이미지는 자동으로 잘라 맞춤).</summary>
        public bool BackgroundVSameAsH { get; set; }
        /// <summary>색 선택 창의 사용자 지정 색 16칸 (Windows COLORREF).</summary>
        public List<int> CustomColors { get; set; }
        /// <summary>최근 고른 색 (#RRGGBB, 최신순 최대 8개).</summary>
        public List<string> RecentColors { get; set; }
        public List<DeckPage> Pages { get; set; }
        /// <summary>이 파일을 마지막으로 저장한 WinDeck 버전 (업데이트 직후 자동 백업용).</summary>
        public string SavedByVersion { get; set; }
        /// <summary>하루 한 번 GitHub 에서 새 버전을 확인한다.</summary>
        public bool AutoUpdateCheck { get; set; }
        /// <summary>마지막 업데이트 확인 시각 (UTC, ISO 8601).</summary>
        public string LastUpdateCheck { get; set; }
        /// <summary>'이 버전 건너뛰기'로 알림을 끈 버전.</summary>
        public string SkippedUpdateVersion { get; set; }
        /// <summary>설정 불러오기 창을 마지막으로 연 폴더.</summary>
        public string LastImportFolder { get; set; }

        public AppConfig()
        {
            Version = 1;
            Hotkey = DefaultHotkey;
            PrevPageHotkey = DefaultPrevPageHotkey;
            NextPageHotkey = DefaultNextPageHotkey;
            KeySize = 88;
            Opacity = 100;
            TopMost = true;
            ButtonCount = 8;
            BackgroundH = new PanelBackground();
            BackgroundV = new PanelBackground();
            BackgroundVSameAsH = true;
            CustomColors = new List<int>();
            RecentColors = new List<string>();
            Pages = new List<DeckPage>();
            AutoUpdateCheck = true;
        }

        /// <summary>지금 배치(가로/세로)에 쓰는 배경.</summary>
        [ScriptIgnore]
        public PanelBackground ActiveBackground
        {
            get { return Vertical && !BackgroundVSameAsH ? BackgroundV : BackgroundH; }
        }

        [ScriptIgnore]
        public int Columns
        {
            get { return Vertical ? ButtonCount / 4 : 4; }
        }

        [ScriptIgnore]
        public int Rows
        {
            get { return Vertical ? 4 : ButtonCount / 4; }
        }

        public static AppConfig CreateDefault()
        {
            var c = new AppConfig();
            var p = new DeckPage { Name = "기본" };
            string downloads = KnownFolders.Downloads
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            p.Buttons.Add(Make("다운로드", "다운로드 폴더를 엽니다.", ActionTypes.Folder, downloads, "", "downloads"));
            p.Buttons.Add(Make("바탕 화면", "바탕 화면 폴더를 엽니다.", ActionTypes.Folder,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "#0063B1", "folder"));
            p.Buttons.Add(Make("Google", "구글 검색 페이지를 엽니다.", ActionTypes.Url, "https://www.google.com", "#038387", "search"));
            p.Buttons.Add(Make("YouTube", "유튜브를 엽니다.", ActionTypes.Url, "https://www.youtube.com", "#C42B1C", "video"));
            p.Buttons.Add(Make("인사말", "커서가 있는 곳에 인사 문장을 입력합니다.", ActionTypes.Text,
                "안녕하세요. 확인 후 회신드리겠습니다.", "#107C10", "wave"));
            p.Buttons.Add(Make("바탕화면 보기", "모든 창을 내리고 바탕화면을 보여줍니다.", ActionTypes.Hotkey, "Win+D", "#5C2D91", "monitor"));
            p.Buttons.Add(Make("화면 캡처", "화면 캡처 도구를 엽니다.", ActionTypes.Hotkey, "Win+Shift+S", "#5C2D91", "camera"));
            p.Buttons.Add(Make("메모장", "메모장을 실행합니다.", ActionTypes.Run, "notepad.exe", "", "memo"));

            c.Pages.Add(p);
            c.Pages.Add(DeckPage.CreateEmpty("페이지 2"));
            c.Normalize();
            return c;
        }

        static DeckButton Make(string title, string desc, string action, string target, string back, string icon)
        {
            IconAsset asset = IconLibrary.Find(icon);
            return new DeckButton
            {
                Title = title,
                Description = desc,
                Action = action,
                Target = target,
                BackColor = back,
                IconData = asset == null ? "" : IconLibrary.Reference(asset)
            };
        }

        public void Normalize()
        {
            if (Hotkey == null) Hotkey = DefaultHotkey;
            if (PrevPageHotkey == null) PrevPageHotkey = DefaultPrevPageHotkey;
            if (NextPageHotkey == null) NextPageHotkey = DefaultNextPageHotkey;
            if (KeySize <= 0) KeySize = 88;
            KeySize = Math.Max(56, Math.Min(160, KeySize));
            if (Opacity <= 0) Opacity = 100;
            Opacity = Math.Max(30, Math.Min(100, Opacity));
            ButtonCount = ButtonCounts.OrderBy(n => Math.Abs(n - ButtonCount)).First();
            if (BackgroundH == null) BackgroundH = new PanelBackground();
            if (BackgroundV == null) BackgroundV = new PanelBackground();
            BackgroundH.Normalize();
            BackgroundV.Normalize();
            if (CustomColors == null) CustomColors = new List<int>();
            if (RecentColors == null) RecentColors = new List<string>();
            RecentColors.RemoveAll(c => string.IsNullOrWhiteSpace(c));
            if (RecentColors.Count > 8) RecentColors.RemoveRange(8, RecentColors.Count - 8);
            if (Pages == null) Pages = new List<DeckPage>();
            Pages.RemoveAll(p => p == null);
            if (Pages.Count == 0) Pages.Add(DeckPage.CreateEmpty("기본"));

            var ids = new HashSet<string>();
            for (int i = 0; i < Pages.Count; i++)
            {
                var p = Pages[i];
                if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id))
                {
                    p.Id = DeckPage.NewId();
                    ids.Add(p.Id);
                }
                if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "페이지 " + (i + 1);
                if (p.Hotkey == null) p.Hotkey = "";
                if (p.Buttons == null) p.Buttons = new List<DeckButton>();
                for (int j = 0; j < p.Buttons.Count; j++)
                {
                    if (p.Buttons[j] == null) p.Buttons[j] = new DeckButton();
                    p.Buttons[j].Normalize();
                }
                while (p.Buttons.Count < MaxKeyCount) p.Buttons.Add(new DeckButton());
                if (p.Buttons.Count > MaxKeyCount) p.Buttons.RemoveRange(MaxKeyCount, p.Buttons.Count - MaxKeyCount);
            }
            CurrentPage = Math.Max(0, Math.Min(Pages.Count - 1, CurrentPage));
        }
    }
}
