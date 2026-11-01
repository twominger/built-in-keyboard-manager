using System.Threading;

namespace BuiltInKeyboardManager;

internal static class Program
{
    private const string MutexName = "Local\\BuiltInKeyboardManager.SingleInstance";
    private const string ShowEventName = "Local\\BuiltInKeyboardManager.Show";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var firstInstance);
        if (!firstInstance)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
                showEvent.Set();
            }
            catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        using var form = new MainForm(args.Contains("--startup", StringComparer.OrdinalIgnoreCase), ShowEventName);
        Application.Run(form);
    }
}
