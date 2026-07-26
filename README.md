# CodexUsageBar

<p align="center">
  <strong>中文</strong> · <a href="./README.en.md">English</a>
</p>

<p align="center">
  安静地待在 Windows 11 任务栏里，显示 Codex 剩余额度。
</p>

[![CI](https://github.com/puwenfu/CodexUsageBar/actions/workflows/ci.yml/badge.svg)](https://github.com/puwenfu/CodexUsageBar/actions/workflows/ci.yml)
[![最新版本](https://img.shields.io/github/v/release/puwenfu/CodexUsageBar?display_name=tag&sort=semver)](https://github.com/puwenfu/CodexUsageBar/releases/latest)
[![下载量](https://img.shields.io/github/downloads/puwenfu/CodexUsageBar/total)](https://github.com/puwenfu/CodexUsageBar/releases)
[![许可证：MIT](https://img.shields.io/badge/license-MIT-7C3AED.svg)](LICENSE)
[![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11)](https://www.microsoft.com/windows/windows-11)

**[下载最新版本](https://github.com/puwenfu/CodexUsageBar/releases/latest)**

> 非官方社区项目，与 OpenAI 没有隶属关系，也未获得 OpenAI 背书。

## 主题预览

以下图片由真实 WPF 控件在 150% DPI 下使用示例数据确定性渲染，不是
Windows Shell 实机截图。时间采用当前的剩余时长格式，例如 `4d 12h 56m`。

<table>
  <tr>
    <td align="center">
      <img src="assets/codex-usage-bar-taskbar.png" width="252" alt="沧海星澜主题，使用剩余时长格式">
    </td>
    <td align="center">
      <img src="assets/codex-usage-bar-taskbar-purple.png" width="252" alt="暮紫流烟主题，使用剩余时长格式">
    </td>
  </tr>
  <tr>
    <td align="center">
      <img src="assets/codex-usage-bar-taskbar-rose.png" width="252" alt="绯樱流霞主题，使用剩余时长格式">
    </td>
    <td align="center">
      <img src="assets/codex-usage-bar-taskbar-mint.png" width="252" alt="薄荷清露主题，使用剩余时长格式">
    </td>
  </tr>
</table>

## 功能

- 显示 Codex 五小时与每周额度的剩余比例和恢复倒计时。
- 两个紧凑仪表完整嵌入 Windows 11 主任务栏。
- 支持手动刷新，不抢占焦点，也不遮挡附近的任务栏控件。
- Codex 暂时不可用时，保留最后一次安全数据。
- 以独立 EXE 运行，不复制或保存 Codex 登录凭据。
- 提供 5 种仪表色彩主题和多种刷新动画。

## 运行要求

- Windows 11，主任务栏位于屏幕底部。
- 主任务栏左侧有足够的显示空间。
- 本机已安装并登录 Codex App 或 Codex CLI。

## 下载与运行

从 [GitHub Releases](https://github.com/puwenfu/CodexUsageBar/releases/latest)
下载当前 Windows ZIP，解压后运行 `CodexUsageBar.exe`。

EXE 未进行代码签名。首次运行时 Windows 可能显示“未知发布者”警告或
SmartScreen 提示。继续前请核对发布来源和校验值。

## 校验下载

每个版本都提供 `SHA256SUMS.txt`。在 PowerShell 中计算 ZIP 的哈希：

```powershell
Get-FileHash .\CodexUsageBar_*_win-x64.zip -Algorithm SHA256
```

将显示的 SHA-256 与 `SHA256SUMS.txt` 中对应 ZIP 的记录进行比较。

## 使用

左键单击小组件可刷新。右键菜单可刷新、切换仪表主题或刷新动画、控制可选
的开机启动、打开调试面板或退出。开机启动默认关闭。

## 隐私

小组件通过本地 Codex app-server 协议读取额度数据，并使用当前
CodexUsageBar 版本标识自身。它不会复制或保存凭据、账户标识、原始额度响应
或任务内容。详见[隐私说明](docs/privacy.md)。

## 已知限制

目前支持位于屏幕底部的 Windows 11 主任务栏。当所需任务栏被隐藏、不可用或
布局不受支持时，应用会退出，而不会绘制到其他位置。Codex 协议变化或临时连接
失败可能延迟刷新；如有安全的历史数据，界面会继续显示最后一次成功结果。

## 从源码构建

安装 .NET 8 SDK，然后运行：

```powershell
dotnet restore CodexUsageBar.sln
dotnet build CodexUsageBar.sln --configuration Release --no-restore
```

本地交互式启动请在仓库根目录运行 `run.bat`。

## 测试

```powershell
dotnet test CodexUsageBar.sln --configuration Release --no-build --verbosity minimal
powershell -NoProfile -Command "Invoke-Pester -Path '.\tests\PublishSupport.Tests.ps1'"
powershell -NoProfile -Command "Invoke-Pester -Path '.\tests\PublishScript.Tests.ps1'"
```

## 发布流程

只有在本地打包已被明确批准后，才能运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

只验证发布输入、不执行打包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -WhatIfValidation
```

版本号只维护在 `Directory.Build.props`。如果版本号与 `CHANGELOG.md` 中最新
已发布条目不一致，验证会在构建前停止。

发布 ZIP 固定包含独立 EXE、`README.md`、`CHANGELOG.md`、`LICENSE` 和
`THIRD-PARTY-NOTICES.txt`，哈希记录在 `SHA256SUMS.txt`。不可变发布规则和
最终 EXE 验收步骤详见[发布流程](docs/release.md)。

## 参与贡献

参见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 安全

参见 [SECURITY.md](SECURITY.md)。公开报告中不要放入凭据、账户信息、原始
Codex 数据、日志或未经脱敏的截图。

## 许可证

CodexUsageBar 使用 [MIT License](LICENSE)。第三方运行时声明位于
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
