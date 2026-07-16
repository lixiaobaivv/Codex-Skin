# Design QA — Codex 主题商店

## 验收范围

- 项目：WinForms / .NET 8 Codex 主题商店，使用本机回环 `127.0.0.1:9229` CDP 动态注入前端。
- 验收日期：2026-07-16（Asia/Shanghai）。
- QA 页面：`qa/codex-fixture.html`，独立 Edge headless 配置，不连接或关闭承载任务的真实 Codex。
- 参考图：`previews/kun-stage.png`、`previews/jackson-sage.png`、`previews/enfp-pop.png`、`previews/dilraba-star.png`。
- 实现入口：`src/CodexThemeStore/Program.cs`；主题数据：`themes/*.json`。

## 四主题全屏证据

| 主题 | 参考图 | 实现截图 | 同屏对比 | 视口与状态 |
| --- | --- | --- | --- | --- |
| KUN Stage | `previews/kun-stage.png` | `qa/captures/kun-stage-home-final-v3.png` | `qa/comparisons/kun-stage-comparison-v3.png` | 1586×992，空白主页 |
| Jackson Sage | `previews/jackson-sage.png` | `qa/captures/jackson-sage-home-final-v3.png` | `qa/comparisons/jackson-sage-comparison-v3.png` | 1586×992，空白主页 |
| ENFP Pop | `previews/enfp-pop.png` | `qa/captures/enfp-pop-home-final-v3.png` | `qa/comparisons/enfp-pop-comparison-v3.png` | 1586×992，空白主页；源参考为 3840×2160，比较板使用 contain 归一化 |
| Dilraba Star | `previews/dilraba-star.png` | `qa/captures/dilraba-star-home-final-v5.png` | `qa/comparisons/dilraba-star-comparison-v3.png` | 1586×992，空白主页 |

四主题连续切换诊断均满足：主题主页、侧栏和 live style 存在；`horizontalOverflow=false`；`cardComposerOverlap=false`；`logoLoaded=true`；`consoleErrorCount=0`。

## 重点区域证据

- 侧栏：`qa/comparisons/dilraba-sidebar-focus-v3.png`。
- Hero 与快捷卡：`qa/comparisons/dilraba-hero-cards-focus-v3.png`。
- 输入框：`qa/comparisons/dilraba-composer-focus-v3.png`。
- 对话视图：`qa/captures/dilraba-star-chat-final-v3.png`。
- 快捷卡交互：`qa/captures/dilraba-action-interaction-final-v3.png`。
- 响应式窄屏：`qa/captures/dilraba-responsive-820x900-final-v3.png`。
- 响应式中屏：`qa/captures/dilraba-responsive-1200x800-final-v3.png`。

## 响应式与交互诊断

| 状态 | 诊断结果 |
| --- | --- |
| 820×900 空白主页 | `horizontalOverflow=false`；`cardComposerOverlap=false`；主页滚动 617/816；Logo 720×194 已加载；控制台错误 0 |
| 1200×800 空白主页 | `horizontalOverflow=false`；`cardComposerOverlap=false`；主页滚动 517/536；Logo 720×194 已加载；控制台错误 0 |
| 1586×992 快捷卡交互 | 第一张卡将“请帮我探索并理解当前代码库，先概览结构、关键模块和主要流程。”写入真实 `.ProseMirror`；无 overflow/重叠；控制台错误 0 |
| 1586×992 对话视图 | 用户气泡、助手卡片、背景和底部输入框均命中主题；无 overflow/重叠；Logo 已加载；控制台错误 0 |

## P2 问题、修复和复验证据

| 之前的 P2 | 修复方式 | 修复后证据 |
| --- | --- | --- |
| 侧栏和正文使用 display/serif 字体，中文辨识度和参考图密度不足 | 将 `fonts.ui` 改为中文无衬线字体栈，仅标题使用独立 `fonts.display`；同步提高字号、行高和辅助文本对比度 | `qa/comparisons/dilraba-sidebar-focus-v3.png` 及上方列出的四张 v3 全屏对比图 |
| 文本 Logo 无法形成四套独立品牌识别 | 每套主题新增透明 PNG Logo，通过 `theme.logoImage` Base64 data URL 注入真实 `<img>` | 上方列出的四张最终主页截图；自动诊断 `logoLoaded=true` |
| 窄屏卡片可能被固定输入框遮挡，原生侧栏可能露出 | 按主面板宽度切换 compact/narrow；为输入框动态预留空间；窄屏保留自定义侧栏并让主页自身滚动 | `qa/captures/dilraba-responsive-820x900-final-v3.png`、`qa/captures/dilraba-responsive-1200x800-final-v3.png`；`cardComposerOverlap=false` |
| “更多”文字在窄布局换行，视觉不稳定 | 使用本地 Tabler `dots-vertical.svg` 图片，避开 `file://` CSS mask CORS | 两张响应式 final-v3 截图；控制台错误 0 |
| 主题切换后可能残留旧 resize 逻辑或叠加注入 | 注入前移除旧 new-document script 和 resize listener，再注册当前主题 | 四主题连续切换均只显示当前主题，控制台错误 0 |

## 五项视觉检查

- 字体：正文、导航和辅助信息采用中文无衬线；标题保留 display/serif 氛围；最新字号调整未产生截断或溢出。
- 间距：侧栏分组、Hero、四卡、输入框层级明确；桌面和中屏无重叠；820 窄屏双列并通过主页滚动完整访问内容。
- 颜色：四套调色分别为黑金舞台、鼠尾草奶油、ENFP 高饱和青橙、星光蓝紫；气泡、边框、焦点环和辅助文字对比可读。
- 图片质量：四张 Hero 使用生成式位图并压缩为高质量 JPG；四套 Logo 为独立透明 PNG，浏览器自然尺寸 720×194，运行时成功解码。
- 文案：侧栏项目/任务、Hero 标题/副标题/标签、快捷卡标题/描述和输入框占位均按主题独立配置；快捷卡提示词可写入编辑器。

## 最终图像资产

- Hero：`backgrounds/kun-stage-hero-v2.jpg`、`backgrounds/jackson-sage-hero-v2.jpg`、`backgrounds/enfp-pop-hero-v2.jpg`、`backgrounds/dilraba-star-hero-v3.jpg`。
- Logo：`logos/kun-stage-logo-ui-v1.png`、`logos/jackson-sage-logo-ui-v1.png`、`logos/enfp-pop-logo-ui-v1.png`、`logos/dilraba-star-logo-ui-v1.png`。
- ImageGen 用途：人物主题横幅与舞台/纸张/涂鸦/星空背景；四套品牌字标和配套花朵、星光、彩色字符、蝴蝶等视觉元素。Logo 采用色键生成后去背、裁边并缩放为 UI 资产。

## P3 后续项

- 可继续逐件补齐参考图中的拍立得、便签、波形、花朵等独立装饰素材。
- 可把快捷卡图标进一步放大并改为更有填充感的专属插画；当前使用 Codex 原生/Tabler 视觉语言以保持清晰和交互一致。
- ENFP 可另做 16:10 专用构图，以减少 16:9 源参考在 1586×992 验收视口中的裁切差异。

final result: passed
