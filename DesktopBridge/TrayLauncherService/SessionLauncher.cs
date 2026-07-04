using System.ComponentModel;
using System.Runtime.InteropServices;
using static TrayLauncherService.NativeMethods;

namespace TrayLauncherService
{
    /// <summary>
    /// Launches the native TrayHelper in the interactive user session WITH package identity, by
    /// running its AppExecutionAlias. Launching the alias (rather than a bare exe) gives the process
    /// package identity, and only the tray icon appears - no UWP UI.
    ///
    /// A LocalSystem service runs in session 0, so it duplicates the active user's token and uses
    /// CreateProcessAsUser to start the alias on that user's desktop.
    /// </summary>
    internal static class SessionLauncher
    {
        // Must match the Alias declared in WAPP/Package.appxmanifest (TrayHelper AppExecutionAlias).
        private const string AliasExeName = "DesktopBridgeTray.exe";

        /// <summary>
        /// Launches the tray helper in the active console session.
        /// Returns true only if the helper process was actually created.
        /// </summary>
        public static bool LaunchInActiveSession()
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                ServiceLog.Write("No active console session; nothing to launch.");
                return false;
            }

            return LaunchInSession(sessionId);
        }

        /// <summary>
        /// Launches the tray helper in the given session. Returns true if the process was created.
        /// </summary>
        public static bool LaunchInSession(uint sessionId)
        {
            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            var processInfo = default(PROCESS_INFORMATION);
            bool launched = false;

            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    ServiceLog.Write($"WTSQueryUserToken failed for session {sessionId}: {LastError()}");
                    return false;
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
                    return false;
                }

                string? aliasPath = ResolveAliasPath(primaryToken);
                if (aliasPath is null || !File.Exists(aliasPath))
                {
                    ServiceLog.Write($"Tray app alias not found (resolved: '{aliasPath ?? "<null>"}'). " +
                        "Ensure the package is installed for the logged-on user.");
                    return false;
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
                    // Suppress the "app starting" spinning-ring cursor for the windowless tray launch.
                    dwFlags = STARTF_FORCEOFFFEEDBACK,
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

                launched = created;
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

            return launched;
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
