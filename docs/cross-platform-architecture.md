# 跨平台客户端边界

主题协议和平台集成必须分层，避免主题仓库了解 Codex 安装位置或获得系统执行权限。

```text
Theme repository v1
        |
        v
Catalog sync + validation + cache       (Windows / macOS 共用)
        |
        v
Theme compiler: JSON -> CSS + bounded JS (Windows / macOS 共用)
        |
        +-------------------+
        |                   |
        v                   v
Windows adapter       macOS adapter
Store/Patched/CDP     app bundle discovery/CDP
```

平台适配器只负责四件事：发现 Codex、以本机回环 CDP 参数启动、向已验证的本机页面注入生成结果、恢复默认。仓库下载层不能启动进程，主题清单不能提供 CSS、JavaScript、HTML 或命令。

共享实现位于 `src/CodexThemeStore.Core`，包含仓库同步与校验、`ThemeDefinition`、`CssBuilder`、`JsBuilder`、状态编译器、CDP 协议和平台接口。Windows WinForms 项目与 `src/CodexThemeStore.Desktop` Avalonia 图形客户端都通过项目引用复用 Core；`src/CodexThemeStore.Cli` 保留为诊断入口。

`MacOsCodexAdapter` 当前负责：

1. 发现 `/Applications`、用户 `Applications` 或 `CODEX_APP_PATH` 指定的 `.app`。
2. 定位 `Contents/MacOS` 主可执行文件并识别同一 app bundle 内的辅助进程。
3. 使用固定回环 CDP 参数启动、停止和检测 Codex。
4. 复用 `CdpThemeInjector` 注入主题或删除持久 new-document script 后回滚。

macOS PKG 安装 `/Applications/Codex-Skin.app`，图形客户端可浏览、筛选、同步、应用和回滚主题。PKG 注册 `dreamskin://` 与 `.dreamskin`，Avalonia 激活处理器复用共享 Core 完成下载限制、Ed25519 验签、图片解码、原子安装和二次应用确认。CLI 仍可从 v1 `theme.json` 执行 `apply-theme` 或 `restart-theme`。

macOS 适配器的验收条件：

1. 只连接 `127.0.0.1` 或 `localhost` 的固定调试端口。（已由自动测试覆盖）
2. 能定位官方 Codex `.app`，但不修改其签名 app bundle。（已实现，待真实设备验证）
3. 能停止并以 CDP 参数启动 Codex，注入失败会明确超时。（已实现，待真实设备验证）
4. 项目、任务、对话和账号 DOM 不被创建、删除或改写。（共享主题编译器约束）
5. Intel 与 Apple Silicon PKG 在真实设备完成安装测试。（待办；当前发布方无法提供签名和公证）
