using System;
using System.Collections.Generic;

namespace WinDeck
{
    public enum HotkeyKind
    {
        TogglePanel,
        PrevPage,
        NextPage,
        Page,
        Button
    }

    /// <summary>설정에 들어 있는 전역 단축키 하나 (무엇을 하는 키인지 포함).</summary>
    public class HotkeyEntry
    {
        public HotkeyKind Kind;
        public string Combo;
        public int PageIndex = -1;
        public int ButtonIndex = -1;
        public string Label;

        public bool IsSameTarget(HotkeyKind kind, int pageIndex, int buttonIndex)
        {
            return Kind == kind && PageIndex == pageIndex && ButtonIndex == buttonIndex;
        }
    }

    /// <summary>패널 보이기·페이지 이동·페이지 바로가기·버튼 실행 단축키를 한곳에서 모으고 겹침을 검사한다.</summary>
    public static class HotkeyMap
    {
        /// <summary>"ctrl+alt+1" → "Ctrl+Alt+1". 비었거나 잘못된 값이면 null.</summary>
        public static string Normalize(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo) || combo.Contains(",")) return null;
            KeyCombo k;
            return KeyCombo.TryParse(combo, out k) ? k.ToString() : null;
        }

        public static List<HotkeyEntry> Collect(AppConfig cfg)
        {
            var list = new List<HotkeyEntry>();
            Add(list, HotkeyKind.TogglePanel, cfg.Hotkey, -1, -1, "패널 보이기/숨기기");
            Add(list, HotkeyKind.PrevPage, cfg.PrevPageHotkey, -1, -1, "이전 페이지");
            Add(list, HotkeyKind.NextPage, cfg.NextPageHotkey, -1, -1, "다음 페이지");
            for (int p = 0; p < cfg.Pages.Count; p++)
            {
                DeckPage page = cfg.Pages[p];
                Add(list, HotkeyKind.Page, page.Hotkey, p, -1, "'" + page.Name + "' 페이지로 이동");
                for (int b = 0; b < page.Buttons.Count; b++)
                {
                    DeckButton button = page.Buttons[b];
                    if (button == null || button.IsEmpty) continue;
                    string title = string.IsNullOrWhiteSpace(button.Title) ? (b + 1) + "번" : "'" + button.Title + "'";
                    Add(list, HotkeyKind.Button, button.Hotkey, p, b, "'" + page.Name + "' 페이지의 " + title + " 버튼");
                }
            }
            return list;
        }

        static void Add(List<HotkeyEntry> list, HotkeyKind kind, string combo, int page, int button, string label)
        {
            string normalized = Normalize(combo);
            if (normalized == null) return;
            list.Add(new HotkeyEntry { Kind = kind, Combo = normalized, PageIndex = page, ButtonIndex = button, Label = label });
        }

        /// <summary>같은 키를 이미 쓰는 다른 항목. 없으면 null.</summary>
        public static HotkeyEntry FindConflict(AppConfig cfg, string combo, HotkeyKind kind, int pageIndex, int buttonIndex)
        {
            string normalized = Normalize(combo);
            if (normalized == null) return null;
            foreach (HotkeyEntry e in Collect(cfg))
            {
                if (e.IsSameTarget(kind, pageIndex, buttonIndex)) continue;
                if (string.Equals(e.Combo, normalized, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }

        /// <summary>
        /// 단축키 입력값 검사. 문제가 없으면 null, 있으면 사용자에게 보여줄 문구.
        /// 수식키 없는 단일 키는 평소 입력을 가로채므로 막는다.
        /// </summary>
        public static string Validate(AppConfig cfg, string combo, HotkeyKind kind, int pageIndex, int buttonIndex)
        {
            if (string.IsNullOrWhiteSpace(combo)) return null;
            KeyCombo k;
            if (combo.Contains(",") || !KeyCombo.TryParse(combo, out k))
                return "단축키 형식이 올바르지 않습니다. 예: Ctrl+Alt+1";
            if (!k.HasModifier && !(k.Key >= 0x70 && k.Key <= 0x87))
                return "'" + k + "' 하나만으로는 쓸 수 없습니다. Ctrl·Alt·Shift·Win 과 함께 지정하세요. (F1~F24 는 단독 사용 가능)";
            HotkeyEntry conflict = FindConflict(cfg, combo, kind, pageIndex, buttonIndex);
            if (conflict != null)
                return "'" + k + "' 는 이미 [" + conflict.Label + "] 에 쓰이고 있습니다.";
            return null;
        }
    }
}
