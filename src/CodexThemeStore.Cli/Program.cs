using CodexThemeStore.Core;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
    {
        PrintUsage();
        return 0;
    }

    var command = args[0].ToLowerInvariant();
    ICodexPlatformAdapter adapter = new MacOsCodexAdapter();
    var requiresMacOs = command is "status" or "launch" or "restart" or "restart-theme";
    if (requiresMacOs && !adapter.IsSupported)
    {
        Console.Error.WriteLine($"命令 {command} 需要 macOS Codex 适配器；主题同步、编译和已有 CDP 页面的注入/回滚可在其他系统运行。");
        return 2;
    }

    try
    {
        var stateDirectory = args.Length > 1 ? Path.GetFullPath(args[1]) : DefaultStateDirectory();
        switch (command)
        {
            case "theme-validate":
            {
                var root = args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();
                var count = ThemeRepositoryClient.ValidateDirectory(root);
                Console.WriteLine($"主题仓库验证通过：{count} 个主题。");
                return 0;
            }
            case "theme-index":
            {
                var root = args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();
                var name = args.Length > 2 ? args[2] : null;
                var count = new ThemeRepositoryAuthoring().GenerateIndex(root, name);
                Console.WriteLine($"已生成 theme-repository.json：{count} 个主题。");
                return 0;
            }
            case "theme-pack":
            {
                var root = args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();
                var destination = args.Length > 2
                    ? Path.GetFullPath(args[2])
                    : Path.Combine(Path.GetDirectoryName(root)!, $"{Path.GetFileName(root)}-theme-catalog-v1.zip");
                var package = new ThemeRepositoryAuthoring().Package(root, destination);
                Console.WriteLine($"主题仓库已打包：{package.ThemeCount} 个主题，{package.Bytes} 字节。");
                Console.WriteLine($"输出：{package.Path}");
                Console.WriteLine($"SHA-256：{package.Sha256}");
                return 0;
            }
            case "refresh":
            {
                var settings = ThemeRepositoryClient.LoadSettings();
                if (args.Length > 1)
                {
                    var source = ThemeRepositoryClient.Sources.FirstOrDefault(item => item.Id.Equals(args[1], StringComparison.OrdinalIgnoreCase))
                                 ?? throw new ArgumentException($"未知加速源: {args[1]}");
                    settings = settings with { SourceId = source.Id };
                }
                var result = await new ThemeRepositoryClient().SyncAsync(settings);
                Console.WriteLine($"已通过 {result.SourceName} 更新 {result.ThemeCount} 个主题。");
                return 0;
            }
            case "list":
            {
                foreach (var theme in LoadThemes())
                {
                    Console.WriteLine($"{theme.CodeThemeId}\t{theme.DisplayName}\t{theme.Category}\t{theme.Version}");
                }
                return 0;
            }
            case "status":
            {
                var installation = await adapter.DiscoverAsync();
                if (installation is null)
                {
                    Console.WriteLine("Codex: not installed");
                    return 1;
                }
                Console.WriteLine($"Codex: {installation.AppPath}");
                Console.WriteLine($"Running: {await adapter.IsRunningAsync(installation)}");
                Console.WriteLine($"CDP: {(await new CdpThemeInjector().IsReadyAsync() ? "ready" : "not available")}");
                return 0;
            }
            case "launch":
            {
                var installation = await RequireInstallationAsync(adapter);
                await adapter.StartAsync(installation, enableCdp: true);
                Console.WriteLine("Codex 已通过本机 CDP 模式启动。");
                return 0;
            }
            case "apply":
            {
                var applied = await new CdpThemeInjector().InjectAsync(ThemeInjectionPayload.Load(stateDirectory), TimeSpan.FromSeconds(15));
                if (applied == 0) throw new InvalidOperationException("没有找到可注入的 Codex 页面，请先启动带回环 CDP 的 Codex。");
                Console.WriteLine("主题已通过 CDP 应用。");
                return 0;
            }
            case "compile":
            {
                if (args.Length < 2) throw new ArgumentException("compile 需要主题 JSON 路径。");
                var outputDirectory = args.Length > 2 ? Path.GetFullPath(args[2]) : DefaultStateDirectory();
                var payload = new ThemeStateCompiler().Compile(ResolveThemePath(args[1]), outputDirectory);
                Console.WriteLine($"主题 {payload.ThemeId} 已编译到 {outputDirectory}");
                return 0;
            }
            case "apply-theme":
            {
                if (args.Length < 2) throw new ArgumentException("apply-theme 需要主题 JSON 路径。");
                var outputDirectory = args.Length > 2 ? Path.GetFullPath(args[2]) : DefaultStateDirectory();
                var payload = new ThemeStateCompiler().Compile(ResolveThemePath(args[1]), outputDirectory);
                if (await new CdpThemeInjector().InjectAsync(payload, TimeSpan.FromSeconds(15)) == 0)
                    throw new InvalidOperationException("没有找到可注入的 Codex 页面，请先启动带回环 CDP 的 Codex。");
                Console.WriteLine($"主题 {payload.ThemeId} 已编译并应用。");
                return 0;
            }
            case "restart":
            {
                var installation = await new CodexThemeRuntime().RestartAndInjectAsync(
                    adapter,
                    ThemeInjectionPayload.Load(stateDirectory),
                    TimeSpan.FromSeconds(90));
                Console.WriteLine($"Codex 已重启并完成主题注入: {installation.AppPath}");
                return 0;
            }
            case "restart-theme":
            {
                if (args.Length < 2) throw new ArgumentException("restart-theme 需要主题 JSON 路径。");
                var outputDirectory = args.Length > 2 ? Path.GetFullPath(args[2]) : DefaultStateDirectory();
                var payload = new ThemeStateCompiler().Compile(ResolveThemePath(args[1]), outputDirectory);
                var installation = await new CodexThemeRuntime().RestartAndInjectAsync(adapter, payload, TimeSpan.FromSeconds(90));
                Console.WriteLine($"主题 {payload.ThemeId} 已编译，Codex 已重启并完成注入: {installation.AppPath}");
                return 0;
            }
            case "rollback":
            {
                var removed = await new CdpThemeInjector().RemoveAsync(TimeSpan.FromSeconds(10)) > 0;
                DeleteSavedTheme(stateDirectory);
                Console.WriteLine(removed ? "已移除当前主题并清理保存状态。" : "已清理保存状态；当前没有可连接的主题页面。");
                return 0;
            }
            default:
                Console.Error.WriteLine($"未知命令: {command}");
                PrintUsage();
                return 2;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task<CodexInstallation> RequireInstallationAsync(ICodexPlatformAdapter adapter) =>
    await adapter.DiscoverAsync() ?? throw new FileNotFoundException("未找到 Codex.app，可通过 CODEX_APP_PATH 指定位置。");

static string DefaultStateDirectory() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CodexThemeStore");

static string ResolveThemePath(string input)
{
    if (File.Exists(input)) return Path.GetFullPath(input);
    var candidate = LoadThemes().FirstOrDefault(theme => theme.CodeThemeId.Equals(input, StringComparison.Ordinal));
    if (candidate is not null) return candidate.SourcePath;
    throw new FileNotFoundException($"未找到主题: {input}");
}

static IReadOnlyList<ThemeDefinition> LoadThemes() => ThemeCatalog.Load(
    new[]
    {
        ThemeRepositoryClient.CacheThemeDirectory,
        Path.Combine(AppContext.BaseDirectory, "themes"),
        Path.Combine(Directory.GetCurrentDirectory(), "themes"),
    },
    platform: OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "macos");

static void DeleteSavedTheme(string stateDirectory)
{
    foreach (var name in new[] { "codex-theme.css", "codex-theme.js", "current-theme.json" })
    {
        var path = Path.Combine(stateDirectory, name);
        if (File.Exists(path)) File.Delete(path);
    }
}

static void PrintUsage()
{
    Console.WriteLine("Codex-Skin CLI");
    Console.WriteLine("  codex-theme-store theme-validate [repository-directory]");
    Console.WriteLine("  codex-theme-store theme-index [repository-directory] [repository-name]");
    Console.WriteLine("  codex-theme-store theme-pack [repository-directory] [output.zip]");
    Console.WriteLine("  codex-theme-store refresh [github|gh-proxy|ghfast]");
    Console.WriteLine("  codex-theme-store list");
    Console.WriteLine("  codex-theme-store status");
    Console.WriteLine("  codex-theme-store launch");
    Console.WriteLine("  codex-theme-store compile <theme-id|theme.json> [state-directory]");
    Console.WriteLine("  codex-theme-store apply [state-directory]");
    Console.WriteLine("  codex-theme-store apply-theme <theme-id|theme.json> [state-directory]");
    Console.WriteLine("  codex-theme-store restart [state-directory]");
    Console.WriteLine("  codex-theme-store restart-theme <theme-id|theme.json> [state-directory]");
    Console.WriteLine("  codex-theme-store rollback [state-directory]");
    Console.WriteLine();
    Console.WriteLine("默认状态目录为系统 LocalApplicationData/CodexThemeStore。");
    Console.WriteLine("非标准安装位置可设置 CODEX_APP_PATH=/Applications/Codex.app。");
}
