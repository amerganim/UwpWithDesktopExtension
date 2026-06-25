using System.ServiceProcess;

namespace TrayLauncherService
{
    /// <summary>
    /// Windows service (LocalSystem, auto-start) that ensures the packaged tray app runs in
    /// the interactive user session. It launches on start and on each user logon / session
    /// connect / unlock so the tray icon and IPC connection are always available.
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
            // Launch on a background thread so we never block the SCM start timeout.
            ThreadPool.QueueUserWorkItem(_ => SessionLauncher.LaunchInActiveSession());
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
                    ThreadPool.QueueUserWorkItem(_ => SessionLauncher.LaunchInSession(sessionId));
                    break;
            }
        }

        protected override void OnStop()
        {
            ServiceLog.Write("Service stopping.");
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
