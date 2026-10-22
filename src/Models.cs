using System;
using System.Collections.Generic;
using System.IO;
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
            for (int i = 0; i < AppConfig.KeyCount; i++) p.Buttons.Add(new DeckButton());
            return p;
        }
    }

    public class AppConfig
    {
        public const int KeyCount = 8;
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
        public List<DeckPage> Pages { get; set; }

        public AppConfig()
        {
            Version = 1;
            Hotkey = DefaultHotkey;
            PrevPageHotkey = DefaultPrevPageHotkey;
            NextPageHotkey = DefaultNextPageHotkey;
            KeySize = 88;
            Opacity = 100;
            TopMost = true;
            Pages = new List<DeckPage>();
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
                IconData = asset == null ? "" : asset.Base64
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
                while (p.Buttons.Count < KeyCount) p.Buttons.Add(new DeckButton());
                if (p.Buttons.Count > KeyCount) p.Buttons.RemoveRange(KeyCount, p.Buttons.Count - KeyCount);
            }
            CurrentPage = Math.Max(0, Math.Min(Pages.Count - 1, CurrentPage));
        }
    }
}
