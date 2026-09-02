# Changelog

## 1.0.0 - 2026-09-02

NSCheatManager-Windows 首个正式版本。

### 核心功能

- 免安装、自包含的 Windows 10/11 x64 单文件程序。
- 多设备配置以及 sys-botbase、FTP、Noexs 独立端口设置。
- 从完全断开状态一键识别当前游戏并连接 FTP。
- 金手指页严格限定为当前 TID 下的 `BID.txt` 与 `notes.txt`。
- 完整 FTP 文件浏览、拖放待上传、批量确认上传及安全失败停止。
- 手动写入的本地版本数据库、备注、游戏名与版本名、离线编辑和恢复上传。
- Atmosphère ZIP 导入、打包分享及时间戳备份。
- 截图、截图目录、亮屏、熄屏和 dmnt 分离。
- 完整日志、单实例运行、窗口置顶和中英文即时切换。

### 安全与数据

- 程序启动时不会自动连接 Switch。
- 浏览或下载当前金手指不会自动写入本地数据库。
- 文本编辑后的改动只进入待上传区，不会自动上传。
- 通用 FTP 与 sys-botbase 游戏会话相互独立。
- 运行数据保存在 EXE 同目录的 `data` 文件夹，手动升级时可以原样保留。
- 首版二进制未使用商业代码签名；请从本仓库下载并核对发布页提供的 SHA-256。

---

This is the first stable release of NSCheatManager-Windows: a portable, self-contained Windows x64 client with current-game cheat scoping, independent FTP browsing, confirmed upload queues, local version history, ZIP import/export, screenshots, screen control, logging, single-instance protection, and live Chinese/English switching.
