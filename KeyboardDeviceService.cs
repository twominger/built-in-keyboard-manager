using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BuiltInKeyboardManager;

internal static class KeyboardDeviceService
{
    private static readonly Guid KeyboardClassGuid = new("4D36E96B-E325-11CE-BFC1-08002BE10318");
    private const uint DigcfPresent = 0x00000002;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint DnStarted = 0x00000008;
    private const uint CmProbDisabled = 22;

    public static IReadOnlyList<KeyboardDevice> Enumerate()
    {
        var devices = new List<KeyboardDevice>();
        var classGuid = KeyboardClassGuid;
        var set = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    continue;
                }

                var id = GetInstanceId(set, ref data);
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = GetProperty(set, ref data, SpdrpFriendlyName)
                           ?? GetProperty(set, ref data, SpdrpDeviceDesc)
                           ?? "键盘设备";
                var enabled = IsDeviceEnabled(id);
                var recommended = IsBuiltInCandidate(id, name);
                devices.Add(new KeyboardDevice(name, id, enabled, recommended));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return devices
            .OrderByDescending(d => d.IsRecommended)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsBuiltInCandidate(string id, string name)
    {
        var text = $"{id} {name}".ToUpperInvariant();
        return text.StartsWith("ACPI\\")
               || text.Contains("PS/2")
               || text.Contains("PS2")
               || text.Contains("STEELSERIES");
    }

    public static bool IsDeviceEnabled(string instanceId)
    {
        if (CmLocateDevNode(out var node, instanceId, 0) != 0) return false;
        if (CmGetDevNodeStatus(out var status, out var problem, node, 0) != 0) return false;
        return (status & DnStarted) != 0 && problem != CmProbDisabled;
    }

    public static async Task<(bool Success, bool RestartRequired, string Message)> SetEnabledAsync(string instanceId, bool enabled)
    {
        var action = enabled ? "/enable-device" : "/disable-device";
        var result = await RunPnpUtilAsync(action, instanceId, force: false);
        if (!enabled && !IsSuccessfulReturnCode(result.ExitCode) && Environment.OSVersion.Version.Build >= 22000)
            result = await RunPnpUtilAsync(action, instanceId, force: true);

        if (result.ExitCode is 3010 or 1641)
        {
            return (true, true, enabled
                ? "启用请求已成功提交，重启 Windows 后恢复键盘。"
                : "禁用请求已成功提交，重启 Windows 后键盘将被禁用。请先保持外接键盘可用。");
        }

        if (result.ExitCode == 0)
        {
            await Task.Delay(500);
            var isEnabledNow = IsDeviceEnabled(instanceId);
            if (isEnabledNow == enabled)
                return (true, false, enabled ? "内置键盘已启用。" : "内置键盘已禁用。电源键通常不受影响。");
            return (true, true, enabled
                ? "启用请求已提交，需要重启 Windows 后生效。"
                : "禁用请求已提交，需要重启 Windows 后生效。重启前请保持外接键盘可用。");
        }

        return (false, false, string.IsNullOrWhiteSpace(result.Output)
            ? $"操作失败，PnPUtil 返回代码 {result.ExitCode}。"
            : result.Output);
    }

    private static bool IsSuccessfulReturnCode(int exitCode) => exitCode is 0 or 3010 or 1641;

    private static async Task<(int ExitCode, string Output)> RunPnpUtilAsync(string action, string instanceId, bool force)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "pnputil.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(instanceId);
        if (force) start.ArgumentList.Add("/force");

        try
        {
            using var process = Process.Start(start);
            if (process is null) return (-1, "无法启动 Windows 设备管理工具。");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var detail = string.Join(" ", stdout, stderr).Trim();
            return (process.ExitCode, detail);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static string? GetInstanceId(IntPtr set, ref SpDevinfoData data)
    {
        var buffer = new StringBuilder(1024);
        return SetupDiGetDeviceInstanceId(set, ref data, buffer, buffer.Capacity, out _)
            ? buffer.ToString()
            : null;
    }

    private static string? GetProperty(IntPtr set, ref SpDevinfoData data, uint property)
    {
        var buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, buffer, (uint)buffer.Length, out _))
            return null;
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint deviceNode, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint deviceNode, uint flags);

    private static uint CmLocateDevNode(out uint deviceNode, string deviceId, uint flags) =>
        CM_Locate_DevNodeW(out deviceNode, deviceId, flags);

    private static uint CmGetDevNodeStatus(out uint status, out uint problemNumber, uint deviceNode, uint flags) =>
        CM_Get_DevNode_Status(out status, out problemNumber, deviceNode, flags);
}
