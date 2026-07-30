using System.Diagnostics;
using System.ServiceProcess;

namespace TrayLauncherService
{
    /// <summary>
    /// Windows service (LocalSystem, auto-start). On start (after install / at boot / at logon) it
    /// launches, in the interactive user session:
    ///   - the native TrayHelper (stays running - owns the tray icon), and
    ///   - the WPF process (only needed briefly).
    /// After a short delay it kills WPF, then STOPS ITSELF - its work is done, so it consumes no
    /// memory while idle.
    ///
    /// FAST STARTUP: Windows Fast Startup (the default for "Shut down") hibernates session 0 and
    /// restores it on the next power-on instead of doing a cold boot, so auto-start services are NOT
    /// re-run. A self-stopped service therefore stays stopped after a shutdown+power-on and would
    /// never see the logon - so the tray would not appear (a full "Restart" bypasses Fast Startup,
    /// which is why restarting worked but shutdown+power-on did not).
    ///
    /// To fix that WITHOUT keeping the service resident, the service registers a Task Scheduler
    /// "at log on" task (see <see cref="EnsureLogonTaskRegistered"/>) whose action is to start this
    /// service. Logon triggers DO fire on the interactive logon that follows a Fast-Startup resume,
    /// so on every sign-in the task starts the service, the service launches the tray, then stops
    /// again. The task is removed by Uninstall.ps1.
    /// </summary>
    public sealed class TrayService : ServiceBase
    {
        // Must match the desktop6:Service Name in SmartThings.WAPP/Package.appxmanifest.
        public const string ServiceNameConst = "SmartThings.Service";

        // Name of the Task Scheduler logon task that restarts this service on each sign-in.
        // Keep in sync with build/Uninstall.ps1.
        private const string LogonTaskName = "DesktopBridge Tray Logon";

        // How long the player is allowed to run before the service kills it.
        private static readonly TimeSpan WpfLifetime = TimeSpan.FromSeconds(5);

        // Process image name (no .exe) of the player - the AssemblyName of SmartThings.AVplayer.
        private const string PlayerProcessName = "SmartThings.AVplayer";

        // Process image name (no .exe) of the native tray helper - the Executable in the manifest's
        // SmartThings.Tray application entry. Used to avoid launching a second tray icon.
        private const string TrayProcessName = "SmartThings.Tray";

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
                // Make sure the logon task exists so we are restarted on future sign-ins (this is
                // what survives Fast Startup). Idempotent and cheap.
                EnsureLogonTaskRegistered();

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
        /// Launches the tray helper (+ WPF) in the session if it isn't already there, kills WPF after
        /// a short delay, then stops the service. If the tray is already running in the session this
        /// is a no-op except for stopping the service. The logon task (registered in OnStart) restarts
        /// the service on the next sign-in, including after a Fast-Startup resume.
        /// </summary>
        private void LaunchThenStop(uint sessionId)
        {
            if (IsTrayRunningInSession(sessionId))
            {
                ServiceLog.Write($"Tray already running in session {sessionId}; nothing to launch.");
                StopSelf();
                return;
            }

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
        /// True if a tray helper process is already running in the given session. The service runs in
        /// session 0 and can see processes in every session, so we filter by SessionId to avoid
        /// treating a tray in another user's session as ours.
        /// </summary>
        private static bool IsTrayRunningInSession(uint sessionId)
        {
            Process[] trays = Process.GetProcessesByName(TrayProcessName);
            try
            {
                foreach (Process process in trays)
                {
                    try
                    {
                        if ((uint)process.SessionId == sessionId)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Process may have exited between enumeration and access; ignore it.
                    }
                }
                return false;
            }
            finally
            {
                foreach (Process process in trays)
                {
                    process.Dispose();
                }
            }
        }

        /// <summary>
        /// Registers (or refreshes) a Task Scheduler task that starts this service at every user
        /// logon. Runs as SYSTEM so it can start the service. This is the piece that makes the tray
        /// reappear after a Fast-Startup "Shut down -> power on", where auto-start services are not
        /// re-run. Best effort: if it fails the service still works for the current session.
        /// </summary>
        private static void EnsureLogonTaskRegistered()
        {
            try
            {
                string scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                // schtasks /Create /TN "<task>" /TR "<sc.exe> start <service>" /SC ONLOGON
                //          /RU SYSTEM /RL HIGHEST /F
                startInfo.ArgumentList.Add("/Create");
                startInfo.ArgumentList.Add("/TN");
                startInfo.ArgumentList.Add(LogonTaskName);
                startInfo.ArgumentList.Add("/TR");
                startInfo.ArgumentList.Add($"{scPath} start {ServiceNameConst}");
                startInfo.ArgumentList.Add("/SC");
                startInfo.ArgumentList.Add("ONLOGON");
                startInfo.ArgumentList.Add("/RU");
                startInfo.ArgumentList.Add("SYSTEM");
                startInfo.ArgumentList.Add("/RL");
                startInfo.ArgumentList.Add("HIGHEST");
                startInfo.ArgumentList.Add("/F");

                using Process? proc = Process.Start(startInfo);
                if (proc is null)
                {
                    ServiceLog.Write("Could not start schtasks.exe to register the logon task.");
                    return;
                }

                proc.WaitForExit(TimeSpan.FromSeconds(15));
                if (proc.ExitCode == 0)
                {
                    ServiceLog.Write($"Logon task '{LogonTaskName}' registered.");
                }
                else
                {
                    string err = proc.StandardError.ReadToEnd().Trim();
                    ServiceLog.Write($"schtasks returned {proc.ExitCode} registering the logon task. {err}");
                }
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"EnsureLogonTaskRegistered failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Terminates the player process (image name "SmartThings.AVplayer.exe"). LocalSystem can
        /// terminate the user's process. Best effort.
        /// </summary>
        private static void KillWpf()
        {
            Process[] players = Process.GetProcessesByName(PlayerProcessName);
            if (players.Length == 0)
            {
                ServiceLog.Write($"No '{PlayerProcessName}' process found to kill.");
            }
            foreach (Process process in players)
            {
                try
                {
                    ServiceLog.Write($"Killing {PlayerProcessName} (pid {process.Id}).");
                    process.Kill();
                }
                catch (Exception ex)
                {
                    ServiceLog.Write($"Kill {PlayerProcessName} failed: {ex.Message}");
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
        /// Stops this service (nothing left to do after launching the tray - the logon task will
        /// restart it on the next sign-in). Runs on a background thread, so the service has already
        /// reported Running to the SCM.
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
