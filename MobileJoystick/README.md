# MobileJoystick（启动器内置版本）

MobileJoystick 为 Terraria / tModLoader 提供手机版操作：双摇杆、触屏背包、菜单和可编辑的按键布局。当前版本 **0.1.4**，原作者 **yu**。本目录是 tML Pocket Launcher 内嵌并适配的客户端模组，来源与迁移记录见 [UPSTREAM.md](UPSTREAM.md)。

## 操作能力

- 双摇杆分别控制移动与瞄准，支持跳跃、攻击、抓钩、坐骑、药水和智能光标。
- 简单、高级、自定义控制布局；四套预设、位置与大小调整、按钮和轮盘绑定、撤销与重做，以及其他模组快捷键。
- 触屏背包、装备、配装、物品拖动与拆分、收藏、箱子存取、配方搜索和制作。
- 标题、角色与世界选择、暂停、NPC 对话、地图、聊天及系统输入法接口。
- 控件大小、不透明度、文字缩放、摇杆死区、瞄准距离与侧边距设置；中文和英文文本。

    0.1.4 同步原生攻击触发器，改善 InnoVault / 灾厄武器重做等读取原生输入的远程武器持续开火。仅瞄准模式、独立使用键和松手停止分别处理。

## 在启动器中使用

启动器自动打包并维护此模组，无需另外导入。进入游戏后，在暂停菜单的“控制”入口选择布局并打开编辑器。为其他模组添加触屏快捷键时，可在自定义按钮或轮盘中选择绑定。

本项目通过 Android SDL 多点触摸输入接入游戏；同时操作多个控件需要运行平台支持多点触摸。`side = Client`，服务器无需安装此界面模组。

## 构建与验证

此副本使用仓库的 **.NET 10** 构建配置及预编译 Terraria **1.4.4.9** / tML **2026.09.3.1** 参考程序集。其他游戏版本需要单独验证。

在仓库根目录执行：

```powershell
.\scripts\build\prepare-game-references.ps1 -TmlRoot '<预编译 tModLoader 安装目录>'
.\scripts\build\prepare-builtin-mods.ps1
.\scripts\checks\check-patch-runtime.ps1
```

正常 Android 构建会自动调用模组打包脚本，将生成的 `MobileJoystick.tmod` 放入 APK 的 `launcher/BuiltinMods/`。参考程序集和生成包位于忽略的构建目录，不随源码提交。完整流程见 [开发指南](../../../docs/development.md)。

运行时验证包含快速点按、双触点移动与瞄准、持续攻击、松手、菜单点击、缩放和其他输入回归；这些自动化验证不能代替真机游玩与第三方模组兼容性测试。

## 源码组织

| 路径                                             | 职责                           |
| ------------------------------------------------ | ------------------------------ |
| `MobileJoystick*.cs`、`MobileTouchInput.cs`      | 摇杆、输入路由、玩家控制和布局 |
| `MobileCustomControls.cs`、`MobileMenuSystem.cs` | 自定义控件、预设、编辑器与菜单 |
| `MobileInventory*.cs`                            | 背包、装备、制作和物品手势     |
| `Mobile*System.cs`                               | 标题、角色、世界、地图和聊天   |
| `Assets/`、`Localization/`                       | 界面素材、来源记录与本地化     |

## 兼容范围与反馈

HUD / 背包 / 制作面板的完整自由布局仍在开发。直接读取硬件鼠标状态、拥有独立右键事件或特殊输入逻辑的模组可能需要适配；多人入口的存在不代表所有联机流程已获验证。

启动器集成问题请提交到 [tML Pocket Launcher Issues](https://github.com/FireworkSky/tML-Pocket-Launcher/issues)；上游项目见 [MobileJoystick](https://github.com/FireworkSky/MobileJoystick)。请附模组和 tML 版本、设备、布局模式、其他启用模组、复现步骤及脱敏日志。

## 来源与权利

本模组为非官方社区项目，与 Re-Logic、505 Games 和 tModLoader 官方团队无官方隶属关系。Terraria 及手机版相关美术资源仍归原权利人所有，来源记录保留在 `Assets/` 的清单和说明中。此副本未附独立上游 LICENSE，本项目不能替原作者或游戏权利人授予新许可。完整说明见 [第三方声明](../../../THIRD_PARTY_NOTICES.md)。
