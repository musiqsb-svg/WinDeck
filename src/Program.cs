using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WinDeck
{
    static class Program
    {
        /// <summary>
        /// 명령줄:
        ///   --exit          실행 중인 WinDeck 을 설정 저장 후 끝낸다 (설치·빌드 스크립트용). 끝나면 0, 시간 초과 1.
        ///   --check-update  실행 중인 WinDeck 에서 업데이트를 바로 확인한다.
        ///   --restart       업데이트 직후 다시 실행: 앞 프로세스가 끝날 때까지 기다렸다가 시작한다.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            bool exitCmd = args.Contains("--exit");
            bool checkUpdate = args.Contains("--check-update");
            bool restart = args.Contains("--restart");

            if (exitCmd)
            {
                if (!AppInstance.Signal(AppInstance.ExitEventName)) return AppInstance.IsRunning() ? 1 : 0;
                return AppInstance.WaitForExit(10000) ? 0 : 1;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, AppInstance.MutexName, out createdNew))
            {
                if (!createdNew && restart) createdNew = AppInstance.Acquire(mutex, 20000);
                if (!createdNew)
                {
                    // 이미 실행 중이면 그 패널을 보이게 하고(또는 업데이트를 확인하게 하고) 끝낸다.
                    AppInstance.Signal(checkUpdate ? AppInstance.UpdateEventName : AppInstance.ShowEventName);
                    return 0;
                }

                try
                {
                    Run(checkUpdate);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
            return 0;
        }

        static void Run(bool checkUpdate)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ErrorLog.Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ErrorLog.Report(e.ExceptionObject as Exception);

            Updater.CleanupAfterUpdate();
            AppConfig cfg = ConfigStore.Load();
            ConfigStore.FindPreviousConfig();
            ConfigStore.RememberLocation();
            ConfigStore.BackupIfDue(cfg);
            if (!AppInstance.IsTest) AutoStart.RefreshPathIfEnabled();

            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInstance.ShowEventName);
            var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInstance.ExitEventName);
            var updateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInstance.UpdateEventName);
            var form = new DeckForm(cfg, true);
            if (checkUpdate) form.Shown += delegate { form.CheckForUpdatesNow(); };

            var listener = new Thread(() =>
            {
                var events = new WaitHandle[] { showEvent, exitEvent, updateEvent };
                try
                {
                    while (true)
                    {
                        int i = WaitHandle.WaitAny(events);
                        // 막 시작한 참이면 창이 만들어질 때까지 기다린다 (그 전에 온 신호를 잃지 않게).
                        for (int t = 0; t < 400 && !form.IsHandleCreated; t++) Thread.Sleep(50);
                        Action action = i == 0 ? form.ShowPanel : i == 1 ? form.RequestExit : (Action)form.CheckForUpdatesNow;
                        try { form.BeginInvoke(action); }
                        catch (InvalidOperationException) { }
                    }
                }
                catch (Exception) { }
            });
            listener.IsBackground = true;
            listener.Start();

            Application.Run(form);
        }
    }
}
