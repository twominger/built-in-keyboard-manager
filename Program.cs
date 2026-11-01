using System.Threading;

namespace BuiltInKeyboardManager;

internal static class Program
{
    private const string MutexName = "Local\\BuiltInKeyboardManager.SingleInstance";
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("内置键盘管理器已经在运行。", "内置键盘管理器",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        Application.Run(form);
    }
}
