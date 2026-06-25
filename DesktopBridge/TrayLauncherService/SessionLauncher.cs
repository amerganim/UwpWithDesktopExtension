using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static TrayLauncherService.NativeMethods;

namespace TrayLauncherService
{
    /// <summary>
    /// Launches the packaged app in the interactive user session so the WPF tray app
    /// (and its app-service / IPC connection to the UWP app) starts with full package identity.
    ///
    /// A LocalSystem service runs in session 0 and cannot show UI, and a process it starts
    /// directly has no package identity. To preserve identity we activate the packaged app's
    /// AUMID via Explorer's "shell:AppsFolder" path inside the target user session. The UWP app
    /// then launches the WPF full-trust process through FullTrustProcessLauncher, which shows the
    /// tray icon and connects the app service.
    /// </summary>
    internal static class SessionLauncher
    {
        // AppId declared for the packaged Application entry in Package.appxmanifest.
        private const string PackageAppId = "App";

        /// <summary>
        /// Activates the packaged app in the currently active console session, if any.
        /// </summary>
        public static void LaunchInActiveSession()
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                ServiceLog.Write("No active console session; nothing to launch.");
                return;
            }

            LaunchInSession(sessionId);
        }

        /// <summary>
        /// Activates the packaged app in the given session.
        /// </summary>
        public static void LaunchInSession(uint sessionId)
        {
            string? aumid = ResolveAumid();
            if (string.IsNullOrEmpty(aumid))
            {
                ServiceLog.Write("Could not resolve the package AUMID; aborting launch. " +
                    "Set the TRAYLAUNCHER_AUMID environment variable or run the service with package identity.");
                return;
            }

            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            var processInfo = default(PROCESS_INFORMATION);

            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    ServiceLog.Write($"WTSQueryUserToken failed for session {sessionId}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    return;
                }

                if (!DuplicateTokenEx(
                        userToken,
                        MAXIMUM_ALLOWED,
                        IntPtr.Zero,
                        SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                        TOKEN_TYPE.TokenPrimary,
                        out primaryToken))
                {
                    ServiceLog.Write($"DuplicateTokenEx failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    return;
                }

                if (!CreateEnvironmentBlock(out environmentBlock, primaryToken, false))
                {
                    // Non-fatal: continue without a custom environment block.
                    environmentBlock = IntPtr.Zero;
                    ServiceLog.Write("CreateEnvironmentBlock failed; continuing without it.");
                }

                string explorer = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe");
                string commandLine = $"\"{explorer}\" shell:AppsFolder\\{aumid}";

                var startupInfo = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = @"winsta0\default",
                };

                bool created = CreateProcessAsUser(
                    primaryToken,
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CREATE_UNICODE_ENVIRONMENT | NORMAL_PRIORITY_CLASS,
                    environmentBlock,
                    null,
                    ref startupInfo,
                    out processInfo);

                if (created)
                {
                    ServiceLog.Write($"Activated packaged app '{aumid}' in session {sessionId}.");
                }
                else
                {
                    ServiceLog.Write($"CreateProcessAsUser failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                }
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"Unexpected error launching in session {sessionId}: {ex}");
            }
            finally
            {
                if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
                if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                if (environmentBlock != IntPtr.Zero) DestroyEnvironmentBlock(environmentBlock);
                if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        /// <summary>
        /// Resolves the packaged app's Application User Model ID. Prefers the service's own
        /// package identity; falls back to the TRAYLAUNCHER_AUMID environment variable for
        /// non-packaged (manual) testing.
        /// </summary>
        private static string? ResolveAumid()
        {
            uint length = 0;
            int rc = GetCurrentApplicationUserModelId(ref length, null);

            // ERROR_INSUFFICIENT_BUFFER (122) means we have identity; size buffer and retry.
            if (rc == 122 && length > 0)
            {
                var builder = new StringBuilder((int)length);
                if (GetCurrentApplicationUserModelId(ref length, builder) == ERROR_SUCCESS)
                {
                    return builder.ToString();
                }
            }

            string? overrideAumid = Environment.GetEnvironmentVariable("TRAYLAUNCHER_AUMID");
            if (!string.IsNullOrWhiteSpace(overrideAumid))
            {
                // Allow either a full AUMID or just the package family name (AppId appended).
                return overrideAumid.Contains('!') ? overrideAumid : $"{overrideAumid}!{PackageAppId}";
            }

            return null;
        }
    }
}
