<p align="center">
  <img src="assets/app-icon.png" width="160" alt="内置键盘管理器图标">
</p>

<h1 align="center">内置键盘管理器</h1>

<p align="center">一个轻量、可逆的 Windows 笔记本内置键盘管理工具。</p>

## 功能

- 枚举 Windows 当前检测到的键盘设备。
- 优先标记 `ACPI`、`PS/2` 和 `SteelSeries` 设备为内置键盘候选。
- 仅按用户选择的设备实例 ID 启用或禁用设备，不批量操作所有键盘。
- 驻留系统托盘，可从托盘启用、禁用或打开主界面。
- 可创建当前用户的最高权限登录计划任务。
- 可在程序自动启动后尝试禁用保存的内置键盘。
- 正确识别 PnPUtil 的 `3010` 和 `1641` 返回码，并提示需要重启。

## 工作原理

程序使用 Windows SetupAPI 枚举键盘设备类：

```text
{4D36E96B-E325-11CE-BFC1-08002BE10318}
```

用户选择设备后，程序保存该设备的唯一实例 ID，并通过 Windows 自带的 `PnPUtil.exe` 执行：

```powershell
pnputil /disable-device "设备实例 ID"
pnputil /enable-device  "设备实例 ID"
```

Windows 11 上，如果系统将目标设备视为关键输入设备，普通禁用失败后程序会使用 `/force` 再试一次。程序不会删除驱动包，也不会修改 `i8042prt.sys`、键盘类 UpperFilters 或未知注册表项。

自启动通过 Windows 任务计划程序实现：登录当前账户时，以交互方式和最高可用权限运行程序。设置保存在：

```text
%LOCALAPPDATA%\BuiltInKeyboardManager\settings.json
```

## 重要限制

部分笔记本内置键盘是启动阶段加载的 PS/2 设备。Windows 不能在系统运行期间安全停止这类设备，PnPUtil 会返回 `3010`：请求成功，但必须重启后才能生效。

例如 MSI GS66 的 `SteelSeries PS/2 Keyboard` 通常显示为：

```text
ACPI\MSI0007\...
```

这种情况下：

- 禁用请求成功后，保持外接键盘连接并重启。
- 电源键通常由 EC/ACPI 独立管理，但首次使用仍应实际测试。
- 恢复操作是否立即生效由设备驱动决定；程序会显示 Windows 的实际结果。

本项目不使用用户态键盘钩子，因为低级键盘钩子无法可靠区分内置键盘与外接键盘；也不附带未经可信签名的内核过滤驱动。

## 使用方法

1. 连接一个可用的 USB 或蓝牙外接键盘。
2. 运行 `内置键盘管理器.exe`，接受管理员权限提示。
3. 在设备列表中选择标记为“内置候选”的键盘，例如 `SteelSeries PS/2 Keyboard`。
4. 先测试“禁用内置键盘”，按提示重启并确认电源键、鼠标和外接键盘正常。
5. 从托盘菜单测试“启用内置键盘”。
6. 确认目标设备无误后，再根据需要启用“登录 Windows 后自动启动”和“启动后自动禁用”。

关闭主窗口不会退出程序，只会隐藏到系统托盘。要完全退出，请右键托盘图标并选择“退出（不改变键盘状态）”。

## 构建

要求：Windows 10/11、.NET 8 SDK。

```powershell
./tools/Build-Icon.ps1
dotnet build ./BuiltInKeyboardManager.csproj -c Release --warnaserror
```

发布为依赖 .NET 8 Desktop Runtime 的单文件程序：

```powershell
dotnet publish ./BuiltInKeyboardManager.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o ./publish
```

## 图标

图标源文件位于 `assets/app-icon-source.png`。运行 `tools/Build-Icon.ps1` 会生成：

- `assets/app-icon.ico`：包含 16、20、24、32、40、48、64、128 和 256 像素图层。
- `assets/app-icon.png`：用于项目说明的 512 像素预览。

## 隐私

程序完全在本机运行，不收集遥测数据，不上传键盘或设备信息，也不依赖在线服务。

## 许可证

[MIT License](LICENSE)
