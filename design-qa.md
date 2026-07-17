# Design QA — Codex-Skin

## 验收范围

- 项目：Tauri 2 + Rust 跨平台主题客户端，前端使用 TypeScript/Vite。
- 验收日期：2026-07-17（Asia/Shanghai）。
- 客户端视口：标准布局 1120×780；最小支持布局 820×620。
- QA 模式：仅 Vite 开发环境允许 `?qa=1`，发布构建不提供模拟桥接。
- 正式主题数据由 [Codex-Skin-Store](https://github.com/lixiaobaivv/Codex-Skin-Store) 维护，客户端发布包不内置在线主题资源。

## 客户端界面验证

| 视口 | 结果 |
| --- | --- |
| 1120×780 | 分类栏、主题卡片、预览、同步状态、应用/重启/回滚操作均完整显示；无横向溢出 |
| 820×620 | 主题卡片保持双列紧凑布局，底部操作区可用；无卡片与进度区重叠 |

- Chromium 控制台：错误 0，警告 0。
- 隐藏进度元素保留网格行尺寸，避免加载状态变化时卡片区域跳动。
- Windows 与 macOS 共用相同界面和交互；平台差异仅位于 Rust 系统适配层。

## 运行时与安全验证

- Tauri 单实例接收 `dreamskin://` 和 `.dreamskin` 激活并将现有窗口置前。
- 主题目录下载到临时目录，完成路径、Schema、资源、图片和边界校验后才原子替换缓存。
- 签名包执行严格 JSON、RFC8785/Ed25519、SHA-256、ZIP 路径、文件数量、媒体格式、尺寸与像素验证。
- CDP 仅允许本机回环目标，注入脚本持久化到新文档并支持验证和回滚。
- Rust 自动化测试覆盖签名篡改、ZIP 穿越/重复项、畸形深链接、SemVer 版本选择和状态持久化。

## 历史主题效果证据

`qa/captures/` 与 `qa/comparisons/` 中的图片继续作为 hero、快捷卡、输入框、气泡和主题视觉的历史证据。它们不是当前 Tauri 客户端外壳的截图，也不用于证明 Codex 原生 DOM 在未来版本中保持不变。

## 当前结论

本地前端构建、Rust 测试、签名主题验证、在线目录同步和 Windows Tauri 可执行文件构建均已通过。macOS 代码与 PKG 脚本已迁移，最终发布前仍以 GitHub Actions 的 Apple Silicon/Intel runner 结果作为平台打包门禁。

final result: passed
