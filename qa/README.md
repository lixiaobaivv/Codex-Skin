# 本地 CDP 主题注入夹具

`codex-fixture.html` 是自包含的 Codex 前端测试页，不会启动或关闭真实 Codex。页面标题含 `Codex`，可被当前注入器的 CDP 目标筛选规则识别。

## 1. 启动回环 CDP 浏览器

在项目根目录打开 PowerShell。以下命令使用临时 Edge 用户目录，不影响日常配置：

```powershell
$fixture = (Resolve-Path .\qa\codex-fixture.html).Path -replace '\\', '/'
$profile = Join-Path $env:TEMP 'codex-theme-store-qa-edge'
Start-Process msedge.exe -ArgumentList @(
  "--user-data-dir=$profile"
  '--remote-debugging-address=127.0.0.1'
  '--remote-debugging-port=9229'
  "file:///$fixture"
)
```

使用 Chrome 时，把 `msedge.exe` 换成 `chrome.exe`，并建议将临时目录名改为 `codex-theme-store-qa-chrome`。

确认 CDP 只监听本机回环地址：

```powershell
Invoke-RestMethod http://127.0.0.1:9229/json/list |
  Select-Object title, url, type
```

列表中应出现标题为 `Codex QA Fixture`、类型为 `page` 的目标。注入器和截图脚本都只接受 `127.0.0.1`/`localhost:9229` 的 WebSocket 地址。

## 2. 注入并连续切换主题

保持夹具页和浏览器打开，在项目根目录执行任意命令：

```powershell
.\Codex-Skin.exe apply dilraba-star
.\Codex-Skin.exe apply jackson-sage
.\Codex-Skin.exe apply kun-stage
.\Codex-Skin.exe apply enfp-pop
```

`apply` 会保存主题，并把 CSS/JS 注入 9229 上标题或 URL 含 `Codex` 的页面。夹具页右上角会显示当前 `--codex-theme-id`。

可直接连续执行不同的 `apply` 命令。注入器会先移除上一条 `Page.addScriptToEvaluateOnNewDocument`，再注册当前主题脚本并替换页面元素；刷新后应只出现最后选择的主题，不应叠加旧主题。

## 3. 视觉与交互检查

- 原生侧栏应保留真实项目、任务、进度和用户区；主题只改变背景、边框和白名单固定导航文案，不创建替代侧栏。
- 空白主页应显示 masthead、主题徽章、hero、标签和四张快捷卡。
- 配置 `home.pet` 时，hero 右下安全区应显示对应宠物图片，且不接管点击或遮挡输入框。
- 对话视图用于检查背景可读性、用户/助手气泡和输入框样式。
- 点击快捷卡应把主题提示词写入 `.ProseMirror`。
- 输入文字后点击发送，或按 `Ctrl + Enter`，应动态创建用户消息。
- 隐藏的 `textarea[placeholder]` 会同步输入框中文占位文案。

## 4. CDP 自动截图

`Capture-CdpScreenshot.ps1` 会选择回环端口上的 Codex 页面、设置视口、等待字体与两帧渲染后保存 PNG，并输出一行 JSON 诊断结果。

```powershell
.\qa\Capture-CdpScreenshot.ps1 `
  -OutFile .\qa\captures\dilraba-star-home.png

.\qa\Capture-CdpScreenshot.ps1 `
  -OutFile .\qa\captures\dilraba-responsive-1200x800.png `
  -Width 1200 `
  -Height 800 `
  -BeforeCapture "document.getElementById('view-empty').click()"
```

常用参数：

- `-OutFile`：必填，PNG 输出路径；父目录不存在时自动创建。
- `-Width` / `-Height`：视口尺寸，默认 `1586 × 992`。
- `-BeforeCapture`：截图前在目标页执行的可选 JavaScript，可用于切换视图或准备交互状态。

JSON 会报告主题 ID、首页、遗留自定义侧栏、快捷卡数量、Logo/宠物加载状态、视口、横向溢出、hero 背景长度以及控制台错误。当前版本的 `legacySidebar` 应为 `false`，用于防止替代侧栏回归。

## 选择器覆盖

夹具包含注入器使用的主要结构：

```text
.app-shell-left-panel
.main-surface
.app-header-tint
.composer-surface-chrome .ProseMirror
[data-content-search-unit-key$=":user"]
[data-content-search-unit-key$=":assistant"]
[data-user-message-bubble="true"]
.app-shell-left-panel .absolute.bottom-0.z-20.inset-x-0
[aria-label="打开设置"]
```

测试结束后直接关闭这份独立浏览器配置即可移除运行时页面。`apply` 已更新本机保存主题；如需同时清理保存状态和 hook，再执行 `.\Codex-Skin.exe rollback`。
