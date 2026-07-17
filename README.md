# Codex-Skin

[简体中文](README.md) | [English](README.en.md)

Codex-Skin 是适用于 Windows 和 macOS 的 Codex Desktop 主题客户端。它可以浏览主题、预览效果、切换主题并恢复默认外观，也支持从 [Codex-Skin-Store](https://lixiaobaivv.github.io/Codex-Skin-Store/) 一键导入经过签名验证的主题。

> Codex-Skin 是社区开源项目，不是 OpenAI 或 Codex 官方产品。程序不会修改 Codex 的签名安装包，也不会读取 API Key、项目文件、任务内容或聊天数据。

![Codex-Skin 主题目录](docs/images/theme-store-desktop.png)

## 下载

从 [最新 Release](https://github.com/lixiaobaivv/Codex-Skin/releases/latest) 选择适合系统的文件：

| 系统 | 推荐文件 | 说明 |
| --- | --- | --- |
| Windows x64 | `Codex-Skin-Setup-win-x64.exe` | 完整安装并自动注册网页导入协议 |
| macOS Apple Silicon | `Codex-Skin-osx-arm64.pkg` | 适用于 M1/M2/M3/M4 等 Apple 芯片 |
| macOS Intel | `Codex-Skin-osx-x64.pkg` | 适用于 Intel Mac |

Windows 只发布 Setup 安装器，不再提供 ZIP 或直接运行的便携包。

Windows 和 macOS 安装包均自带 .NET 运行时，不需要另外安装 .NET SDK。当前发布方无法提供商业 Windows 签名或 Apple 签名与公证，因此系统可能要求手动确认。

## GitHub 镜像加速下载

如果 GitHub Release 下载较慢，可以把原始下载地址放到镜像前缀后面。

原始地址示例：

```text
https://github.com/lixiaobaivv/Codex-Skin/releases/latest/download/Codex-Skin-Setup-win-x64.exe
```

GHFast：

```text
https://ghfast.top/https://github.com/lixiaobaivv/Codex-Skin/releases/latest/download/Codex-Skin-Setup-win-x64.exe
```

GH Proxy：

```text
https://gh-proxy.com/https://github.com/lixiaobaivv/Codex-Skin/releases/latest/download/Codex-Skin-Setup-win-x64.exe
```

把文件名替换为 Release 页面显示的实际值。镜像属于第三方服务，可能失效或缓存旧文件；下载后请使用同一 Release 中的 `Codex-Skin-installers-SHA256SUMS.txt` 校验文件。校验不一致时不要运行。

Windows PowerShell 校验：

```powershell
Get-FileHash .\Codex-Skin-Setup-win-x64.exe -Algorithm SHA256
```

macOS 校验：

```bash
shasum -a 256 Codex-Skin-osx-arm64.pkg
```

## Windows 使用方法

1. 运行 `Codex-Skin-Setup-win-x64.exe` 完成安装。
2. 打开 Codex-Skin，等待主题目录同步完成。
3. 选择分类和主题预览。
4. 点击“应用并重启 Codex”。
5. 需要取消主题时点击“恢复默认”。

Setup 会为当前用户注册 `dreamskin://` 和 `.dreamskin` 文件关联，卸载时只清理由本程序拥有的关联。

## macOS 使用方法

1. Apple Silicon 下载 `Codex-Skin-osx-arm64.pkg`，Intel Mac 下载 `Codex-Skin-osx-x64.pkg`。
2. 安装后从“应用程序”打开 `Codex-Skin.app`。
3. 如果 Gatekeeper 阻止打开，请先确认文件 SHA-256，再到“系统设置 → 隐私与安全性”选择仍要打开。
4. 选择主题后点击“应用并重启 Codex”。

PKG 会声明 `dreamskin://` 和 `.dreamskin` 文件类型。首次安装后建议先打开一次 Codex-Skin，让 macOS LaunchServices 完成关联。

macOS PKG 当前未签名、未公证。主题浏览、同步、Codex 发现、CDP 注入和回滚已实现；仍需更多真实 Apple Silicon 与 Intel 设备验收。

## 从网页一键导入主题

打开 [Codex-Skin-Store](https://lixiaobaivv.github.io/Codex-Skin-Store/)，选择主题并点击“一键导入”。

客户端会依次执行：

1. 显示下载域名、主题 ID、版本和文件大小；
2. 请求用户确认下载；
3. 在窗口中显示下载进度；GitHub 不稳定时按当前选择、官方直连和内置镜像线路自动重试；
4. 校验整包 SHA-256、Ed25519 签名、主题清单和图片；
5. 原子安装主题；
6. 再次询问是否重启 Codex 并应用。

网页点击不会静默安装或切换主题。Windows Setup 和 macOS PKG 会自动注册协议。

验证完成的主题会保留在本机主题列表中，重新打开 Codex-Skin 后仍可选择；同一主题存在多个版本时自动使用最高版本。

也可以双击本地 `.dreamskin` 文件并在客户端窗口中确认安装。

## 主题目录与加速源

桌面主题目录固定来自公开仓库 [lixiaobaivv/Codex-Skin-Store](https://github.com/lixiaobaivv/Codex-Skin-Store)。客户端只允许选择：

- GitHub 官方直连；
- GH Proxy；
- GHFast。

客户端安装包不再携带重复的内置主题。首次启动需要联网同步，之后会使用已验证的本地缓存；更新会先下载到临时目录，全部校验通过后才替换缓存。

网页商店的签名包目录与桌面主题目录是两套独立协议。网页签名包用于安全分发，桌面目录用于客户端浏览和快速切换，两者都不能携带 JavaScript、HTML、CSS、SVG 或可执行文件。

## 主题可以修改什么

主题可以调整：

- 主背景、固定侧栏视觉和顶部区域；
- 用户与助手消息气泡；
- 首页 hero、Logo、标签和四张快捷操作卡；
- 输入框颜色和占位提示；
- 可选宠物图片。

主题不能替换用户项目、任务、进度、对话内容或账号区域的数据。四张快捷卡只会把对应提示词写入真实输入框。

## 常见问题

### 应用主题后 Codex 没有变化

使用“应用并重启 Codex”，确保 Codex 由 Codex-Skin 以本机 CDP 模式启动。程序只连接 `127.0.0.1:9229`，不会开放远程调试端口到局域网。

### 主题同步失败

切换 GitHub、GH Proxy 或 GHFast 后重试。同步失败不会删除上一次有效缓存；首次使用尚无缓存时，切换线路后点击“刷新”。

### 网页点击“一键导入”没有反应

- Windows：重新运行 Setup 安装器修复协议关联。
- macOS：确认使用包含 URL handler 的新版 PKG，并至少启动过一次 Codex-Skin。

### Codex 使用主题后变慢

新版运行时只观察主区域、输入框、侧栏和消息容器的结构变化，不再对流式回复执行全页扫描；同时关闭主区域和消息气泡的大面积实时模糊。遇到旧主题状态时先“恢复默认”，再重新应用新版主题。

## 安全与隐私

- 不修改 Microsoft Store 或 macOS 中 Codex 的签名应用包；
- CDP 仅绑定本机回环地址；
- 主题目录和图片按不可信输入校验；
- `.dreamskin` 限制大小、文件数量、路径、媒体格式和像素尺寸；
- SHA-256 校验传输完整性，Ed25519 验证主题来源；
- 安装和应用始终是两次独立确认。

更详细的边界说明见 [跨平台架构](docs/cross-platform-architecture.md)、[主题目录标准](docs/theme-repository-v1.md) 和 [签名导入兼容说明](docs/dreamskin-compatibility.md)。

## 参与项目

主题投稿请前往 [Codex-Skin-Store 投稿指南](https://github.com/lixiaobaivv/Codex-Skin-Store/blob/main/docs/theme-submission.md)。客户端问题请在 [Codex-Skin Issues](https://github.com/lixiaobaivv/Codex-Skin/issues) 提交，并附上系统版本、Codex 版本和复现步骤。

开发、构建和主题制作说明位于 [docs](docs/)；普通用户不需要克隆源码或安装开发工具。
