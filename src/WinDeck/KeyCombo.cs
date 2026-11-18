using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>"Ctrl+Shift+Esc" 같은 키 조합 한 개. 여러 개는 쉼표로 구분한다.</summary>
    public class KeyCombo
    {
        public bool Ctrl;
        public bool Shift;
        public bool Alt;
        public bool Win;
        public ushort Key;

        static readonly Dictionary<string, ushort> names = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<ushort, string> display = new Dictionary<ushort, string>();

        static KeyCombo()
        {
            Add(0x0D, "Enter", "Return");
            Add(0x1B, "Esc", "Escape");
            Add(0x09, "Tab");
            Add(0x20, "Space", "Spacebar");
            Add(0x08, "Backspace", "Back", "BS");
            Add(0x2E, "Delete", "Del");
            Add(0x2D, "Insert", "Ins");
            Add(0x24, "Home");
            Add(0x23, "End");
            Add(0x21, "PageUp", "PgUp", "Prior");
            Add(0x22, "PageDown", "PgDn", "Next");
            Add(0x26, "Up");
            Add(0x28, "Down");
            Add(0x25, "Left");
            Add(0x27, "Right");
            Add(0x2C, "PrintScreen", "PrtSc", "PrtScn", "Snapshot");
            Add(0x13, "Pause", "Break");
            Add(0x14, "CapsLock", "Capital");
            Add(0x90, "NumLock");
            Add(0x91, "ScrollLock", "Scroll");
            Add(0x5D, "Menu", "Apps", "ContextMenu");
            Add(0xAD, "VolumeMute", "Mute");
            Add(0xAE, "VolumeDown");
            Add(0xAF, "VolumeUp");
            Add(0xB0, "MediaNext", "NextTrack", "MediaNextTrack");
            Add(0xB1, "MediaPrev", "PrevTrack", "MediaPreviousTrack");
            Add(0xB2, "MediaStop");
            Add(0xB3, "MediaPlayPause", "PlayPause");
            Add(0x15, "한영", "HangulMode", "Hangul", "KanaMode");
            Add(0x19, "한자", "HanjaMode", "Hanja");
            Add(0xBC, "Comma", "Oemcomma");
            Add(0xBE, "Period", ".", "OemPeriod");
            Add(0xBD, "Minus", "-", "OemMinus");
            Add(0xBB, "=", "Equals", "Plus", "Oemplus");
            Add(0xBF, "/", "Slash", "OemQuestion");
            Add(0xDC, "\\", "Backslash", "OemPipe");
            Add(0xBA, ";", "Semicolon", "OemSemicolon");
            Add(0xDE, "'", "Quote", "OemQuotes");
            Add(0xC0, "`", "Backtick", "Tilde", "Oemtilde");
            Add(0xDB, "[", "OpenBracket", "OemOpenBrackets");
            Add(0xDD, "]", "CloseBracket", "OemCloseBrackets");
            for (int i = 0; i <= 9; i++) Add((ushort)(0x60 + i), "Num" + i, "NumPad" + i);
            Add(0x6A, "Num*", "Multiply");
            Add(0x6B, "Num+", "Add");
            Add(0x6D, "Num-", "Subtract");
            Add(0x6E, "Num.", "Decimal");
            Add(0x6F, "Num/", "Divide");
            for (int i = 1; i <= 24; i++) Add((ushort)(0x6F + i), "F" + i);
            for (char ch = 'A'; ch <= 'Z'; ch++) Add(ch, ch.ToString());
            for (char ch = '0'; ch <= '9'; ch++) Add(ch, ch.ToString(), "D" + ch);
            Add(0x10, "ShiftKey");
            Add(0x11, "ControlKey");
            Add(0x12, "AltKey");
            Add(0x5B, "WinKey", "LWin");
        }

        static void Add(ushort vk, params string[] aliases)
        {
            if (!display.ContainsKey(vk)) display[vk] = aliases[0];
            foreach (string a in aliases) names[a] = vk;
        }

        public static KeyCombo Parse(string text)
        {
            if (text == null || text.Trim().Length == 0) throw new FormatException("키가 비어 있습니다.");
            var c = new KeyCombo();
            foreach (string part in text.Split('+'))
            {
                string raw = part.Trim();
                if (raw.Length == 0) throw new FormatException("'" + text.Trim() + "' 형식이 올바르지 않습니다.");
                string t = raw.ToLowerInvariant();
                if (t == "ctrl" || t == "control" || t == "ctl" || t == "컨트롤") { c.Ctrl = true; continue; }
                if (t == "shift" || t == "시프트") { c.Shift = true; continue; }
                if (t == "alt" || t == "알트") { c.Alt = true; continue; }
                if (t == "win" || t == "windows" || t == "윈도우" || t == "super" || t == "meta") { c.Win = true; continue; }
                if (c.Key != 0) throw new FormatException("'" + text.Trim() + "' 에 일반 키가 두 개 이상 있습니다.");

                ushort vk;
                if (!names.TryGetValue(raw, out vk))
                {
                    Keys k;
                    if (!char.IsDigit(raw[0]) && Enum.TryParse(raw, true, out k) && (int)k > 0 && (int)k < 256)
                        vk = (ushort)k;
                    else
                        throw new FormatException("'" + raw + "' 은(는) 알 수 없는 키 이름입니다.");
                }
                c.Key = vk;
            }

            if (c.Key == 0)
            {
                // "Win" 처럼 수식키만 있으면 그 키 자체를 누른다.
                if (c.Win) { c.Win = false; c.Key = 0x5B; }
                else if (c.Alt) { c.Alt = false; c.Key = 0x12; }
                else if (c.Shift) { c.Shift = false; c.Key = 0x10; }
                else if (c.Ctrl) { c.Ctrl = false; c.Key = 0x11; }
            }
            return c;
        }

        public static bool TryParse(string text, out KeyCombo combo)
        {
            try
            {
                combo = Parse(text);
                return true;
            }
            catch (FormatException)
            {
                combo = null;
                return false;
            }
        }

        public static List<KeyCombo> ParseSequence(string text)
        {
            var list = new List<KeyCombo>();
            foreach (string part in (text ?? "").Split(','))
            {
                if (part.Trim().Length == 0) continue;
                list.Add(Parse(part));
            }
            if (list.Count == 0) throw new FormatException("단축키가 비어 있습니다.");
            return list;
        }

        public static string NormalizeSequence(string text)
        {
            return string.Join(", ", ParseSequence(text).Select(k => k.ToString()).ToArray());
        }

        public uint HotkeyModifiers
        {
            get
            {
                uint m = 0;
                if (Ctrl) m |= Native.MOD_CONTROL;
                if (Shift) m |= Native.MOD_SHIFT;
                if (Alt) m |= Native.MOD_ALT;
                if (Win) m |= Native.MOD_WIN;
                return m;
            }
        }

        public bool HasModifier
        {
            get { return Ctrl || Shift || Alt || Win; }
        }

        public override string ToString()
        {
            if (!HasModifier)
            {
                // 수식키 단독 입력은 Parse 가 받아들이는 짧은 이름으로 표시한다.
                if (Key == 0x5B) return "Win";
                if (Key == 0x11) return "Ctrl";
                if (Key == 0x12) return "Alt";
                if (Key == 0x10) return "Shift";
            }
            var parts = new List<string>();
            if (Win) parts.Add("Win");
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            string n;
            parts.Add(display.TryGetValue(Key, out n) ? n : ((Keys)Key).ToString());
            return string.Join("+", parts.ToArray());
        }
    }
}
