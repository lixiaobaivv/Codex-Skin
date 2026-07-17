# 设计符合性审查

审查日期：2026-07-17。范围包括 Windows/macOS Tauri 客户端、Rust 核心与诊断工具、Codex-Skin-Store 主题仓库 v1、CDP 注入和 CI 安装包。

## 设计要求对照

| 要求 | 状态 | 证据或说明 |
| --- | --- | --- |
| Windows 软件选择和切换主题 | 已真实验证 | Windows 11、Store Codex 26.707.9981.0：保存、CDP 重启、实时注入、主题切换和回滚通过 |
| macOS 软件选择和切换主题 | 已实现 | Tauri 图形商店支持主题预览、分类、同步、应用、重启和回滚；待真实 Mac 验证 |
| GitHub 专用主题仓库 | 符合 | 固定使用公开 `lixiaobaivv/Codex-Skin-Store/main`；客户端只允许选择下载加速源 |
| 双击启动后同步最新目录 | Windows/macOS 符合 | 首次启动允许空目录显示并自动刷新，失败保留可重试界面；后续失败保留缓存 |
| 手动刷新和加速源 | 符合 | GitHub、GH Proxy、GHFast；CLI 也支持 `refresh` |
| 按主题类型选择 | 符合 | Windows 与 macOS 共用 Tauri 动态分类筛选界面 |
| 背景、对话框和固定侧栏视觉可更改 | 符合 | 由共享 CSS/JS 生成器应用 |
| 宠物元素允许随主题更改 | 已实现 | 可选 `home.pet` 使用仓库本地图片，运行时限制尺寸并放置在 hero 安全区 |
| 项目和任务不可变更 | 符合 | v1 Schema 禁止相关字段；运行时禁用通用文本替换并保留原生侧栏 |
| 首页主题引导且不拥挤覆盖 | 符合 | 首页只在无消息状态出现；四卡默认同排、主区小于 760px 才降为两列，并为输入框动态预留空间 |
| 主题运行性能 | 已优化 | 结构增量观察取代全页/逐帧扫描；忽略流式消息内部变化，移除主区和消息气泡大面积实时模糊 |
| 网页一键导入 | Windows 符合，macOS 待实机验收 | Windows Setup 自动注册并清理关联；macOS PKG plist、Tauri 激活和 Rust 验签导入已接入 |
| Windows/macOS 完整安装包 | 已实现构建链 | Windows Setup EXE、macOS arm64/x64 PKG；不再以 ZIP 作为正式产物 |
| GitHub CI 编译与发布 | 已实现配置 | PR/main 构建安装包，`v*` 标签创建 Release |

## 本次发现并修复

1. `P1` 单文件发布曾把部分预览嵌入 EXE，但运行时按文件路径读取，可能导致发布版启动失败。安装包现不再携带在线主题资源，CI 校验首次启动可从官方目录同步四套主题。
2. `P1` 项目只有 ZIP/发布目录，没有安装器。现增加 Windows Inno Setup 和 macOS `pkgbuild`。
3. `P2` 损坏或不存在的远程缓存可能在自动刷新前阻止商店启动。现在允许空目录启动并保留线路选择与刷新操作。
4. `P2` macOS 曾直接运行 app bundle 内二进制，随后又错误地把 `.app` 路径传给 `open -a`。现在通过 LaunchServices `open -n /Applications/Codex.app --args ...` 启动并检查退出状态。
5. `P2` 旧客户端允许任意修改主题仓库，容易绕过官方审核边界。现固定使用 Codex-Skin-Store，只保留直连和镜像选择，并自动迁移旧设置。
6. `P3` 发布目录曾携带 Core PDB，且预览图体积偏大。现自动移除符号并优化预览资源。
7. `P1` 编译器残留不可达的自定义侧栏代码，与项目/任务不可变边界冲突。现删除替代侧栏生成逻辑，只保留原生侧栏样式和固定导航白名单。
8. `P1` CDP 目标过去只依赖标题/URL，可能命中 Codex 辅助窗口。现注入前要求页面同时存在主区、原生侧栏以及输入框或主内容标记。
9. `P2` 消息气泡只覆盖旧版 `data-content-search-unit-key`。现同时兼容新版 `data-message-author-role`，并优先使用稳定的原生侧栏 `aside.app-shell-left-panel`。
10. `P2` 跨平台 CLI 曾在 Linux 拒绝 `list`、`compile` 和已有 CDP 页面的注入。现只有发现、启动和重启 Codex.app 的命令要求 macOS。
11. `P1` 真实 Windows 回滚暴露了待执行动画帧重新创建主题主页的问题。现统一使用 `dispose` 取消待执行帧，并恢复标题、固定导航文案、主页和主题属性。
12. `P1` MutationObserver 曾监听整个页面的文字、class 和属性；流式回复每帧触发 TreeWalker、全页控件扫描和布局读取。现只监听相关结构与三个有限属性，并忽略消息/编辑器内部变化。
13. `P2` 首页操作卡使用 `auto-fit + 280px`，1080p 高度场景下可出现三加一换行。现默认固定四列并缩小卡片，主内容区小于 760px 时才响应式降列。
14. `P2` Windows Setup 未自动注册网页导入协议。现安装后执行所有权安全的 `protocol register`，卸载前执行 `protocol unregister`。

## 发布前剩余项

1. 在真实 Intel 与 Apple Silicon Mac 上验证 Codex `.app` 名称、Tauri 界面、LaunchServices 启动、CDP 注入和回滚。
2. 当前无法提供商业代码签名与 Apple 公证；发布说明必须明确 Windows SmartScreen 和 macOS Gatekeeper 的手动确认步骤。
3. 在真实 Windows 安装/卸载 Setup，确认网页浏览器可拉起客户端且卸载不遗留协议关联。
4. 在真实 macOS 上验证 `.dreamskin` 文件关联、冷启动/已运行两种 URL 激活、确认窗口和安装后应用；自动测试已覆盖共享验签安装器与 plist 契约。
