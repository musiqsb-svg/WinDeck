using System;
using System.Threading;

namespace WinDeck
{
    /// <summary>
    /// 실행 중인 WinDeck 하나를 가리키는 이름들(중복 실행 방지, 보이기·종료·업데이트 확인 신호, 레지스트리 위치).
    /// 자동 점검 때는 WINDECK_INSTANCE 환경 변수로 이름을 바꿔, 사용자가 쓰고 있는 WinDeck 과 겹치지 않게 한다.
    /// </summary>
    public static class AppInstance
    {
        public static string TestId
        {
            get
            {
                string id = Environment.GetEnvironmentVariable("WINDECK_INSTANCE");
                return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            }
        }

        public static bool IsTest
        {
            get { return TestId != null; }
        }

        static string Suffix
        {
            get { return IsTest ? "." + TestId : ""; }
        }

        // 설치 파일(installer\WinDeck.iss)도 같은 이름을 쓴다. 바꿀 때 함께 고칠 것.
        public static string MutexName
        {
            get { return @"Local\WinDeck.SingleInstance" + Suffix; }
        }

        public static string ShowEventName
        {
            get { return @"Local\WinDeck.ShowPanel" + Suffix; }
        }

        public static string ExitEventName
        {
            get { return @"Local\WinDeck.Exit" + Suffix; }
        }

        public static string UpdateEventName
        {
            get { return @"Local\WinDeck.CheckUpdate" + Suffix; }
        }

        /// <summary>HKCU 아래 WinDeck 이 쓰는 키 (마지막 설정 위치 기록).</summary>
        public static string RegistryKey
        {
            get { return IsTest ? @"Software\WinDeck-test\" + TestId : @"Software\WinDeck"; }
        }

        /// <summary>실행 중인 WinDeck 에 신호를 보낸다. 받을 WinDeck 이 없으면 false.</summary>
        public static bool Signal(string eventName)
        {
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(eventName)) ev.Set();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsRunning()
        {
            try
            {
                using (Mutex.OpenExisting(MutexName)) return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>실행 중인 WinDeck 이 끝날 때까지 기다린다. 끝났거나 원래 없으면 true.</summary>
        public static bool WaitForExit(int timeoutMs)
        {
            Mutex m;
            try
            {
                m = Mutex.OpenExisting(MutexName);
            }
            catch (Exception)
            {
                return true;
            }
            using (m)
            {
                if (!Acquire(m, timeoutMs)) return false;
                m.ReleaseMutex();
                return true;
            }
        }

        /// <summary>뮤텍스를 얻을 때까지 기다린다. 앞 프로세스가 놓지 않고 끝난 경우(abandoned)도 얻은 것으로 본다.</summary>
        public static bool Acquire(Mutex m, int timeoutMs)
        {
            try
            {
                return m.WaitOne(timeoutMs);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
    }
}
