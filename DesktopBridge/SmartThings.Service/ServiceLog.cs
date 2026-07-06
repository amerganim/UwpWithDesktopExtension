namespace TrayLauncherService
{
    /// <summary>
    /// Thread-safe file logger. LocalSystem cannot reliably write to a user's profile,
    /// so logs go under ProgramData.
    /// </summary>
    internal static class ServiceLog
    {
        private static readonly object Gate = new();
        private static readonly string LogPath = BuildLogPath();

        private static string BuildLogPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "DesktopBridge");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "TrayLauncherService.log");
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} SERVICE : {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never crash the service.
            }
        }
    }
}
