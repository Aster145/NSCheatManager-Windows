# NSCheatManager-Windows

面向自有或获授权 CFW Nintendo Switch 的免安装 Windows 管理工具。程序通过 sys-botbase 识别当前游戏，通过 FTP 管理文件，并可配合 Noexs 分离 dmnt。

[下载正式版](https://github.com/Aster145/NSCheatManager-Windows/releases/latest) · [安卓版项目](https://github.com/Aster145/NSCheatManager)

## 主要功能

- 保存并切换多个 Switch 设备配置。
- 使用 sys-botbase 识别当前 TID、BID、main 与 heap。
- 一键连接并仅列出当前 `BID.txt` 与 `notes.txt`。
- 浏览 Switch SD 卡；下载、上传、重命名和删除普通文件。
- 记住最后选择的设备，并可设置全局 FTP 默认进入目录；启动时仍不会自动连接。
- 在 FTP 当前目录新建本地 TXT 草稿，用设定的编辑器打开后加入待上传区。
- 编辑后的文件先进入待上传区，由用户点击确认后统一上传。
- 手动写入本地 SQLite 数据库，填写备注并离线编辑历史版本。
- 导入或打包 Atmosphère 格式 ZIP，并自动保留时间戳备份。
- 截图、亮屏、熄屏及 dmnt 分离。
- 简体中文与 English 即时切换。

## 下载与运行

1. 从 [Releases](https://github.com/Aster145/NSCheatManager-Windows/releases) 下载 `NSCheatManager-v1.0.1-win-x64.zip`。
2. 解压到普通文件夹。
3. 直接运行 `NSCheatManager.exe`，无需安装 .NET 或安装程序。
4. 在“设置”中填写设备名称、IPv4 地址及端口。

当前发布包没有商业代码签名证书，Windows SmartScreen 可能显示未知发布者提示。请只从本仓库 Releases 下载，并使用 Release 中的 `SHA256SUMS.txt` 核对文件完整性。

程序会在 EXE 同目录创建 `data` 文件夹，用于保存设备配置、数据库、日志、截图、下载文件及备份。升级时请保留该文件夹，只替换程序文件。

日志可能包含设备 IP、端口、文件路径和异常堆栈；将日志附加到公开 Issue 前请先脱敏。

默认端口：sys-botbase `6000`、FTP `21`、Noexs `7331`。程序启动时不会自动连接设备；进入相应功能并主动点击后才会连接。

FTP 默认目录是全局设置，必须使用以 `/` 开头的绝对路径。FTP“新建文本文件”会自动补充 `.txt` 后缀，只创建本地草稿并加入待上传区；远端存在同名文件时，确认上传前会再次询问是否覆盖。

## 安全提示

这是非官方社区软件。远端文件和内存操作可能导致软件崩溃或数据损坏：

- 仅在自有或获授权设备上使用。
- 操作前备份重要数据。
- 不要把未经认证的 Switch 服务暴露到互联网。
- 上传批次遇到首个失败后会停止，其余项目继续保留在待上传区。

## 从源码构建

需要 .NET SDK 9.0.317 或更新版本以及 Windows 10/11 x64：

```powershell
dotnet build .\NSCheatManager.Windows.csproj -c Release
dotnet publish .\NSCheatManager.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## English

NSCheatManager-Windows is a portable Windows client for an owned or authorized CFW Nintendo Switch. It uses sys-botbase for game identification, FTP for file operations, and Noexs for optional dmnt detachment.

Download the self-contained `win-x64` ZIP from [Releases](https://github.com/Aster145/NSCheatManager-Windows/releases), extract it, and run `NSCheatManager.exe`. No installer or separate .NET runtime is required. Keep the generated `data` directory when replacing the application during a manual update.

The current binaries are unsigned and may trigger a Windows SmartScreen warning. Download them only from this repository's Releases and verify them against `SHA256SUMS.txt`.

Use this software only on devices you own or are authorized to manage. Keep backups and never expose unauthenticated Switch services to the Internet.

## 社区与许可 / Community and license

- QQ 群：`457965140`
- Android: [Aster145/NSCheatManager](https://github.com/Aster145/NSCheatManager)
- License: [GNU GPL v3 only](LICENSE.txt)
- Third-party notices: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)

Copyright (C) 2026 Aster145. This project is distributed under `GPL-3.0-only`.
