using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DesktopBridge
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private bool _connectionAttached;

        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// When app is loaded, kick off the desktop process
        /// and listen to app service connection events
        /// </summary>
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                App.AppServiceConnected += MainPage_AppServiceConnected;
                App.AppServiceDisconnected += MainPage_AppServiceDisconnected;
                 await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();

            }

            // The tray app usually connects before this page exists, so the AppServiceConnected
            // event has already fired and was missed. Wire up the existing connection now so
            // Scenario 2 (incoming requests) is shown.
            if (App.Connection != null)
            {
                AttachConnection(App.Connection);
            }
        }

        private void MainPage_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            LogManager.Instance.WriteLogs("App Service Connected");
            if (e?.AppServiceConnection != null)
            {
                AttachConnection(e.AppServiceConnection);
            }
        }

        /// <summary>
        /// Subscribes the UI to the connection (idempotent) and enables the controls.
        /// </summary>
        private async void AttachConnection(AppServiceConnection connection)
        {
            if (_connectionAttached)
            {
                return;
            }
            _connectionAttached = true;

            // App.Connection_RequestReceived is the single responder; this handler only mirrors
            // incoming requests into Scenario 2's text box.
            connection.RequestReceived += AppServiceConnection_RequestReceived;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                btnRegKey.IsEnabled = true;
            });
        }

        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void MainPage_AppServiceDisconnected(object sender, EventArgs e)
        {

            LogManager.Instance.WriteLogs($"ON UWP Disconnected ");
            _connectionAttached = false;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // disable UI to access the connection
                btnRegKey.IsEnabled = false;
            });
        }

        /// <summary>
        /// Send request to query the registry
        /// </summary>
        private async void btnClick_ReadKey(object sender, RoutedEventArgs e)
        {
            if (App.Connection == null)
            {
                LogManager.Instance.WriteLogs("Read key requested but the app-service connection is not available.");
                return;
            }

            ValueSet request = new ValueSet();
            request.Add("KEY", tbKey.Text);
            AppServiceResponse response = await App.Connection.SendMessageAsync(request);

            // display the response key/value pairs
            tbResult.Text = "";
            foreach (string key in response.Message.Keys)
            {
                tbResult.Text += key + " = " + response.Message[key] + "\r\n";
            }

            // Ask the (already running) WPF tray app to show a custom notification over the
            // existing IPC connection - no need to launch another process (which flashed the
            // "app starting" cursor).
            ValueSet notify = new ValueSet();
            notify.Add("CMD", "SHOWNOTI");
            await App.Connection.SendMessageAsync(notify);
        }

        /// <summary>
        /// Mirrors a calculation request from the desktop process in the UI.
        /// The response itself is sent by the single responder in App.Connection_RequestReceived;
        /// sending a second response here would throw and tear down the connection.
        /// </summary>
        private async void AppServiceConnection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            LogManager.Instance.WriteLogs("Request received (UI mirror).");

            object v1, v2;
            if (!args.Request.Message.TryGetValue("D1", out v1) ||
                !args.Request.Message.TryGetValue("D2", out v2) ||
                !(v1 is double) || !(v2 is double))
            {
                return;
            }

            double d1 = (double)v1;
            double d2 = (double)v2;

            // log the request in the UI for demo purposes
            await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                tbRequests.Text += string.Format("Request: {0} + {1} --> Response = {2}\r\n", d1, d2, d1 + d2);
            });
        }
    }
}
