# Codex Skin 主题制作与发布规范 v1

本文定义主题从制作、验证、预览、发布到升级的统一流程。官方主题源文件只保存在 [Codex-Skin-Store](https://github.com/lixiaobaivv/Codex-Skin-Store)，本客户端仓库只提供校验器、签名器和协议实现，不再复制正式主题资源。关键字“必须”表示发布阻断要求，“建议”表示质量要求。机器可读约束以 `schemas/theme-v1.schema.json` 和 `schemas/theme-repository-v1.schema.json` 为准。

## 1. 仓库结构

```text
theme-repository.json
schemas/
  theme-v1.schema.json
  theme-repository-v1.schema.json
themes/
  <theme-id>.json
previews/
  <theme-id>.png
backgrounds/
  <theme-id>.jpg
logos/
  <theme-id>.png
pets/
  <theme-id>.png
```

- `theme-repository.json`、两个 Schema 和 `themes/` 必须存在。
- 主题文件名必须等于 `codeThemeId`，ID 使用 2–64 位小写字母、数字和连字符。
- `previews/` 是商店截图；`backgrounds/` 是运行时背景；`logos/` 是 masthead Logo；`pets/` 是可选宠物。资源不得跨目录复用路径。
- 发布包只允许 JSON 与 PNG/JPG/JPEG/WebP/AVIF，并检查图片文件头与扩展名一致。脚本、HTML、二进制、字体、远程 URL 和符号链接不得成为运行时资源。

## 2. 图片规格

| 资源 | 必须 | 建议尺寸 | 建议格式 | 用途 |
| --- | --- | --- | --- | --- |
| preview | 是 | 1600×1000 或同等 16:10 | PNG/WebP | 商店中检查完整真实界面 |
| background | 否 | 2560×1600，至少 1600px 宽 | JPG/WebP | 主背景与空白首页 hero |
| logo | 否 | 600×160 以内透明画布 | PNG/WebP | masthead 品牌标识 |
| pet | 否 | 256×256 或 512×512 透明画布 | PNG/WebP | hero 右下安全区 |

单文件硬限制 20 MB，整个发布目录硬限制 200 MB/2000 文件。建议 preview 不超过 1.5 MB、background 不超过 3 MB、Logo/宠物各不超过 500 KB。图片必须具有合法使用和再分发权利。

## 3. 清单规则

从 `templates/theme-v1.template.json` 复制清单，并遵守：

模板展示了全部可选能力；没有对应 Logo、背景或宠物资源时，必须同时删除相应可选字段。

- `version` 使用语义化版本，例如 `1.2.0`。已公开的 `codeThemeId` 永不转让或复用。
- `variant` 描述主题本身的明暗模式，不跟随系统自动切换。
- `accent`、`ink`、`surface` 使用六位十六进制颜色，并保证正文可读。
- `theme.backgroundFit` 默认为 `smart`：比例接近 Hero 时铺满显示，裁切超过 34% 时自动完整显示并用同图模糊铺底；作者也可固定为 `cover` 或 `contain`。
- `theme.backgroundPosition` 用于保护人物或主体焦点，只能使用模板列出的安全定位值；人物头部靠近画面上缘时优先使用 `center top`。
- `home.quickActions` 必须正好四项；点击后只向真实输入框写入 `prompt`。
- `home.pet` 只能提供图片、替代文本和 48–220 的显示尺寸，不能包含脚本或点击行为。
- `home.sidebarLabels` 只能修改新建任务、已安排、插件和设置四个固定入口。
- 项目、任务、对话、进度和账号数据必须保持 Codex 原生内容及行为。
- `copy.replacePlaceholders` 只修改输入提示，不得用来改写页面业务数据。

未知字段会被拒绝。主题不能提供 CSS、JavaScript、HTML、命令、DLL、远程图片或仓库外文件路径。

## 4. 制作流程

1. 复制 `templates/theme-v1.template.json` 为 `themes/<theme-id>.json`。
2. 准备 preview；按需准备 background、Logo 和 pet，并写入对应目录。
3. 修改颜色、字体、首页文案和四个提示词。
4. 生成索引并验证：

```bash
cargo run --manifest-path src-tauri/Cargo.toml --bin catalog-tool -- index . "My Theme Repository"
cargo run --manifest-path src-tauri/Cargo.toml --bin catalog-tool -- validate .
```

5. 使用 QA fixture 或真实 Codex 检查空白首页、会话页、窄窗口和回滚。
6. 更新 preview，使其反映当前版本真实界面，而不是仅包含背景原图。

## 5. 发布流程

在 Codex-Skin-Store 工作副本中，发布前执行：

```bash
cargo run --manifest-path src-tauri/Cargo.toml --bin catalog-tool -- index .
cargo run --manifest-path src-tauri/Cargo.toml --bin catalog-tool -- validate .
cargo run --manifest-path src-tauri/Cargo.toml --bin catalog-tool -- pack . artifacts/theme-catalog-v1.zip
```

`theme-index` 会扫描全部主题、校验资源并按 ID 生成索引。`theme-validate` 会拒绝漏索引、重复 ID、错误目录、缺失资源、未知字段和超限文件。`theme-pack` 只打包客户端允许的文件，并输出 SHA-256。

正式主题仓库使用：

- `main`：客户端读取的稳定目录；每次提交都必须通过 CI。
- Pull Request：新主题和更新先在 PR 中审核，不直接写入 `main`。
- Git tag：仓库快照可使用 `themes-v1-YYYYMMDD`；单主题版本仍以清单 `version` 为准。
- 回滚：撤销有问题的清单版本或从索引暂时移除；不得让旧 ID 指向另一个主题。

审核合并后不再人工构建或填写包元数据。维护者手动运行 Codex-Skin 的 **Publish reviewed Codex-Skin themes**，这次操作就是发布批准；工作流发现 `package: null` 主题后，按 Store 提交 SHA 签名并进行双平台验签。Store 随后自动下载复验、回填目录和部署网页。仓库管理员还可为 `theme-publishing` Environment 配置 Required reviewers，增加第二层审批。

## 6. 验收清单

- Schema、Core 校验和索引一致性全部通过。
- Windows 与 macOS 使用相同清单编译结果。
- 主页固定四张卡，输入框不覆盖可见卡片，窄屏无横向溢出。
- 会话页背景、助手/用户气泡和输入框可读。
- 原生项目、任务、进度、用户区未被创建、删除、改名或改变行为。
- 宠物不遮挡标题、标签、快捷卡和输入框；没有宠物时布局不留空洞。
- 连续切换两个主题后无旧节点、旧监听器或旧文案残留。
- 回滚后标题、导航文案、主页、样式和 CDP 持久脚本全部恢复。
- preview 与发布版本一致，素材授权和作者信息明确。
