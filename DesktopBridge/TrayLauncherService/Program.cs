using System.ServiceProcess;
using TrayLauncherService;

// "--console" runs the launch logic once in the foreground (for manual testing from an
// elevated prompt). With no arguments the process is started by the Service Control Manager.
if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
{
    TrayService.RunInteractive();
    return;
}

ServiceBase.Run(new TrayService());
