# STU-OA-Monitor

汕头大学 OA 桌面通知工具

## 简介

基于 .NET 10 + WPF 的 Windows 托盘应用，定时拉取汕头大学 OA 通知列表，发现新条目时弹出系统通知，并按日期归档为本地 HTML 页面。

## 功能特性

- 后台定时轮询（默认 10 分钟），支持自定义间隔
- 系统托盘常驻，关闭窗口自动隐藏到托盘
- 新通知通过 Windows 10/11 系统 Toast 提醒，点击则打开 WPF 窗口
- 按发布日期归档为 HTML（`oa_daily/html/YYYY-MM-DD.html`），可离线查阅
- 只关注指定日期之后的通知
- 已读去重：重启后不会重复提醒

## 系统要求

- 操作系统：Windows 10/11
- 处理器：x64、x86、ARM64 架构

## 快速开始

1. 从[GitHub Releases](https://github.com/302Leaves/STU-OA-Monitor/releases)下载最新压缩包并解压到**有写权限的目录**（不要放在 `C:\Program Files`，程序需要在同级目录写配置和日志）。
2. 将电脑连接到**汕头大学校园网**或使用**汕头大学 WebVPN**。
3. 双击 `StuOaMonitor.exe` 启动，首次运行会在同目录生成 `config/`、`oa_daily/` 和 `oa_monitor.log`。
4. 程序常驻托盘；**退出请右键托盘图标 → 退出**（直接关窗口只是将程序隐藏到托盘）。

## 常见问题

- 收不到通知
  - 检查 Windows「设置 → 系统 → 通知」是否允许本程序，以及是否开启专注助手。
- 启动提示「Windows 已保护你的电脑」
  - 程序未签名，点「更多信息 → 仍要运行」。
- 获取不到OA通知数据
  - 将电脑连接到**汕头大学校园网**或使用**汕头大学 WebVPN**，确保能访问 oa.stu.edu.cn。

## 贡献与反馈

- 欢迎提交 Issue 报告 Bug 或功能请求：[GitHub Issues](https://github.com/302Leaves/STU-OA-Monitor/issues)。
- 提交 Pull Request 前请确保代码能够被成功构建，并遵循项目编码风格。
  
## 隐私与免责声明

- 本工具仅访问汕头大学 OA 的公开通知列表页，不上传、不收集任何个人数据。
- 本项目为**非官方项目**，与汕头大学无关。
- 本工具仅供个人学习、查看通知使用，请勿用于高频批量请求 OA 站点。

## 许可

- 本项目采用 [MIT](LICENSE) 许可，仅覆盖本仓库中由作者编写的源代码。

## 致谢

- 托盘与通知库： [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)（MIT）。
