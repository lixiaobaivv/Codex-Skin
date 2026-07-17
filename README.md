# Codex-Skin

[简体中文](README.md) | [English](README.en.md)

[![CI](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/ci.yml/badge.svg)](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/ci.yml)
[![Build and package](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/build.yml/badge.svg)](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/build.yml)

Codex-Skin 是适用于 Windows 和 macOS 的 Codex Desktop 主题客户端。它提供可视化主题目录、实时预览、安全应用与回滚，并支持从 [Codex-Skin-Store](https://lixiaobaivv.github.io/Codex-Skin-Store/) 导入经过签名验证的主题。

Codex-Skin 让主题的浏览、按需下载、验证、安装、增量更新和应用形成一条完整且可审计的流程。

- 在线主题商店：<https://lixiaobaivv.github.io/Codex-Skin-Store/>
- 无代码主题工坊：<https://lixiaobaivv.github.io/Codex-Skin-Store/submit/>
- 最新客户端下载：<https://github.com/lixiaobaivv/Codex-Skin/releases/latest>

> Codex-Skin 是社区开源项目，不是 OpenAI 或 Codex 官方产品。它不会修改 Codex 的签名安装包，也不会读取 API Key、项目文件、任务内容或聊天数据。

![Codex-Skin 主题目录](docs/images/theme-store-desktop.png)

## 核心功能

- 从官方主题目录同步主题，并按人物、动漫、游戏、风景、极简、节日和其他分类筛选；
- 在应用前查看主题预览、名称、说明和分类；
- 将主题应用到已启动的 Codex，或由 Codex-Skin 重启 Codex 后自动应用；
- 一键恢复 Codex 默认外观；
- 支持 `dreamskin://` 网页导入和本地 `.dreamskin` 文件导入；
- 验证 SHA-256、Ed25519/RFC8785 签名、主题清单、ZIP 路径和图片内容；
- 通过轻量远程索引增量检查更新，在 GitHub、GH Proxy 和 GHFast 之间自动回退；
- 预览图进入可视区域时才下载，完整主题资源在应用前按需下载并持久缓存；
- 显示在线、已下载和有更新状态，支持订阅主题并在刷新目录时自动更新本地资源；
- 使用单实例窗口接收网页链接和文件激活，避免重复启动客户端。

## 下载

从 [最新 Release](https://github.com/lixiaobaivv/Codex-Skin/releases/latest) 下载对应平台的安装包：

| 平台 | 文件 | 说明 |
| --- | --- | --- |
| Windows x64 | `Codex-Skin-Setup-win-x64.exe` | 图形安装器，注册网页协议和主题文件关联 |
| macOS Apple Silicon | `Codex-Skin-osx-arm64.pkg` | 适用于 Apple 芯片 Mac |
| macOS Intel | `Codex-Skin-osx-x64.pkg` | 适用于 Intel Mac |

Windows 仅提供 Setup 安装器。Windows 使用系统 WebView2，macOS 使用系统 WKWebView，不需要额外安装应用运行时。

当前发布包没有商业代码签名：Windows SmartScreen 或 macOS Gatekeeper 可能要求手动确认。运行前请从同一 Release 下载 `Codex-Skin-installers-SHA256SUMS.txt` 并核对文件哈希。

Windows PowerShell：

```powershell
Get-FileHash .\Codex-Skin-Setup-win-x64.exe -Algorithm SHA256
```

macOS：

```bash
shasum -a 256 Codex-Skin-osx-arm64.pkg
```

## 快速开始

### Windows

1. 安装 `Codex-Skin-Setup-win-x64.exe`。
2. 从开始菜单打开 Codex-Skin。
3. 首次使用时点击“刷新”，等待官方主题目录同步完成。
4. 选择分类和主题卡片，然后点击“应用并重启 Codex”。
5. 需要取消主题时点击“恢复默认”。

Setup 会为当前用户注册 `dreamskin://` 和 `.dreamskin`。卸载时只清理由 Codex-Skin 创建的协议与文件关联。

### macOS

1. 根据处理器安装 `Codex-Skin-osx-arm64.pkg` 或 `Codex-Skin-osx-x64.pkg`。
2. 从“应用程序”打开 `Codex-Skin.app`。
3. 如果 Gatekeeper 阻止打开，请先核对 SHA-256，再前往“系统设置 → 隐私与安全性”选择仍要打开。
4. 同步主题目录，选择主题并点击“应用并重启 Codex”。

PKG 会声明 `dreamskin://` 和 `.dreamskin` 文件类型。安装后请至少启动一次 Codex-Skin，让 LaunchServices 完成系统关联。

macOS PKG 当前未签名、未公证。CI 已在 Apple Silicon 与 Intel 目标上完成 PKG 构建验证；发布前仍建议在对应真实设备上验收系统关联与启动行为。

## 应用和恢复主题

窗口底部提供三个操作：

- **应用主题**：向当前以主题模式运行的 Codex 应用所选主题，不重启 Codex；
- **应用并重启 Codex**：关闭正在运行的 Codex，以仅绑定本机回环的 CDP 参数重新启动，并在连接成功后应用主题；这是推荐操作；
- **恢复默认**：移除当前主题注入和持久的新页面脚本，恢复 Codex 默认外观。

Codex-Skin 只连接 `127.0.0.1:9229` 或等价的本机回环目标，不会把调试端口开放到局域网。主题应用失败时，客户端会显示明确错误，不会修改 Codex 安装目录。

## 导入主题

### 从网页一键导入

1. 打开 [Codex-Skin-Store](https://lixiaobaivv.github.io/Codex-Skin-Store/) 并选择主题。
2. 点击“一键导入”，浏览器会打开 `dreamskin://` 链接。
3. Codex-Skin 显示来源和主题提示，并在下载前请求确认。
4. 客户端下载主题包，验证大小、SHA-256、签名、清单和图片。
5. 验证成功后主题被原子安装到本地主题库，但不会自动应用。
6. 在主题列表中选择它，再决定立即应用或重启 Codex 后应用。

网页不能静默安装或切换主题。如果客户端已经运行，链接会交给现有窗口并将其置前。

### 导入本地文件

双击已下载的 `.dreamskin` 文件，或用 Codex-Skin 打开它。客户端会显示本地路径并请求确认，然后执行与网页导入相同的签名和资源校验。

同一主题可安装多个版本，列表会自动选择最高 SemVer 版本。同一 ID 和版本如果内容不同会被拒绝，避免不可变版本被覆盖。

## 主题目录与下载线路

桌面主题目录固定来自公开仓库 [lixiaobaivv/Codex-Skin-Store](https://github.com/lixiaobaivv/Codex-Skin-Store)。可选线路包括：

- GitHub 官方直连；
- GH Proxy；
- GHFast。

同步只请求内容寻址的轻量索引，并使用 HTTP ETag 避免重复传输没有变化的目录。客户端会优先使用当前线路，再自动尝试其他内置线路；所有线路失败时，上一次有效目录仍可继续使用。

预览图进入可视区域时才按需下载。选择应用主题后，客户端只补齐该主题缺少或哈希发生变化的清单、背景、Logo 和宠物图片；每个资源都会核对声明大小、SHA-256 和图片内容，再原子写入持久缓存。主题从远程目录下架后不会继续作为在线主题显示，但已安装的签名版本仍可离线使用。

主题卡片会标记“在线”“已下载”“已订阅”或“有更新”。“下载主题”只保存资源而不应用；订阅会在当前版本尚未下载时立即下载，并在以后刷新目录时自动补齐新版本。取消订阅不会删除已经下载的本地主题。

安装包不内置在线主题，因此首次使用需要联网同步轻量索引。第三方镜像可能失效或缓存旧内容，客户端仍会按索引中的大小和 SHA-256 校验每个下载结果。

## 主题能修改什么

主题是声明式数据，可以设置：

- 背景、表面、文字、边框和强调色；
- 原生侧栏和顶部区域的视觉样式；
- 用户与助手消息气泡；
- 首页 Hero、Logo、标签、说明和快捷操作卡；
- 输入框外观和占位文案；
- 可选背景、Logo 和宠物图片。

主题不能携带任意 CSS、JavaScript、HTML、SVG 或可执行文件，也不能替换项目、任务、对话、进度和账号数据。快捷操作卡只能把预设提示词写入真实输入框。

## 安全设计

- 不修改 Microsoft Store 或 macOS 中 Codex 的签名应用；
- CDP 连接仅接受固定端口上的本机回环目标；
- 主题目录、清单和图片始终按不可信输入处理；
- `.dreamskin` 限制 URI、下载、重定向、文件数量、解压大小、路径、格式、尺寸和像素总量；
- 深链接拒绝未知字段、重复字段、私网地址和不安全重定向；
- SHA-256 保护传输完整性，Ed25519/RFC8785 验证发布者签名；
- 下载安装和主题应用是两个独立的用户确认步骤；
- 缓存和安装目录使用临时位置与原子替换，失败不会破坏上一次有效状态。

详细协议见 [跨平台架构](docs/cross-platform-architecture.md)、[主题目录标准](docs/theme-repository-v1.md) 和 [DreamSkin 兼容说明](docs/dreamskin-compatibility.md)。

## 常见问题

### 首次打开没有主题

安装包不携带主题资源。保持联网，选择下载线路并点击“刷新”。同步成功后主题卡片会自动出现。

### 点击“应用主题”提示 Codex 未以主题模式启动

第一次应用请使用“应用并重启 Codex”。此操作会用本机回环 CDP 参数启动 Codex，之后可使用不重启的“应用主题”切换主题。

### 网页点击“一键导入”没有反应

- Windows：重新运行 Setup 修复当前用户的协议关联；
- macOS：确认已安装包含 URL handler 的 PKG，并至少启动过一次 Codex-Skin；
- 仍无法唤起时，从网页下载 `.dreamskin` 文件并手动打开。

### 同步失败

切换 GitHub、GH Proxy 或 GHFast 后重试。全部线路失败不会删除已有缓存，可以稍后再次刷新。

### 系统提示应用来自未知发布者

当前安装包未进行商业签名或 Apple 公证。只从本仓库 Release 下载，并先核对 SHA-256；无法确认来源时不要运行。

## 技术架构

Codex-Skin 使用 Tauri 2、Rust 和 TypeScript/Vite 构建：

- **界面层**（`src/`）：主题浏览、分类、预览、确认流程和状态反馈；
- **应用桥接层**（`src-tauri/src/lib.rs`）：Tauri Command/Event、单实例、深链接和文件激活；
- **领域核心层**（`repository.rs`、`catalog.rs`、`compiler.rs`、`dreamskin.rs`、`protocol.rs`）：同步、校验、版本选择、主题编译、验签和安全下载；
- **运行时集成层**（`cdp.rs`、`platform.rs`）：Codex 发现与启动、回环 CDP 注入、验证和回滚；
- **工具与发布层**（`authoring.rs`、`src-tauri/src/bin/`、`installer/`）：目录制作、签名包诊断和跨平台安装包。

主题目录的数据流为：条件同步轻量索引 → 增量合并目录状态 → 可视区域按需缓存预览 → 应用前下载并校验缺少或变化的资源 → 编译声明式主题 → 通过回环 CDP 应用到 Codex。`.dreamskin` 使用独立的签名导入链路，验证成功后进入本地不可变版本库。

Windows 与 macOS 共用界面和领域逻辑，平台差异集中在 Codex 发现、进程启动、窗口激活和安装器关联。

## 项目结构

```text
src/                    TypeScript/Vite 界面
src-tauri/src/          Rust 应用核心与平台适配
src-tauri/src/bin/      catalog-tool、dreamskin-verify
installer/windows/      Windows Inno Setup
installer/macos/        macOS App/PKG 打包
tools/dreamskin/        主题签名包制作工具
schemas/                桌面主题目录 Schema
samples/dreamskin/      可复现的签名测试样例
tests/                  合同与发布配置测试
docs/                   架构、协议和主题制作文档
```

## 本地开发

需要 Node.js 22、稳定版 Rust 和对应平台的 Tauri 系统依赖。Windows 需要 MSVC Build Tools 与 WebView2；macOS 需要 Xcode Command Line Tools。

```bash
npm ci
npm run tauri -- dev
```

提交前运行完整检查：

```bash
npm run build
node --test tests/dreamskin-catalog-fixtures.test.mjs tests/dreamskin-fixture.test.mjs tests/release-contract.test.mjs
cargo fmt --manifest-path src-tauri/Cargo.toml -- --check
cargo clippy --manifest-path src-tauri/Cargo.toml --all-targets --locked -- -D warnings
cargo test --manifest-path src-tauri/Cargo.toml --locked
```

构建当前平台的优化版应用：

```bash
npm run tauri -- build --no-bundle
```

Windows Setup 和两个 macOS PKG 由 [Build and package](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/build.yml) 工作流生成。

## 制作和投稿主题

普通创作者可以直接使用 [Codex-Skin-Store 在线主题工坊](https://lixiaobaivv.github.io/Codex-Skin-Store/submit/)，不需要 Fork 仓库、安装 Node.js 或 Rust、手写 JSON、计算 SHA-256，也不需要接触官方签名私钥。

1. 填写主题名称、稳定的英文主题 ID、作者和素材许可；
2. 调整强调色、文字色、界面底色与首页文案；
3. 在页面右侧即时预览，文字草稿会保存在当前浏览器；
4. 上传来自真实 Codex 界面的 PNG 效果图，可选上传背景图片；
5. 生成标准投稿 ZIP；
6. 打开 GitHub 投稿表单，把 ZIP 拖入“标准投稿包”区域。

标准投稿包会自动生成桌面主题清单、`package: null` 的商店草案、客户端预览、网页预览、可选背景以及素材许可说明。提交表单不会直接发布主题：维护者先检查真实效果与素材权利，再把投稿包转换为审核 PR。PR 中的封闭 Schema 和目录检查通过后，由可信 CI 使用精确 Store 提交构建并签名 `.dreamskin`，完成 Windows、macOS 双平台验签，最后更新内容寻址的远程目录。客户端在下一次刷新时按需发现并增量下载新主题。

需要自定义 Logo、宠物、四张快捷操作卡、字体或完整文案的作者，可以使用 [高级主题投稿流程](https://github.com/lixiaobaivv/Codex-Skin-Store/blob/main/docs/theme-submission.md#高级作者流程)。主题始终是声明式数据；投稿不能携带任意 CSS、JavaScript、HTML、SVG、命令或可执行文件。

本仓库的 [主题制作指南](docs/theme-authoring.md) 面向目录维护者和协议开发者。[签名样例发布说明](docs/publish-signed-sample.md) 用于维护测试夹具，不是普通作者的投稿步骤。Rust 工具 `catalog-tool` 可以生成索引、验证目录和打包；`dreamskin-verify` 可以针对 Windows 或 macOS 验证签名主题包。

## 反馈与贡献

客户端问题请提交到 [Codex-Skin Issues](https://github.com/lixiaobaivv/Codex-Skin/issues)，并附上操作系统、Codex 版本、Codex-Skin 版本、复现步骤和错误信息。代码贡献应先通过上述全部本地检查。
