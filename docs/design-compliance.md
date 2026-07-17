# 设计符合性审查

审查日期：2026-07-16。范围包括 Windows WinForms 客户端、macOS Avalonia 客户端、共享 Core、诊断 CLI、主题仓库 v1、CDP 注入和 CI 安装包。

## 设计要求对照

| 要求 | 状态 | 证据或说明 |
| --- | --- | --- |
| Windows 软件选择和切换主题 | 已真实验证 | Windows 11、Store Codex 26.707.9981.0：保存、CDP 重启、实时注入、主题切换和回滚通过 |
| macOS 软件选择和切换主题 | 已实现 | Avalonia 图形商店支持主题预览、分类、同步、应用、重启和回滚；待真实 Mac 验证 |
| GitHub 专用主题仓库 | 基础符合 | 仓库 v1 清单、Schema、自动同步、本地缓存和 UI 配置已实现；发布前仍需确定公开仓库地址 |
| 双击启动后同步最新目录 | Windows 符合 | 窗口 `Shown` 后自动刷新，失败保留缓存或内置主题 |
| 手动刷新和加速源 | 符合 | GitHub、GH Proxy、GHFast；CLI 也支持 `refresh` |
| 按主题类型选择 | 符合 | Windows WinForms 与 macOS Avalonia 均提供动态分类筛选 |
| 背景、对话框和固定侧栏视觉可更改 | 符合 | 由共享 CSS/JS 生成器应用 |
| 宠物元素允许随主题更改 | 已实现 | 可选 `home.pet` 使用仓库本地图片，运行时限制尺寸并放置在 hero 安全区 |
| 项目和任务不可变更 | 符合 | v1 Schema 禁止相关字段；运行时禁用通用文本替换并保留原生侧栏 |
| 首页主题引导且不拥挤覆盖 | 符合现有夹具 | 首页只在无消息状态出现，为输入框预留空间；已有响应式截图证据 |
| Windows/macOS 完整安装包 | 已实现构建链 | Windows Setup EXE、macOS arm64/x64 PKG；不再以 ZIP 作为正式产物 |
| GitHub CI 编译与发布 | 已实现配置 | PR/main 构建安装包，`v*` 标签创建 Release |

## 本次发现并修复

1. `P1` 单文件发布曾把部分预览嵌入 EXE，但运行时按文件路径读取，可能导致发布版启动失败。所有主题资源现强制 `ExcludeFromSingleFile`，CI 校验四套资源完整性。
2. `P1` 项目只有 ZIP/发布目录，没有安装器。现增加 Windows Inno Setup 和 macOS `pkgbuild`。
3. `P2` 损坏的远程缓存可能在自动刷新前阻止商店启动。现在会校验缓存并回退内置主题。
4. `P2` macOS 曾直接运行 app bundle 内二进制。现在通过 LaunchServices `open -n -a ... --args` 启动。
5. `P2` 专用主题仓库只能手改设置文件。Windows UI 现可设置 `owner/repository` 与分支。
6. `P3` 发布目录曾携带 Core PDB，且预览图体积偏大。现自动移除符号并优化预览资源。
7. `P1` 编译器残留不可达的自定义侧栏代码，与项目/任务不可变边界冲突。现删除替代侧栏生成逻辑，只保留原生侧栏样式和固定导航白名单。
8. `P1` CDP 目标过去只依赖标题/URL，可能命中 Codex 辅助窗口。现注入前要求页面同时存在主区、原生侧栏以及输入框或主内容标记。
9. `P2` 消息气泡只覆盖旧版 `data-content-search-unit-key`。现同时兼容新版 `data-message-author-role`，并优先使用稳定的原生侧栏 `aside.app-shell-left-panel`。
10. `P2` 跨平台 CLI 曾在 Linux 拒绝 `list`、`compile` 和已有 CDP 页面的注入。现只有发现、启动和重启 Codex.app 的命令要求 macOS。
11. `P1` 真实 Windows 回滚暴露了待执行动画帧重新创建主题主页的问题。现统一使用 `dispose` 取消待执行帧，并恢复标题、固定导航文案、主页和主题属性。

## 发布前剩余项

1. 在真实 Intel 与 Apple Silicon Mac 上验证 Codex `.app` 名称、Avalonia 界面、LaunchServices 启动、CDP 注入和回滚。
2. 当前无法提供商业代码签名与 Apple 公证；发布说明必须明确 Windows SmartScreen 和 macOS Gatekeeper 的手动确认步骤。
3. 首次推送后确认 GitHub 托管 runner 上的 Inno Setup 与 `pkgbuild` 产物可安装；当前 Linux 环境无法执行这两个平台安装器。
4. 将默认仓库从当前代码仓库切换到最终公开主题仓库；私有仓库无法供未登录客户自动同步。
