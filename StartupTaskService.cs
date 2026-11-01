using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace BuiltInKeyboardManager;

internal static class StartupTaskService
{
    public const string TaskName = "BuiltInKeyboardManager";

    public static bool Exists()
    {
        var result = RunSchtasks("/Query", "/TN", TaskName);
        return result.ExitCode == 0;
    }

    public static (bool Success, string Message) SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            var deleted = RunSchtasks("/Delete", "/TN", TaskName, "/F");
            return deleted.ExitCode == 0 || !Exists()
                ? (true, "已关闭开机自启动。")
                : (false, deleted.Output);
        }

        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid)) return (false, "无法确定当前 Windows 用户。");

        var xml = BuildTaskXml(exe, sid);
        var temp = Path.Combine(Path.GetTempPath(), $"bikm-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(temp, xml, new UnicodeEncoding(false, true));
            var result = RunSchtasks("/Create", "/TN", TaskName, "/XML", temp, "/F");
            return result.ExitCode == 0
                ? (true, "已设置登录 Windows 后自动启动。")
                : (false, result.Output);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static string BuildTaskXml(string exePath, string userSid)
    {
        static string E(string text) => SecurityElement.Escape(text) ?? text;
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>内置键盘管理器托盘程序</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{E(userSid)}</UserId></LogonTrigger></Triggers>
  <Principals><Principal id="Author"><UserId>{E(userSid)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>
  <Actions Context="Author"><Exec><Command>{E(exePath)}</Command><Arguments>--startup</Arguments></Exec></Actions>
</Task>
""";
    }

    private static (int ExitCode, string Output) RunSchtasks(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!;
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
