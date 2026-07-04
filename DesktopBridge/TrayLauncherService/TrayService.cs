using System.ServiceProcess;

namespace TrayLauncherService
{
    /// <summary>
    /// Windows service (LocalSystem, auto-start) whose only job is to launch the native TrayHelper
    /// in the interactive user session. Once it has launched the helper it has no further work, so
    /// it stops itself.
    ///
    /// If no user is signed in yet when the service starts (e.g. at boot), the launch is a no-op and
    /// the service keeps running until a user logs on (SessionLogon), launches then, and stops. It is
    /// auto-start, so it runs again on the next boot.
    /// </summary>
    public sealed class TrayService : ServiceBase
    {
        public const string ServiceNameConst = "TrayLauncherService";

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
            // Launch on a background thread so we never block the SCM start timeout, and stop the
            // service if the helper was actually launched.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (SessionLauncher.LaunchInActiveSession())
                {
                    StopSelf();
                }
                else
                {
                    ServiceLog.Write("No launch at start (likely no user session yet); waiting for logon.");
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
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (SessionLauncher.LaunchInSession(sessionId))
                        {
                            StopSelf();
                        }
                    });
                    break;
            }
        }

        protected override void OnStop()
        {
            ServiceLog.Write("Service stopping.");
        }

        /// <summary>
        /// Stops this service (it has nothing left to do after launching the tray). Runs on a
        /// background thread, so the service has already reported Running to the SCM.
        /// </summary>
        private void StopSelf()
        {
            try
            {
                using var controller = new ServiceController(ServiceNameConst);
                // The launch work means the service is already Running; guard just in case.
                if (controller.Status is ServiceControllerStatus.StartPending)
                {
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
                if (controller.Status is ServiceControllerStatus.Running)
                {
                    ServiceLog.Write("Tray launched; stopping the service (no further work).");
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
            SessionLauncher.LaunchInActiveSession();
        }
    }
}
