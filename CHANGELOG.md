# CHANGELOG

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-11-26

### Added
- GUI 窗口显示通知表单，支持双击通知的标题跳转到学校 OA 中该通知的页面
- GUI 设置窗口，允许设置爬取消息的最早日期
- 系统托盘常驻，支持左键显示窗口、右键菜单（显示 / 立即检查 / 退出）
- 发现新通知时弹出桌面通知，多条新消息自动合并为一条提示，支持单击系统通知打开 GUI 窗口
- 日志模块，支持 控制台 + 文件 打印，日志等级可通过命令行或配置指定
- 配置文件，持久化记录抓取最早日期与日志等级，缺失或损坏时自动回退默认值
- 本地汇总爬取到的消息，按日期生成静态 HTML 页面、元数据 JSON、索引
- 基于本地已爬取消息的 URL 集合与日期判断新消息
- 启动时基于本地已有 meta JSON 与 HTML 备份中检查本地旧消息是否缺失，并通过重新爬取进行修复
- 加入命令行参数，用于临时调试
- WPF 与 Core 解耦，Core 可以独立复用
- 支持 Windows 10 / 11，支持 x64 / x86 / ARM64，使用自包含发布，目标机器无需额外安装 .NET

[Unreleased]: https://github.com/302Leaves/STU-OA-Monitor/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/302Leaves/STU-OA-Monitor/releases/tag/v1.0.0