# 晾图 / LiangTu

截图挂在晾衣绳上。Windows 托盘应用：接管蓝盾（Tipray）`Ctrl+Alt+P` 截图的另存为流程，把截图挂到屏幕顶部，完整替代旧版「蓝盾截图助手」。

![晾图：截图挂在屏幕顶部的晾衣绳上](docs/images/hero.png)

<p align="center"><sub>界面示意图</sub></p>

交互参考开源 macOS 应用 [tendedero](https://github.com/alejandrobujan/tendedero)（仅参考实现思路；**未使用**其名称与图标）。

## 功能

| 操作 | 行为 |
|------|------|
| `Ctrl+Alt+P` | 蓝盾截图（本应用不拦截） |
| 点蓝盾保存 | 自动确认另存为（BMP/加密原文）→ 写入收件箱 → 挂上晾衣绳；**不**再转明文 PNG |
| 鼠标贴屏幕顶边 / `Ctrl+Alt+T` / 托盘 | 展开或收起晾衣绳 |
| 拖动绳子 / 两端金色圆点 | 移到主屏或副屏，并按该屏宽度/DPI 重新排布 |
| 贴另一块屏幕顶边 | 绳子移到该屏 |
| 托盘 → 移到显示器 | 选择目标屏幕 |
| 常显 8 张 / 右侧 `+N` | 更多截图折叠；点击展开，再点一张可挂回前排 |
| 单击照片 | 复制到剪贴板（文件 + 位图，可粘贴进企微） |
| 沿绳水平拖 | 夹子保持夹着，松手换位 |
| 往下拉开夹子 / 拖出窗口 | 以文件形式拖到企微/文件夹/桌面 |
| 右上角 × / 右键「删除」 | 收件箱文件进回收站并离线 |
| 右键「重命名」 | 改文件名 |
| 右键菜单 | 复制 / 固定前排 / 重命名 / 打开 / 画图 / 文件夹 / 取下 / 删除 |
| 托盘 | 版本号、收件箱清理、保留策略、显隐快捷键、衣夹/绳子/照片样式、绳头挂饰、停止旧助手、日志 |
| 长按 | 用画图打开 |
| 双击 | 用默认看图应用打开 |

## 环境要求

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（开发/编译）
- 企业环境使用蓝盾截图

## 构建与运行

```powershell
cd d:\workspace\myProject\web\hangOutImg
dotnet build LiangTu.sln -c Release
dotnet run --project src\LiangTu\LiangTu.csproj -c Release
```

发布单文件：

```powershell
dotnet publish src\LiangTu\LiangTu.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

### 一键打包给同事

双击根目录 `打包给同事.bat`，会生成：

- `dist\LiangTu\` — 最近一次 publish 的可运行目录  
- `dist\LiangTu-win-x64-v{版本}.zip` — 带版本号的压缩包（自带 .NET）；历史版本 zip 会保留不覆盖

对方解压后双击 `LiangTu.exe`；首次建议在托盘打开「开机自启」。

## 重要：与旧助手互斥

启动时若检测到旧版「蓝盾截图助手」（`AutoSave-LdScreen`），会提示一键停止并关闭其开机自启。也可随时在托盘菜单选择「停止旧版蓝盾截图助手」。两者同时运行会抢「另存为」对话框。

## v1.1 新增

- 启动检测/停止旧助手  
- 收件箱自动清理（默认 14 天 / 最多 200 张，绳上文件保留）  
- 挂绳/复制/打开/另存为失败时托盘气球提示  
- 托盘显示版本；首次启动三步引导  
- 右键「固定在前排」；托盘可改显隐快捷键

## 数据目录

- 设置 / 日志 / 默认收件箱：`%LOCALAPPDATA%\LiangTu\`
- 默认 Inbox：`%LOCALAPPDATA%\LiangTu\Inbox`
- 托盘菜单可更改收件箱路径、开关自动另存为、开机自启

## 技术栈

C# · WPF · .NET 8 · Win32（另存为自动化、热键、点击穿透）
