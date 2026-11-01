using System.Diagnostics;

namespace BuiltInKeyboardManager;

/// <summary>只用于清理 1.1 及更早版本创建的自启动任务。</summary>
internal static class StartupTaskService
{
    private const string TaskName = "BuiltInKeyboardManager";

    public static bool Exists() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    public static (bool Success, string Message) RemoveLegacyTask()
    {
        var result = Run("/Delete", "/TN", TaskName, "/F");
        return result.ExitCode == 0 || !Exists()
            ? (true, "旧版开机自启动任务已删除。")
            : (false, string.IsNullOrWhiteSpace(result.Output) ? "无法删除旧版自启动任务。" : result.Output);
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, string.Join(" ", output, error).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
