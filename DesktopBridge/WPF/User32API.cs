using System.Runtime.InteropServices;

namespace WPF
{
    /// <summary>
    /// Minimal user32 interop used for cross-instance window messaging.
    /// </summary>
    internal static class User32API
    {
        public static readonly IntPtr HWND_BROADCAST = new(0xffff);

        // Registered (application-defined) window messages used to signal the running instance.
        public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME");
        public static readonly int WM_SHOWNOTI = RegisterWindowMessage("WM_SHOWNOTI");

        [DllImport("user32.dll")]
        public static extern bool SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll")]
        public static extern int RegisterWindowMessage(string message);
    }
}
