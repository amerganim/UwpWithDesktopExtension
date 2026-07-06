using System.Diagnostics;
using System.ServiceProcess;

namespace TrayLauncherService
{
    /// <summary>
    /// Windows service (LocalSystem, auto-start). On start (after install / at boot) and on user
    /// logon it launches, in the interactive user session:
    ///   - the native TrayHelper (stays running - owns the tray icon), and
    ///   - the WPF process (only needed briefly).
    /// After a short delay it kills WPF, then stops itself (its work is done). It is auto-start, so
    /// it runs again on the next boot; if no user is signed in yet it waits for logon.
    /// </summary>
    public sealed class TrayService : ServiceBase
    {
        // Must match the desktop6:Service Name in SmartThings.WAPP/Package.appxmanifest.
        public const string ServiceNameConst = "SmartThings.Service";

        // How long WPF is allowed to run before the service kills it.
        private static readonly TimeSpan WpfLifetime = TimeSpan.FromSeconds(5);

        public TrayService()
        {
            ServiceName = ServiceNameConst;
            CanHandleSessionChangeEvent = true;
            CanShutdown = true;
            CanStop = true;
        }

        protected override void OnStart(string[] args)
        {
            ServiceLog.Write("Service starting.");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (SessionLauncher.TryGetActiveSession(out uint sessionId))
                {
                    LaunchThenStop(sessionId);
                }
                else
                {
                    ServiceLog.Write("No user session yet (likely boot before logon); waiting for logon.");
                }
            });
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            ServiceLog.Write($"Session change: {changeDescription.Reason}, session {changeDescription.SessionId}.");

            switch (changeDescription.Reason)
            {
                case SessionChangeReason.SessionLogon:
                case SessionChangeReason.SessionUnlock:
                case SessionChangeReason.ConsoleConnect:
                case SessionChangeReason.RemoteConnect:
                    uint sessionId = (uint)changeDescription.SessionId;
                    ThreadPool.QueueUserWorkItem(_ => LaunchThenStop(sessionId));
                    break;
            }
        }

        /// <summary>
        /// Launches TrayHelper + WPF in the session, then (after a delay) kills WPF and stops the
        /// service. Only proceeds to kill/stop if the tray helper actually launched.
        /// </summary>
        private void LaunchThenStop(uint sessionId)
        {
            bool trayLaunched = SessionLauncher.LaunchInSession(sessionId, SessionLauncher.TrayAlias);
            bool wpfLaunched  = SessionLauncher.LaunchInSession(sessionId, SessionLauncher.WpfAlias);
            ServiceLog.Write($"Launch results: tray={trayLaunched}, wpf={wpfLaunched}.");

            if (!trayLaunched)
            {
                // Stay running so a later logon (or retry) can try again.
                return;
            }

            // WPF was only needed briefly; give it a moment, then terminate it.
            Thread.Sleep(WpfLifetime);
            KillWpf();

            StopSelf();
        }

        /// <summary>
        /// Terminates the WPF process (image name "WPF.exe"). LocalSystem can terminate the user's
        /// process. Best effort.
        /// </summary>
        private static void KillWpf()
        {
            foreach (Process process in Process.GetProcessesByName("WPF"))
            {
                try
                {
                    ServiceLog.Write($"Killing WPF (pid {process.Id}).");
                    process.Kill();
                }
                catch (Exception ex)
                {
                    ServiceLog.Write($"Kill WPF failed: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        protected override void OnStop()
        {
            ServiceLog.Write("Service stopping.");
        }

        /// <summary>
        /// Stops this service (nothing left to do after launching the tray). Runs on a background
        /// thread, so the service has already reported Running to the SCM.
        /// </summary>
        private void StopSelf()
        {
            try
            {
                using var controller = new ServiceController(ServiceNameConst);
                if (controller.Status is ServiceControllerStatus.StartPending)
                {
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
                if (controller.Status is ServiceControllerStatus.Running)
                {
                    ServiceLog.Write("Work done; stopping the service.");
                    controller.Stop();
                }
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"StopSelf failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs the launch logic once without the SCM, for manual testing from an elevated console.
        /// </summary>
        public static void RunInteractive()
        {
            ServiceLog.Write("Running interactively (console mode).");
            SessionLauncher.LaunchInActiveSession(SessionLauncher.TrayAlias);
            SessionLauncher.LaunchInActiveSession(SessionLauncher.WpfAlias);
        }
    }
}
