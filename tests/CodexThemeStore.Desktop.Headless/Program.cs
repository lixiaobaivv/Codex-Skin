using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CodexThemeStore.Desktop;

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "codex-theme-store-desktop.png");
var width = args.Length > 1 ? double.Parse(args[1]) : 1120;
var height = args.Length > 2 ? double.Parse(args[2]) : 780;
Environment.SetEnvironmentVariable("CODEX_THEME_STORE_SKIP_AUTO_REFRESH", "1");
AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .SetupWithoutStarting();

var window = new MainWindow();
window.Width = width;
window.Height = height;
window.Show();
Dispatcher.UIThread.RunJobs();
var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Headless renderer did not produce a frame.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
frame.Save(output);
window.Close();

if (new FileInfo(output).Length < 10_000) throw new InvalidOperationException("Rendered screenshot is unexpectedly small.");
Console.WriteLine(output);
