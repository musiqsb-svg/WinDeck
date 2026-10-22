using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>SendInput 으로 문장·키 조합을 현재 포커스된 창에 보낸다.</summary>
    public static class InputSender
    {
        const ushort VK_RETURN = 0x0D;
        const ushort VK_TAB = 0x09;
        const ushort VK_SHIFT = 0x10;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_MENU = 0x12;
        const ushort VK_LWIN = 0x5B;
        const ushort VK_V = 0x56;

        static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));

        /// <summary>유니코드 문자를 한 글자씩 입력한다. 한글도 IME 상태와 상관없이 그대로 들어간다.</summary>
        public static void TypeText(string text, bool pressEnter)
        {
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            var batch = new List<Native.INPUT>();
            foreach (char ch in text)
            {
                if (ch == '\n') AddPress(batch, VK_RETURN);
                else if (ch == '\t') AddPress(batch, VK_TAB);
                else
                {
                    batch.Add(Unicode(ch, false));
                    batch.Add(Unicode(ch, true));
                }
                if (batch.Count >= 64)
                {
                    Send(batch);
                    batch.Clear();
                    Thread.Sleep(8);
                }
            }
            if (pressEnter) AddPress(batch, VK_RETURN);
            Send(batch);
        }

        public static void SendCombo(KeyCombo combo)
        {
            var list = new List<Native.INPUT>();
            var mods = new List<ushort>();
            if (combo.Ctrl) mods.Add(VK_CONTROL);
            if (combo.Shift) mods.Add(VK_SHIFT);
            if (combo.Alt) mods.Add(VK_MENU);
            if (combo.Win) mods.Add(VK_LWIN);
            foreach (ushort m in mods) list.Add(Key(m, false));
            list.Add(Key(combo.Key, false));
            list.Add(Key(combo.Key, true));
            for (int i = mods.Count - 1; i >= 0; i--) list.Add(Key(mods[i], true));
            Send(list);
        }

        static bool IsDown(int vk)
        {
            return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>Ctrl·Shift·Alt·Win 중 하나라도 눌려 있는지.</summary>
        public static bool ModifiersDown()
        {
            return IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(0x5C);
        }

        public static bool AltOrWinDown()
        {
            return IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(0x5C);
        }

        /// <summary>
        /// 아무 기능 없는 키(0xE8)를 한 번 보낸다. Alt·Win 만 눌렀다 뗀 것으로 보이지 않게 해서
        /// 앞 창의 메뉴 표시줄이나 시작 메뉴가 열리는 것을 막는다.
        /// </summary>
        public static void SendMaskKey()
        {
            var list = new List<Native.INPUT>();
            AddPress(list, 0xE8);
            Send(list);
        }

        public static void SendSequence(IList<KeyCombo> sequence)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                SendCombo(sequence[i]);
                if (i < sequence.Count - 1) Thread.Sleep(60);
            }
        }

        // ---- 붙여넣기 방식 ----
        static IDataObject savedClipboard;
        static bool restorePending;
        static System.Windows.Forms.Timer restoreTimer;

        /// <summary>클립보드에 문장을 넣고 Ctrl+V 로 붙여넣은 뒤, 잠시 후 원래 클립보드를 되돌린다.</summary>
        public static void PasteText(string text, bool pressEnter)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!restorePending) savedClipboard = BackupClipboard();
            if (!Retry(delegate { Clipboard.SetText(text); }))
                throw new ActionException("클립보드를 사용할 수 없습니다. 잠시 후 다시 시도하세요.");
            Thread.Sleep(30);
            SendCombo(new KeyCombo { Ctrl = true, Key = VK_V });
            if (pressEnter)
            {
                Thread.Sleep(80);
                var list = new List<Native.INPUT>();
                AddPress(list, VK_RETURN);
                Send(list);
            }
            ScheduleRestore();
        }

        static void ScheduleRestore()
        {
            restorePending = true;
            if (restoreTimer == null)
            {
                restoreTimer = new System.Windows.Forms.Timer { Interval = 800 };
                restoreTimer.Tick += delegate
                {
                    restoreTimer.Stop();
                    restorePending = false;
                    IDataObject data = savedClipboard;
                    savedClipboard = null;
                    if (data != null) Retry(delegate { Clipboard.SetDataObject(data, true); });
                };
            }
            restoreTimer.Stop();
            restoreTimer.Start();
        }

        static IDataObject BackupClipboard()
        {
            try
            {
                IDataObject src = Clipboard.GetDataObject();
                if (src == null) return null;
                var copy = new DataObject();
                int count = 0;
                foreach (string format in src.GetFormats(false))
                {
                    try
                    {
                        object d = src.GetData(format, false);
                        if (d != null)
                        {
                            copy.SetData(format, false, d);
                            count++;
                        }
                    }
                    catch (Exception) { }
                }
                return count > 0 ? copy : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool Retry(Action action)
        {
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception)
                {
                    Thread.Sleep(40);
                }
            }
            return false;
        }

        // ---- low level ----
        static void AddPress(List<Native.INPUT> list, ushort vk)
        {
            list.Add(Key(vk, false));
            list.Add(Key(vk, true));
        }

        static Native.INPUT Key(ushort vk, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.U.ki.wVk = vk;
            input.U.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
            if (IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            input.U.ki.dwFlags = flags;
            return input;
        }

        static Native.INPUT Unicode(char ch, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.U.ki.wVk = 0;
            input.U.ki.wScan = ch;
            input.U.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            return input;
        }

        static bool IsExtended(ushort vk)
        {
            if (vk >= 0x21 && vk <= 0x28) return true;   // PgUp PgDn End Home 방향키
            if (vk >= 0xA6 && vk <= 0xB7) return true;   // 브라우저·볼륨·미디어 키
            switch (vk)
            {
                case 0x2C: // PrintScreen
                case 0x2D: // Insert
                case 0x2E: // Delete
                case 0x5B: // LWin
                case 0x5C: // RWin
                case 0x5D: // Apps
                case 0x6F: // Num /
                case 0x90: // NumLock
                case 0xA3: // RCtrl
                case 0xA5: // RAlt
                    return true;
            }
            return false;
        }

        static void Send(List<Native.INPUT> list)
        {
            if (list.Count == 0) return;
            Native.INPUT[] arr = list.ToArray();
            uint sent = Native.SendInput((uint)arr.Length, arr, InputSize);
            if (sent != arr.Length)
                throw new ActionException("키 입력을 보내지 못했습니다.\n관리자 권한으로 실행 중인 창에는 입력할 수 없습니다.");
        }
    }

    /// <summary>사용자에게 그대로 보여줄 수 있는 실행 오류.</summary>
    public class ActionException : Exception
    {
        public ActionException(string message) : base(message) { }
    }
}
