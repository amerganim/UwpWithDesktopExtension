using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace WPF
{
    /// <summary>
    /// Interaction logic for App.xaml.
    /// Enforces a single running instance and routes command-line driven
    /// requests (e.g. "parameters") to the already-running instance via Win32 messages.
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static readonly Mutex InstanceMutex = new(true, "WPF_SINGLE_INSTANCE");
        private bool _shuttingDown;

        public App()
        {
            string[] args = Environment.GetCommandLineArgs();

            // The UWP app launches us with a parameter group (e.g. "parameters") to
            // request a custom notification. Forward that to the live instance and exit.
            if (args.Length > 1)
            {
                string lastArg = args[^1];
                Debug.WriteLine($"Parameter received: count={args.Length}, last='{lastArg}'");

                if (lastArg.Contains("parameter"))
                {
                    User32API.SendMessage(User32API.HWND_BROADCAST, User32API.WM_SHOWNOTI, IntPtr.Zero, IntPtr.Zero);
                    _shuttingDown = true;
                    Shutdown();
                    return;
                }
            }

            // Single-instance guard: if another instance owns the mutex, ask it to surface its
            // window (this is how launching the UWP app brings up the WPF window) and exit.
            if (!InstanceMutex.WaitOne(TimeSpan.Zero, true))
            {
                LogManager.Instance.WriteLogs("Another instance is already running; signaling WM_SHOWME.");
                User32API.SendMessage(User32API.HWND_BROADCAST, User32API.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                _shuttingDown = true;
                Shutdown();
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (_shuttingDown)
            {
                return;
            }

            // The app lives in the tray, so it must not exit just because no window is visible.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Tray-only startup: MainWindow creates the tray icon and opens the app-service (IPC)
            // connection, but we do NOT show the window. We only realize its native handle so it can
            // receive the WM_SHOWME broadcast (sent when the user launches the UWP app). The window
            // is shown on demand from the tray menu or WM_SHOWME.
            MainWindow = new MainWindow();
            new WindowInteropHelper(MainWindow).EnsureHandle();
        }
    }
}
