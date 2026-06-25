using System;
using System.IO;

namespace DesktopBridge
{
    /// <summary>
    /// Simple thread-safe file logger for the UWP app.
    /// </summary>
    public sealed class LogManager
    {
        private static readonly Lazy<LogManager> lazyInstance = new Lazy<LogManager>(() => new LogManager());
        public static LogManager Instance => lazyInstance.Value;

        private readonly object _gate = new object();
        private readonly string _filePath;

        private LogManager()
        {
            string directoryPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _filePath = Path.Combine(directoryPath, "DesktopBridgeUWP.txt");
        }

        public void WriteLogs(string logMessage)
        {
            try
            {
                lock (_gate)
                {
                    File.AppendAllText(_filePath, DateTime.Now + " UWP : " + logMessage + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
