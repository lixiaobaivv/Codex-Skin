using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace CodexThemeStore.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (ApplicationLifetime is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, args) =>
                {
                    var value = args switch
                    {
                        ProtocolActivatedEventArgs protocol => protocol.Uri.AbsoluteUri,
                        FileActivatedEventArgs file => file.Files.FirstOrDefault()?.Path.LocalPath,
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(value))
                        Dispatcher.UIThread.Post(() => _ = window.HandleExternalImportAsync(value));
                };
            }
            foreach (var value in desktop.Args ?? [])
            {
                if (value.StartsWith("dreamskin:", StringComparison.OrdinalIgnoreCase) ||
                    value.EndsWith(".dreamskin", StringComparison.OrdinalIgnoreCase))
                    Dispatcher.UIThread.Post(() => _ = window.HandleExternalImportAsync(value));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
