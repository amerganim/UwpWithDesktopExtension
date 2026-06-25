using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// Owns the tray icon and the bidirectional app-service (IPC) connection to the UWP app.
    /// </summary>
    public partial class MainWindow : Window
    {
        private NotifyIcon? _notifyIcon;
        private AppServiceConnection? _connection;
        private bool _isExiting;
        private double d1, d2;

        public MainWindow()
        {
            InitializeComponent();
            CreateTrayIcon();
            InitializeAppServiceConnection();
        }

        private void CreateTrayIcon()
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "myicon.ico");
            _notifyIcon = new NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconPath),
                Visible = true,
                Text = "DesktopBridge",
            };
            _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (_, _) => RestoreWindow());
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Closing the window only hides it to the tray; real exit goes through ExitApplication().
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == User32API.WM_SHOWME)
            {
                RestoreWindow();
            }
            else if (msg == User32API.WM_SHOWNOTI)
            {
                new NotificationWindow().Show();
            }

            return IntPtr.Zero;
        }

        private async void InitializeAppServiceConnection()
        {
            LogManager.Instance.WriteLogs("Initialize app-service connection.");

            _connection = new AppServiceConnection
            {
                AppServiceName = "SampleInteropService",
                PackageFamilyName = Package.Current.Id.FamilyName,
            };
            _connection.RequestReceived += Connection_RequestReceived;
            _connection.ServiceClosed += Connection_ServiceClosed;

            AppServiceConnectionStatus status = await _connection.OpenAsync();
            if (status != AppServiceConnectionStatus.Success)
            {
                // Don't pop a modal dialog: this runs tray-only at logon, possibly before the
                // UWP background host is ready. Log and retry shortly.
                LogManager.Instance.WriteLogs($"App-service connection failed: {status}; retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5));
                InitializeAppServiceConnection();
            }
        }

        private void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            LogManager.Instance.WriteLogs($"App-service disconnected from WPF: {args.Status}.");

            // The connection to the UWP app was lost; try to re-establish it instead of exiting.
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(InitializeAppServiceConnection));
        }

        /// <summary>
        /// Handles a request from the UWP app to read a registry key.
        /// </summary>
        private async void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var response = new ValueSet();
                string? key = args.Request.Message["KEY"] as string;
                int index = key?.IndexOf('\\') ?? -1;

                if (key is null || index <= 0)
                {
                    response.Add("ERROR", "INVALID REQUEST");
                }
                else
                {
                    string hiveName = key.Substring(0, index);
                    string keyName = key.Substring(index + 1);
                    RegistryHive hive = hiveName switch
                    {
                        "HKLM" => RegistryHive.LocalMachine,
                        "HKCU" => RegistryHive.CurrentUser,
                        "HKCR" => RegistryHive.ClassesRoot,
                        "HKU" => RegistryHive.Users,
                        "HKCC" => RegistryHive.CurrentConfig,
                        _ => RegistryHive.ClassesRoot,
                    };

                    using RegistryKey? regKey = RegistryKey.OpenRemoteBaseKey(hive, "").OpenSubKey(keyName);
                    if (regKey != null)
                    {
                        foreach (string valueName in regKey.GetValueNames())
                        {
                            response.Add(valueName, regKey.GetValue(valueName)?.ToString());
                        }
                    }
                    else
                    {
                        response.Add("ERROR", "KEY NOT FOUND");
                    }
                }

                await args.Request.SendResponseAsync(response);
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>
        /// Sends a request to the UWP app (demo of the bidirectional connection).
        /// </summary>
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (_connection is null)
            {
                return;
            }

            try
            {
                var request = new ValueSet { { "D1", d1 }, { "D2", d2 } };
                AppServiceResponse response = await _connection.SendMessageAsync(request);

                if (response.Status == AppServiceResponseStatus.Success &&
                    response.Message.TryGetValue("RESULT", out object? value) &&
                    value is double result)
                {
                    tbResult.Text = result.ToString();
                }
                else
                {
                    LogManager.Instance.WriteLogs($"Calc request returned no result (status={response.Status}).");
                    tbResult.Text = "?";
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.WriteLogs($"Calc request failed: {ex.Message}");
                tbResult.Text = "?";
            }
        }

        /// <summary>
        /// Enables the "equals" button only when both inputs are valid numbers.
        /// </summary>
        private void tb_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnCalc.IsEnabled = double.TryParse(tb1.Text, out d1) & double.TryParse(tb2.Text, out d2);
        }
    }
}
