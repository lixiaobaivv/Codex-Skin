# Codex-Skin

**Codex-Skin** 是 Codex Desktop 的开源主题客户端，同时也是 Codex-Skin-Store 签名主题包的本机导入器。它在用户本机完成主题选择、校验、安装、应用和恢复，不需要应用服务器、数据库或在线账号系统。

- 主题商店：<https://lixiaobaivv.github.io/Codex-Skin-Store/>
- 客户端下载：<https://github.com/lixiaobaivv/Codex-Skin/releases/latest>
- 签名主题样例：<https://github.com/lixiaobaivv/Codex-Skin/releases/tag/catalog-v1>
- 导入兼容说明：[docs/dreamskin-compatibility.md](docs/dreamskin-compatibility.md)

> [!IMPORTANT]
> Codex-Skin 是社区开源项目，不是 OpenAI 或 Codex 官方产品。程序不会修改 Microsoft Store 中 Codex 的签名安装包，也不会读取 Codex 凭证、API Key、项目内容或聊天数据。

## 下载

正式客户端版本使用 `v<SemVer>` 标签发布，例如 `v0.1.0`。Windows Release 提供以下英文文件名：

| 文件 | 用途 |
| --- | --- |
| `Codex-Skin-win-x64.exe` | Windows x64 自包含单文件客户端，可直接运行 |
| `Codex-Skin-win-x64.zip` | 相同客户端的 ZIP 包 |
| `SHA256SUMS.txt` | Release 文件的 SHA-256 校验值 |

Windows EXE 自带 .NET 运行时，不要求用户预先安装 .NET SDK 或 Runtime。下载后建议先对照 `SHA256SUMS.txt` 校验文件，再运行程序。

主题包 Release（如 `sample-v1`、`catalog-v1`）只包含 `.dreamskin` 测试或目录主题，不等同于客户端版本。需要客户端 EXE 时请进入最新的 `v*` Release。

## 平台状态

| 平台 | 状态 | 计划产物 |
| --- | --- | --- |
| Windows x64 | 已实现并纳入 CI/Release 自动构建 | `Codex-Skin-win-x64.exe` |
| macOS Apple Silicon | 导入器兼容代码已准备，但当前仓库尚未包含可独立构建的 macOS 客户端源码，也未完成真实 macOS runner/设备验收 | 计划提供签名、可验证的 `.app` 压缩包或 `.dmg` |
| macOS Intel | 尚未确定是否支持 | 根据实际用户需求和构建验证决定 |

不会用空壳脚本或伪造文件冒充 macOS 可执行程序。macOS 正式发布需要先完成：

1. 将可构建的 macOS 客户端源码纳入受版本控制的仓库；
2. 在 `macos-latest` runner 上完成 `.dreamskin` 下载、签名验证、安装和 URL handler 测试；
3. 生成 `.app`，完成代码签名与公证策略；
4. 为产物生成 SHA-256，并由同一个 `v*` Release 工作流上传。

## Windows 快速开始

