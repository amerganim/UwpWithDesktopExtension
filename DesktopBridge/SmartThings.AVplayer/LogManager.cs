using System.IO;

namespace WPF
{
    /// <summary>
    /// Simple thread-safe file logger for the WPF full-trust process.
    /// </summary>
    public sealed class LogManager
    {
        private static readonly Lazy<LogManager> LazyInstance = new(() => new LogManager());
        public static LogManager Instance => LazyInstance.Value;

        private readonly object _gate = new();
        private readonly string _filePath;

        private LogManager()
        {
            string directoryPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _filePath = Path.Combine(directoryPath, "DesktopBridgeWPF.txt");
        }

        public void WriteLogs(string logMessage)
        {
            try
            {
                lock (_gate)
                {
                    File.AppendAllText(_filePath, $"{DateTime.Now} WPF : {logMessage}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
