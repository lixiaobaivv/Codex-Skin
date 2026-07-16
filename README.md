# Codex Theme Store

Codex 桌面主题切换工具，按四张示例图实现四套视觉主题，并通过 CSS/JS 动态重绘 Codex 前端。

- Store 版：以 `127.0.0.1:9229` 本机回环 CDP 启动官方 Codex，向当前页面动态注入主题。
- Patched 版：把 CSS/JS hook 写入可编辑的 webview，重启后自动加载。

工具不会修改 Store 版位于 `WindowsApps` 的签名安装包，Codex 更新后仍可重新检测新版本。

公开主题商店：<https://lixiaobaivv.github.io/Codex-Skin-Store/>
签名互操作样例：<https://github.com/lixiaobaivv/Codex-Skin/releases/tag/sample-v1>

商店页面由 GitHub Pages 托管，主题包由 GitHub Releases 托管；当前公开 MVP
不需要应用服务器、数据库或对象存储。

## 四套主题与生图素材

| 主题 ID | 名称 | 模式 | 当前 hero |
| --- | --- | --- | --- |
| `dilraba-star` | 热巴星球 | light | `backgrounds/dilraba-star-hero-v3.jpg` |
| `jackson-sage` | 千玺星球 | light | `backgrounds/jackson-sage-hero-v2.jpg` |
| `kun-stage` | KUN 舞台 | dark | `backgrounds/kun-stage-hero-v2.jpg` |
| `enfp-pop` | ENFP 小宇宙 | light | `backgrounds/enfp-pop-hero-v2.jpg` |

四张 hero 是按示例风格生成并筛选的 v2/v3 素材；本地图片会转为 data URL 注入，不依赖 `file://`，也不需要修改 `WindowsApps` 权限。`previews` 中的完整界面图只用于主题商店预览。

## 当前界面覆盖

运行时注入会根据当前主题动态创建或重绘：

- 主题侧栏，以及导航、项目、任务、进度和底部用户区；
- 顶部 masthead、主题徽章、hero 文案与标签；
- 四张快捷操作卡，点击后会把对应提示词写入输入框；
- 主背景、用户/助手对话气泡、底部输入框和占位文案。

因此背景和文字不只是预览图的一部分，而是由主题 CSS/JS 在真实页面中生成并随主题切换。

## 推荐用法

直接双击工作区根目录：

```text
Codex主题商店.exe
```

这是自包含版本，不需要安装 .NET。程序显示四张主题预览图，选择主题后可执行：

- `仅保存主题`：更新当前选择，不关闭 Codex；若 9229 上已有目标页，也会尝试实时注入。
- `应用并重启 Codex`：关闭 Store Codex，再以回环 CDP 主题模式打开并注入。
- `恢复默认`：清理保存主题和已安装 hook。

Store 版启动参数固定为：

```text
--remote-debugging-address=127.0.0.1
--remote-debugging-port=9229
```

注入器只接受 `127.0.0.1` 或 `localhost`、端口 9229 的 WebSocket 调试目标，不连接外部 CDP 地址。

## 连续切换主题

可以在同一个已打开页面中连续执行 `apply`。每次切换都会读取上一条 `Page.addScriptToEvaluateOnNewDocument` 标识，先通过 `Page.removeScriptToEvaluateOnNewDocument` 移除旧脚本，再注册并保存新脚本，同时替换当前页面元素。刷新页面后只会恢复最后选择的主题，不会叠加旧主题。

## 命令行

```powershell
.\Codex主题商店.exe list
.\Codex主题商店.exe status
.\Codex主题商店.exe apply dilraba-star
.\Codex主题商店.exe apply jackson-sage
.\Codex主题商店.exe apply kun-stage
.\Codex主题商店.exe apply enfp-pop
.\Codex主题商店.exe launch
.\Codex主题商店.exe rollback
```

`apply` 选择、保存主题并尝试注入当前 9229 目标。Store 版尚未以主题模式启动时，再执行 `launch` 完成重启和注入。

Store 版状态文件位于：

```text
%LOCALAPPDATA%\CodexThemeStore
```

## `.dreamskin` 签名包导入

Windows 客户端已开始接入 Codex-Skin-Store 的 MVP v1 包契约。当前阶段支持由受信任
Ed25519 密钥签名的本地 `.dreamskin` 文件；导入只完成验证和安装，默认不会自动应用：

```powershell
.\Codex主题商店.exe import C:\Downloads\theme.dreamskin
.\Codex主题商店.exe import C:\Downloads\theme.dreamskin --apply
```

安装目录为：

```text
%LOCALAPPDATA%\CodexThemeStore\themes\packages\<theme-id>\<version>\
```

仓库中的 `samples/dreamskin` 是跨平台协议测试夹具，可通过
`node tools/dreamskin/build-sample.mjs` 重建。其私钥是公开的开发测试密钥，绝不能用于
生产发布。

签名互操作样例的发布检查、不可变 Release 规则和商店解锁步骤见
[`docs/publish-signed-sample.md`](docs/publish-signed-sample.md)。在 Release 下载地址返回
HTTP 200 之前，商店中的 `published` 必须保持为 `false`。

为当前用户注册网页一键导入协议和文件关联：

```powershell
.\Codex主题商店.exe protocol register
.\Codex主题商店.exe protocol status
.\Codex主题商店.exe protocol unregister
```

`dreamskin://install` 会先显示来源、主题提示、版本和大小，用户确认后才进行受限 HTTPS
下载；安装成功后会再次询问是否应用。下载器拒绝私网/本机地址、非 443 端口、HTTPS
降级、超过三次的重定向、大小不符和整包哈希不符。

## 回滚

```powershell
.\Codex主题商店.exe rollback
```

它会删除 Store 版保存主题、移除当前调试页的运行时样式，并清理 Patched 版 HTML hook。Store 版未开启调试端口时，正常关闭再打开也会回到原始界面。

## 自定义主题

复制 `themes/*.json` 后修改颜色、字体、背景和文案。`theme.backgroundImage` 可指向本地图片，`copy.replaceText` 和 `copy.replacePlaceholders` 用于替换导航与输入框文本；首页 masthead、标签、快捷卡和侧栏内容由相应主题配置生成。

本地 CDP 夹具、视觉检查和自动截图方法见 `qa/README.md`。
