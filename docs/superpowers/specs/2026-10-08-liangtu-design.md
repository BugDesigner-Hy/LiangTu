# 晾图 / LiangTu 设计规格

**日期:** 2026-10-08  
**状态:** 已批准并进入实现

## 概述

Windows 原生托盘应用：接管企业蓝盾（Tipray）`Ctrl+Alt+P` 截图的另存为流程，将截图挂到屏幕顶部「晾衣绳」，完整替代现有 PowerShell「蓝盾截图助手」。交互参考 [tendedero](https://github.com/alejandrobujan/tendedero)，**不使用**其名称与图标。

## 命名

| 用途 | 名称 |
|------|------|
| 显示名 | 晾图 |
| 程序集 / 解决方案 | LiangTu |
| 数据目录 | `%LOCALAPPDATA%\LiangTu\` |

## 决策

- **技术栈:** C# WPF，.NET 8
- **范围:** 完整替代蓝盾助手 + Tendedero 级晾衣绳交互
- **落盘:** 混合收件箱，默认 Inbox，托盘可改路径
- **显隐:** 贴顶感应 + 托盘 + `Ctrl+Alt+T`
- **截图:** 不自带；仅蓝盾 `Ctrl+Alt+P`

## 架构模块

- `SaveAsAutomator` — 蓝盾另存为自动化
- `ScreenshotWatcher` — 监视 Inbox 新图
- `Line` / `Pegged` — 挂载模型
- `LineOverlayWindow` — 透明顶栏
- `TopEdgeSensor` / `HotKeyService` / `TrayService`
- `Settings` / `AppLog`

## 非目标

- 替代蓝盾截图热键
- 多语言、商店分发、代码签名流水线
- 首版不做飞入动画（可二期）