1. 从 [Releases](https://github.com/lixiaobaivv/Codex-Skin/releases/latest) 下载 `Codex-Skin-win-x64.exe`；
2. 双击启动客户端；
3. 选择内置主题，可以仅保存，也可以应用并重新启动 Codex；
4. 如需从网页一键导入，以当前用户身份注册 `dreamskin://` 协议。

```powershell
.\Codex-Skin-win-x64.exe protocol register
.\Codex-Skin-win-x64.exe protocol status
```

解除关联：

```powershell
.\Codex-Skin-win-x64.exe protocol unregister
```

## 主题能力

仓库内置四套本机主题：

| 主题 ID | 名称 | 模式 |
| --- | --- | --- |
| `dilraba-star` | 热巴星球 | light |
| `jackson-sage` | 千玺星球 | light |
| `kun-stage` | KUN 舞台 | dark |
| `enfp-pop` | ENFP 小宇宙 | light |

客户端支持两种应用方式：

- **Store 版**：使用 `127.0.0.1:9229` 本机回环 CDP 启动官方 Codex，并向当前页面注入主题；
- **Patched 版**：在用户明确使用可编辑 webview 时写入主题 hook，重启后自动加载。

Store 版不会修改 `WindowsApps` 中的签名安装包。Codex 更新后，客户端可以重新检测新的安装位置。

## `.dreamskin` 导入

`.dreamskin` 是带有严格清单、资源摘要和 Ed25519 签名的主题包。安装与立即应用是两个独立确认动作。

导入本地文件：

```powershell
.\Codex-Skin-win-x64.exe import C:\Downloads\theme.dreamskin
.\Codex-Skin-win-x64.exe import C:\Downloads\theme.dreamskin --apply
```

客户端会依次执行：

1. 限制包大小和 ZIP 条目；
2. 拒绝路径穿越、符号链接和额外文件；
3. 校验封闭 Schema；
4. 校验 Ed25519 签名；
5. 校验资源大小、SHA-256、媒体类型和像素尺寸；
6. 检查主题 ID、版本、平台和客户端兼容范围；
7. 原子安装到本机主题库；
8. 安装完成后再次询问是否立即应用。

默认安装目录：

```text
%LOCALAPPDATA%\CodexThemeStore\themes\packages\<theme-id>\<version>\
```

## `dreamskin://` 一键导入

注册协议后，Codex-Skin-Store 可以通过以下形式唤起客户端：

```text
dreamskin://install?url=<https-url>&sha256=<sha256>&size=<bytes>&id=<theme-id>&version=<semver>
```

客户端会先展示来源域名、主题提示、版本和大小，并要求确认。下载器只接受公开 HTTPS 地址，拒绝本机或私网地址、非 443 端口、HTTPS 降级、过多重定向、大小不符和整包哈希不符。

## 命令行

```powershell
.\Codex-Skin-win-x64.exe list
.\Codex-Skin-win-x64.exe status
.\Codex-Skin-win-x64.exe apply dilraba-star
.\Codex-Skin-win-x64.exe apply jackson-sage
.\Codex-Skin-win-x64.exe launch
.\Codex-Skin-win-x64.exe rollback
.\Codex-Skin-win-x64.exe import C:\Downloads\theme.dreamskin
.\Codex-Skin-win-x64.exe protocol register
.\Codex-Skin-win-x64.exe protocol status
.\Codex-Skin-win-x64.exe protocol unregister
```

`apply` 保存主题并尝试注入当前回环调试页；`launch` 以主题模式重新启动 Store 版 Codex；`rollback` 清除已保存主题和运行时注入并恢复默认界面。

## 从源码构建

要求：

- Windows 10/11；
- .NET 8 SDK；
- Node.js 22（用于签名主题夹具测试）。

普通构建：

```powershell
dotnet build src\CodexThemeStore\CodexThemeStore.csproj --configuration Release
```

自包含 Windows 发布：

```powershell
dotnet publish src\CodexThemeStore\CodexThemeStore.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts\Codex-Skin-win-x64
```

运行签名包测试：

```powershell
node --test tests\*.test.mjs
```

## 自动发布

工作流 [`.github/workflows/release-client.yml`](.github/workflows/release-client.yml) 负责客户端版本：

1. 验证全部签名主题夹具；
2. 构建 Windows x64 自包含单文件程序；
3. 将 EXE 单独复制到干净目录并运行协议自测，确保它不是依赖工作区文件的空壳；
4. 生成 ZIP 和 `SHA256SUMS.txt`；
5. 创建或验证不可变的 `v*` GitHub Release。

推送 SemVer 标签即可发布：

```powershell
git tag v0.1.0
git push origin v0.1.0
```

也可以在 GitHub Actions 中手动运行 **Publish Codex-Skin client** 并输入版本标签。

`sample-v1` 和 `catalog-v1` 由独立的主题包发布流程维护，客户端版本与主题数据版本不会混用。

## 项目结构

```text
.github/workflows/       CI、客户端 Release 和主题样例发布
backgrounds/             内置主题背景
logos/                   内置主题 UI 标志
previews/                主题预览图
samples/dreamskin/       确定性的签名互操作夹具
src/CodexThemeStore/     Windows 客户端与导入器
tests/                   签名包结构与密码学验证测试
themes/                  内置主题配置
tools/dreamskin/         签名主题包生成器
patches/                 外部项目兼容补丁快照
qa/                      本地视觉与 CDP 验证工具
```

## 安全边界

- CDP 只允许 `127.0.0.1` 或 `localhost` 的固定端口；
- 不修改 Microsoft Store 的 Codex 签名安装包；
- 不接收主题脚本、HTML、CSS、SVG、WASM 或可执行载荷；
- 外部 SHA-256 与包内 Ed25519 签名分别校验传输完整性和来源；
- 当前公开样例密钥仅用于互操作测试，不能代表生产发布者身份；
- 生产主题需要独立的发布者信任、密钥轮换和撤回机制。

## 路线图

- [x] Windows 主题选择、应用、恢复和回环 CDP 注入；
- [x] `.dreamskin` 本地文件导入；
- [x] `dreamskin://install`、安全下载和文件关联；
- [x] Windows x64 GitHub Release 自动构建；
- [ ] 将 macOS 客户端源码正式接入仓库；
- [ ] macOS Apple Silicon runner 与设备验收；
- [ ] macOS 代码签名、公证和 Release 产物；
- [ ] 生产发布者密钥、轮换和撤回列表；
- [ ] 自动更新和稳定版发布通道。

## 许可证

代码和构建脚本按仓库许可证分发。主题作品及第三方素材仍受各自许可证约束，发布者必须单独声明来源和再分发权限。
