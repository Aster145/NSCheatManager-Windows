# Changelog

## 1.0.2 - 2026-09-06

- 修复“打包为 ZIP”复用本地旧缓存的问题；现在每次都读取 Switch 上当前 TID/BID 对应的最新文件，且不会覆盖待上传的本地编辑。
- 修复重复使用相同 ZIP 名称时静默生成 `-2`、`-3` 文件而容易误开旧包的问题；分享文件现在使用填写的名称原子覆盖，时间戳备份仍会保留。
- 修复文件名中的版本小数点被误判为扩展名的问题；仅末尾真正的 `.zip` 会被去除，输入时仍不需要填写后缀。
- 覆盖目标 ZIP 被资源管理器、预览窗格或解压软件占用时，现在会在下载前显示明确路径和处理提示。
- FTP 新建文本文件恢复为使用 Windows 默认关联程序；设置中的编辑器路径仅作为可选覆盖项。
- 修复金手指页已建立 FTP 会话后，通用 FTP 浏览器首次列出仍错误进入 `/` 而忽略默认目录的问题。
- 修复外部编辑器连续保存时多个事件争用 `pending.json.tmp`，可能导致待上传状态保存失败的问题。

## 1.0.1 - 2026-09-05

- 启动时恢复上次选择的设备，但不自动连接任何协议。
- 设置页新增全局 FTP 默认进入目录；连接时严格进入该目录，失败时保持断开。
- FTP 页面可填写文件名并新建本地 TXT 草稿，自动补充 `.txt`，用设定的编辑器打开并加入待上传区。
- 上传前检测远端同名文件并要求确认；拒绝覆盖时停止批次，所有未处理项继续保留。

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
