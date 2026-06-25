using System.ComponentModel;
using System.Runtime.InteropServices;
using static TrayLauncherService.NativeMethods;

namespace TrayLauncherService
{
    /// <summary>
    /// Launches the WPF tray app in the interactive user session WITH package identity, by running
    /// its AppExecutionAlias. Package identity is required for the app-service / IPC connection to
    /// the UWP app, and launching the alias (rather than the UWP app itself) brings up only the WPF
    /// tray icon - not the UWP UI.
    ///
    /// A LocalSystem service runs in session 0, so it duplicates the active user's token and uses
    /// CreateProcessAsUser to start the alias on that user's desktop.
    /// </summary>
    internal static class SessionLauncher
    {
        // Must match the Alias declared in WAPP/Package.appxmanifest (TrayApp AppExecutionAlias).
        private const string AliasExeName = "DesktopBridgeTray.exe";

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

        public static void LaunchInSession(uint sessionId)
        {
            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            var processInfo = default(PROCESS_INFORMATION);

            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    ServiceLog.Write($"WTSQueryUserToken failed for session {sessionId}: {LastError()}");
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
                    ServiceLog.Write($"DuplicateTokenEx failed: {LastError()}");
                    return;
                }

                string? aliasPath = ResolveAliasPath(primaryToken);
                if (aliasPath is null || !File.Exists(aliasPath))
                {
                    ServiceLog.Write($"Tray app alias not found (resolved: '{aliasPath ?? "<null>"}'). " +
                        "Ensure the package is installed for the logged-on user.");
                    return;
                }

                if (!CreateEnvironmentBlock(out environmentBlock, primaryToken, false))
                {
                    environmentBlock = IntPtr.Zero;
                    ServiceLog.Write("CreateEnvironmentBlock failed; continuing without it.");
                }

                var startupInfo = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = @"winsta0\default",
                };

                bool created = CreateProcessAsUser(
                    primaryToken,
                    aliasPath,
                    null,
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
                    ServiceLog.Write($"Launched tray app '{aliasPath}' in session {sessionId}.");
                }
                else
                {
                    ServiceLog.Write($"CreateProcessAsUser failed: {LastError()}");
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
        /// Resolves the full path to the per-user AppExecutionAlias:
        /// %LOCALAPPDATA%\Microsoft\WindowsApps\&lt;alias&gt;. The TRAYLAUNCHER_ALIAS environment
        /// variable can override it (full path or bare exe name) for manual testing.
        /// </summary>
        private static string? ResolveAliasPath(IntPtr userToken)
        {
            string? overridePath = Environment.GetEnvironmentVariable("TRAYLAUNCHER_ALIAS");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.IsPathRooted(overridePath) ? overridePath : ExpandAliasInUserApps(userToken, overridePath);
            }

            return ExpandAliasInUserApps(userToken, AliasExeName);
        }

        private static string? ExpandAliasInUserApps(IntPtr userToken, string exeName)
        {
            string? localAppData = GetKnownFolderPath(FOLDERID_LocalAppData, userToken);
            if (localAppData is null)
            {
                return null;
            }

            return Path.Combine(localAppData, "Microsoft", "WindowsApps", exeName);
        }

        private static string? GetKnownFolderPath(Guid folderId, IntPtr userToken)
        {
            IntPtr pathPtr = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(folderId, 0, userToken, out pathPtr) != 0)
                {
                    return null;
                }
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                if (pathPtr != IntPtr.Zero) CoTaskMemFree(pathPtr);
            }
        }

        private static string LastError() => new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }
}
