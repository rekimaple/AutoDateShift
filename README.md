# AutoDateShift（系统日期自动切换）

<img src="app.png" width="96" align="right">

按预设规则自动调整 Windows 系统日期的小工具，带 GUI 管理界面与后台服务。
A Windows utility that automatically shifts the system date according to preset rules, with a GUI manager and a background service.

---

## 功能特性 | Features

- **两种规则模式**
  - 按星期：将指定日期调整为最近的某个星期几（±3 天内取最近）——适用于仅按星期几调度任务的软件（如打铃、排课、值班系统）
  - 按日期：将指定日期调整为任意目标日期，偏移天数不限
- **自动还原**：无规则的日期自动恢复真实日期，时分秒始终不变
- **双进程架构**：GUI 管理界面 + 无窗口后台服务（每 60 秒自检），规则仅服务运行时生效
- **开机自启**：基于任务计划（SYSTEM 账户、最高权限），无人登录也能运行；断电重启策略可选（记忆上次状态 / 自动开启 / 保持关闭）
- **时间校准**：支持 NTP 网络校时（预置 5 个常用服务器）与手动校时，内网离线可用
- **运维留痕**：一行一条的运行日志（`[时间] [来源] 内容`），错误含版本号与原因，按月分文件，可筛选导出
- **中英文界面**：即时切换，布局随文本长度自适应
- **单实例运行**：重复启动激活已有窗口；关闭窗口默认最小化到托盘

## 运行环境 | Requirements

- Windows 7 / Server 2008 R2 及以上（含 Server 2012/2016/2019/2022、Windows 10/11）
- 无需安装运行时：依赖系统自带 .NET Framework 4.0
- 修改系统时间需管理员权限；后台服务以 SYSTEM 账户运行

## 编译 | Build

无需安装 Visual Studio，直接双击 `build.bat`（调用系统自带的 .NET 4.0 编译器），生成单文件 `AutoDateShift.exe`。

编译后建议执行自检（19 项断言应全部通过）：

```
AutoDateShift.exe --selftest --datadir %TEMP%\ads_test
```

## 使用 | Usage

1. 右键 `AutoDateShift.exe` → 以管理员身份运行
2. 「运行状态」页启动后台服务（建议同时勾选开机自启）
3. 「调整规则」页录入规则并保存

详见 [部署说明.md](部署说明.md)。See `部署说明.md` (Chinese) for deployment details.

## 卸载 | Uninstall

1. 停止后台服务并取消开机自启
2. 退出程序，删除 exe 与数据目录（默认 `C:\ProgramData\AutoDateShift`）
3. 若日期仍处于调整状态，手动修正系统日期

若 exe 已丢失：以管理员身份运行 `应急卸载.bat` 清理计划任务与进程。

## 安全性 | Security

- 不主动联网、不上传数据（仅 NTP 校时时访问一次所选时间服务器）
- 规则、状态与日志均为本机明文 JSON / 文本，可直接审计
- 界面图标由代码矢量绘制，无外部资源文件

## 免责声明 | Disclaimer

本软件部分代码由 AI 辅助生成。修改系统时间可能影响依赖系统日期的其他软件（授权许可、证书、计划任务、文件时间戳等）。请在充分评估后使用，由此产生的后果由使用者自行承担。

Parts of this software were generated with AI assistance. Changing the system time may affect other software that depends on the system date. Evaluate before use; all consequences are borne by the user.

## License

Copyright (c) 2026 rekimaple. Released under the [MIT License](LICENSE).
