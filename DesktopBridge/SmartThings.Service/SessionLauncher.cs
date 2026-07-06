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
        // Aliases declared in WAPP/Package.appxmanifest.
        public const string TrayAlias = "DesktopBridgeTray.exe";
        public const string WpfAlias  = "DesktopBridgeWpf.exe";

        /// <summary>
        /// Gets the id of the active console session, if a user is signed in.
        /// </summary>
        public static bool TryGetActiveSession(out uint sessionId)
        {
            sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                ServiceLog.Write("No active console session.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Launches the given alias in the active console session.
        /// Returns true only if the process was actually created.
        /// </summary>
        public static bool LaunchInActiveSession(string aliasExeName = TrayAlias)
        {
            return TryGetActiveSession(out uint sessionId) && LaunchInSession(sessionId, aliasExeName);
        }

        /// <summary>
        /// Launches the given alias in the given session. Returns true if the process was created.
        /// </summary>
        public static bool LaunchInSession(uint sessionId, string aliasExeName)
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

                string? aliasPath = ResolveAliasPath(primaryToken, aliasExeName);
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
                    ServiceLog.Write($"Launched '{aliasPath}' in session {sessionId}.");
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
        private static string? ResolveAliasPath(IntPtr userToken, string aliasExeName)
        {
            return ExpandAliasInUserApps(userToken, aliasExeName);
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
