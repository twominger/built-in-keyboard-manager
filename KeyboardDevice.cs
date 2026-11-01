namespace BuiltInKeyboardManager;

internal sealed record KeyboardDevice(string Name, string InstanceId, bool IsEnabled, bool IsRecommended)
{
    public override string ToString()
    {
        var kind = IsRecommended ? "内置候选" : "可能是外接设备";
        var state = IsEnabled ? "已启用" : "已禁用";
        return $"{Name}  [{kind}，{state}]";
    }
}
