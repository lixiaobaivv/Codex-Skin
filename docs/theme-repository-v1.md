# Codex Skin 主题仓库标准 v1

主题仓库必须在根目录提供 `theme-repository.json` 和 `schemas/` 下的两个 v1 Schema。每个主题清单放在 `themes/<id>.json`，图片只能放在 `backgrounds/`、`logos/`、`pets/` 或 `previews/`。完整字段约束以 `schemas/theme-repository-v1.schema.json` 和 `schemas/theme-v1.schema.json` 为准，制作与发布流程见 [`theme-authoring.md`](theme-authoring.md)。

## 可修改区域

- Codex 主背景、窗口表面、对话气泡、输入框和固定导航元素的颜色、字体与边框。
- 空白首页的品牌横幅、标签和快捷引导。快捷引导只能向真实输入框填入提示词。
- 空白首页 hero 右下角的可选宠物图片；主题可替换或移除宠物，但不能提供宠物脚本。
- 固定导航中的“新建任务”“已安排”“插件”“设置”显示文案。

## 不可修改区域

- 用户项目、任务、对话和账号数据，包括名称、数量、排序、状态和点击行为。
- 项目与任务分组标题。
- 任意远程 JavaScript、HTML、可执行文件、动态链接库或系统命令。
- GitHub 仓库以外的网络资源。所有运行时图片必须随主题仓库下载并通过相对路径引用。

客户端必须忽略旧主题中的 `sidebarProject`、`sidebarItems`、`tasks`、`profileLabel` 和任意页面文本替换，并在安装 v1 主题时拒绝未知字段。

## 示例

```json
{
  "$schema": "../schemas/theme-v1.schema.json",
  "schemaVersion": 1,
  "version": "1.0.0",
  "displayName": "示例主题",
  "codeThemeId": "example-theme",
  "category": "极简",
  "variant": "light",
  "previewImage": "../previews/example-theme.png",
  "theme": {
    "accent": "#2F6FED",
    "ink": "#202124",
    "surface": "#F7F8FA",
    "backgroundImage": "../backgrounds/example-theme.jpg"
  },
  "home": {
    "brand": "Example",
    "title": "今天构建什么？",
    "pet": {
      "image": "../pets/example-pet.png",
      "alt": "示例宠物",
      "size": 112
    },
    "quickActions": [
      { "title": "理解代码", "prompt": "请概览当前代码库。" },
      { "title": "构建功能", "prompt": "请实现一个新功能。" },
      { "title": "审查代码", "prompt": "请审查当前代码。" },
      { "title": "修复问题", "prompt": "请诊断并修复当前问题。" }
    ]
  }
}
```

## 发布规则

1. 主题 ID 一经发布不得复用；升级使用语义化 `version`。
2. 单个文件不超过 20 MB，仓库归档不超过 200 MB，最多 2,000 个文件。
3. 客户端先下载到临时目录，验证清单、路径和资源后再原子替换本地缓存。
4. 同步失败时保留上一份可用缓存；首次运行无缓存时使用应用自带主题。

## 标准命令

```bash
dotnet run --project src/CodexThemeStore.Cli -- theme-index . "My Theme Repository"
dotnet run --project src/CodexThemeStore.Cli -- theme-validate .
dotnet run --project src/CodexThemeStore.Cli -- theme-pack . artifacts/theme-catalog-v1.zip
```

索引必须覆盖 `themes/` 中全部且仅有的主题清单。CI 和客户端使用同一个 Core 校验器，不接受“Schema 通过但资源缺失”的仓库。
