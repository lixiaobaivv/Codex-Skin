using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CodexThemeStore.Core;

namespace CodexThemeStore.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<ThemeCardModel> _visibleThemes = [];
    private readonly List<ThemeCardModel> _allThemes = [];
    private readonly ICodexPlatformAdapter _adapter = new MacOsCodexAdapter();
    private readonly SemaphoreSlim _importGate = new(1, 1);
    private string _selectedCategory = "全部";
    private ThemeCardModel? _selected;
    private string? _lastImportValue;
    private DateTimeOffset _lastImportAt;

    public MainWindow()
    {
        InitializeComponent();
        ThemeList.ItemsSource = _visibleThemes;
        SourceCombo.ItemsSource = ThemeRepositoryClient.Sources;
        var settings = ThemeRepositoryClient.LoadSettings();
        SourceCombo.SelectedItem = ThemeRepositoryClient.Sources.FirstOrDefault(item => item.Id == settings.SourceId) ?? ThemeRepositoryClient.Sources[0];

        foreach (var button in CategoryBar.Children.OfType<Button>())
        {
            button.Click += (_, _) => SelectCategory(button.Tag as string ?? "全部");
        }
        SelectCategory("全部", applyFilter: false);
        ThemeList.SelectionChanged += (_, _) => SelectTheme(ThemeList.SelectedItem as ThemeCardModel);
        RefreshButton.Click += async (_, _) => await RefreshAsync(false);
        ApplyButton.Click += async (_, _) => await ApplyAsync(false);
        RestartButton.Click += async (_, _) => await ApplyAsync(true);
        RollbackButton.Click += async (_, _) => await RollbackAsync();
        Opened += async (_, _) =>
        {
            LoadLocalThemes();
            if (Environment.GetEnvironmentVariable("CODEX_THEME_STORE_SKIP_AUTO_REFRESH") != "1")
                await RefreshAsync(true);
        };
        Closed += (_, _) => DisposeThemes();
    }

    private void LoadLocalThemes()
    {
        var selectedId = _selected?.Id;
        DisposeThemes();
        _allThemes.Clear();
        foreach (var theme in ThemeCatalog.Load(ThemeDirectories(), platform: "macos", allowEmpty: true))
        {
            _allThemes.Add(new ThemeCardModel(
                theme.CodeThemeId,
                theme.DisplayName,
                theme.Description,
                theme.Category,
                theme.SourcePath,
                new Bitmap(theme.ResolvePreviewImage())));
        }
        CatalogLabel.Text = $"{_allThemes.Count} 个主题";
        ApplyFilter();
        SelectTheme(_allThemes.FirstOrDefault(theme => theme.Id == selectedId) ?? _visibleThemes.FirstOrDefault());
        if (_allThemes.Count == 0) StatusLabel.Text = "首次使用需要联网同步主题，点击刷新会自动选择线路";
    }

    private static IEnumerable<string> ThemeDirectories()
    {
        yield return ThemeRepositoryClient.CacheThemeDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "themes");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "themes");
    }

    private void ApplyFilter()
    {
        var category = _selectedCategory;
        var previousSelection = _selected;
        _visibleThemes.Clear();
        foreach (var theme in _allThemes.Where(theme => category == "全部" || theme.Category == category))
            _visibleThemes.Add(theme);
        SelectTheme(previousSelection is not null && _visibleThemes.Contains(previousSelection)
            ? previousSelection
            : _visibleThemes.FirstOrDefault());
        CatalogLabel.Text = category == "全部" ? $"{_allThemes.Count} 个主题" : $"{_visibleThemes.Count} / {_allThemes.Count} 个主题";
    }

    private void SelectCategory(string category, bool applyFilter = true)
    {
        _selectedCategory = category;
        foreach (var button in CategoryBar.Children.OfType<Button>())
            button.Classes.Set("selected", string.Equals(button.Tag as string, category, StringComparison.Ordinal));
        if (applyFilter) ApplyFilter();
    }

    private void SelectTheme(ThemeCardModel? theme)
    {
        foreach (var item in _allThemes) item.IsSelected = ReferenceEquals(item, theme);
        _selected = theme;
        ThemeList.SelectedItem = theme;
        SelectionLabel.Text = theme is null ? "请选择主题" : $"已选择：{theme.Name}";
        SetCommandAvailability(!BusyBar.IsVisible);
    }

    private async Task RefreshAsync(bool silent)
    {
        var source = SourceCombo.SelectedItem as ThemeRepositorySource ?? ThemeRepositoryClient.Sources[0];
        var settings = ThemeRepositoryClient.LoadSettings() with { SourceId = source.Id };
        SetBusy(true, $"正在从 {source.Name} 开始同步...");
        try
        {
            var result = await new ThemeRepositoryClient().SyncAsync(settings);
            SourceCombo.SelectedItem = ThemeRepositoryClient.Sources.First(item => item.Id == result.SourceId);
            LoadLocalThemes();
            StatusLabel.Text = $"已自动通过 {result.SourceName} 更新 {result.ThemeCount} 个主题";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = silent && _allThemes.Count > 0 ? "所有线路同步失败，已使用本地缓存" : "GitHub 和镜像线路均同步失败";
            if (!silent) await ShowErrorAsync(ex.Message);
        }
        finally
        {
            SetBusy(false, StatusLabel.Text ?? "准备就绪");
        }
    }

    private async Task ApplyAsync(bool restart)
    {
        if (_selected is null) return;
        var restartConfirmed = false;
        if (!restart && !await new CdpThemeInjector().IsReadyAsync())
        {
            if (!await ConfirmAsync("当前 Codex 没有启用本机主题端口，需要关闭并重新启动 Codex 后应用主题。是否继续？")) return;
            restart = true;
            restartConfirmed = true;
        }
        if (restart && !restartConfirmed && !await ConfirmAsync("应用主题会关闭当前 Codex，然后以主题模式重新打开。")) return;
        SetBusy(true, restart ? "正在重启 Codex..." : "正在应用主题...");
        try
        {
            var payload = new ThemeStateCompiler().Compile(_selected.SourcePath, StateDirectory());
            if (restart)
                await new CodexThemeRuntime().RestartAndInjectAsync(_adapter, payload, TimeSpan.FromSeconds(90));
            else if (!await _adapter.InjectAsync(payload, TimeSpan.FromSeconds(15)))
                throw new InvalidOperationException("Codex 未以主题模式启动，请使用“应用并重启 Codex”。");
            StatusLabel.Text = $"{_selected.Name} 已应用";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "主题应用失败";
            await ShowErrorAsync(ex.Message);
        }
        finally
        {
            SetBusy(false, StatusLabel.Text ?? "准备就绪");
        }
    }

    private async Task RollbackAsync()
    {
        if (!await ConfirmAsync("恢复 Codex 默认主题并清除已保存主题？")) return;
        SetBusy(true, "正在恢复默认主题...");
        try
        {
            await _adapter.RollbackAsync(TimeSpan.FromSeconds(10));
            foreach (var file in new[] { "codex-theme.css", "codex-theme.js", "current-theme.json" })
            {
                var path = Path.Combine(StateDirectory(), file);
                if (File.Exists(path)) File.Delete(path);
            }
            StatusLabel.Text = "已恢复默认主题";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "恢复失败";
            await ShowErrorAsync(ex.Message);
        }
        finally
        {
            SetBusy(false, StatusLabel.Text ?? "准备就绪");
        }
    }

    public async Task HandleExternalImportAsync(string value)
    {
        await _importGate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (value.Equals(_lastImportValue, StringComparison.Ordinal) && now - _lastImportAt < TimeSpan.FromSeconds(3))
                return;
            _lastImportValue = value;
            _lastImportAt = now;

            if (!IsVisible) Show();
            Activate();

            DreamSkinImportResult imported;
            if (value.StartsWith("dreamskin:", StringComparison.OrdinalIgnoreCase))
            {
                var request = DreamSkinProtocol.Parse(value);
                if (!await ConfirmAsync(
                        $"从 {request.PackageUri.Host} 下载主题？\n\n" +
                        $"主题：{request.Id ?? "未提供"}\n版本：{request.Version ?? "未提供"}\n大小：{request.Size:N0} 字节\n\n" +
                        "客户端会校验 SHA-256、Ed25519 签名和全部图片后再安装，不会自动应用。")) return;
                SetBusy(true, "正在准备下载主题...");
                BusyBar.IsIndeterminate = false;
                BusyBar.Value = 0;
                var reporter = new Progress<DreamSkinDownloadProgress>(UpdateDownloadProgress);
                var preferredSource = (SourceCombo.SelectedItem as ThemeRepositorySource)?.Id
                                      ?? ThemeRepositoryClient.LoadSettings().SourceId;
                imported = await DreamSkinDownloadService.DownloadAndImportAsync(
                    request,
                    CancellationToken.None,
                    preferredSource,
                    reporter);
            }
            else
            {
                var path = Uri.TryCreate(value, UriKind.Absolute, out var fileUri) && fileUri.IsFile
                    ? fileUri.LocalPath
                    : value;
                if (!path.EndsWith(".dreamskin", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("只支持打开 .dreamskin 主题包。");
                if (!await ConfirmAsync($"验证并安装本地主题包？\n\n{Path.GetFileName(path)}\n\n安装后不会自动应用。")) return;
                SetBusy(true, "正在验证主题签名...");
                imported = await Task.Run(() => DreamSkinPackageInstaller.ImportLocal(path, platform: "macos"));
            }

            StatusLabel.Text = $"{imported.DisplayName} 已安全安装";
            LoadLocalThemes();
            if (!await ConfirmAsync($"{imported.DisplayName} 已通过签名验证并安装。\n\n立即重启 Codex 并应用该主题？")) return;
            SetBusy(true, "正在重启 Codex 并应用主题...");
            var selected = ThemeCatalog.Load(ThemeDirectories(), platform: "macos")
                .FirstOrDefault(theme => theme.CodeThemeId.Equals(imported.Id, StringComparison.Ordinal) &&
                                         ThemeCatalog.CompareVersions(theme.Version, imported.Version) >= 0);
            var payload = new ThemeStateCompiler().Compile(selected?.SourcePath ?? imported.ManifestPath, StateDirectory());
            await new CodexThemeRuntime().RestartAndInjectAsync(_adapter, payload, TimeSpan.FromSeconds(90));
            StatusLabel.Text = $"{imported.DisplayName} 已应用";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "主题导入失败";
            await ShowErrorAsync(ex.Message);
        }
        finally
        {
            SetBusy(false, StatusLabel.Text ?? "准备就绪");
            _importGate.Release();
        }
    }

    private void SetBusy(bool busy, string status)
    {
        BusyBar.IsVisible = busy;
        StatusLabel.Text = status;
        foreach (var button in CategoryBar.Children.OfType<Button>()) button.IsEnabled = !busy;
        SourceCombo.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        ThemeList.IsEnabled = !busy;
        SetCommandAvailability(!busy);
        if (!busy)
        {
            BusyBar.IsIndeterminate = true;
            BusyBar.Value = 0;
        }
    }

    private void UpdateDownloadProgress(DreamSkinDownloadProgress progress)
    {
        var percent = progress.TotalBytes <= 0
            ? 0
            : (int)Math.Clamp(progress.BytesReceived * 100 / progress.TotalBytes, 0, 100);
        BusyBar.IsIndeterminate = false;
        BusyBar.Value = percent;
        StatusLabel.Text = progress.Stage == "正在下载"
            ? $"正在通过 {progress.SourceName} 下载主题：{percent}%"
            : $"{progress.Stage}（{progress.SourceName}）";
    }

    private void SetCommandAvailability(bool enabled)
    {
        ApplyButton.IsEnabled = enabled && _selected is not null && _adapter.IsSupported;
        RestartButton.IsEnabled = enabled && _selected is not null && _adapter.IsSupported;
        RollbackButton.IsEnabled = enabled && _adapter.IsSupported;
    }

    private async Task ShowErrorAsync(string message)
    {
        var close = new Button { Content = "关闭", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, MinWidth = 80 };
        var dialog = new Window
        {
            Title = "Codex-Skin",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(22),
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 396 },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var result = false;
        var cancel = new Button { Content = "取消", MinWidth = 80 };
        var confirm = new Button { Content = "继续", MinWidth = 80, Background = new SolidColorBrush(Color.Parse("#D96F4D")), Foreground = Brushes.White };
        var dialog = new Window
        {
            Title = "Codex-Skin",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(22),
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 396 },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, confirm },
                    },
                },
            },
        };
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) => { result = true; dialog.Close(); };
        await dialog.ShowDialog(this);
        return result;
    }

    private static string StateDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexThemeStore");

    private void DisposeThemes()
    {
        foreach (var theme in _allThemes) theme.Dispose();
        _visibleThemes.Clear();
    }
}

public sealed class ThemeCardModel : INotifyPropertyChanged, IDisposable
{
    private bool _isSelected;

    public ThemeCardModel(string id, string name, string description, string category, string sourcePath, Bitmap preview)
    {
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        SourcePath = sourcePath;
        Preview = preview;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string SourcePath { get; }
    public Bitmap Preview { get; }
    public IBrush BorderBrush => IsSelected ? new SolidColorBrush(Color.Parse("#D96F4D")) : new SolidColorBrush(Color.Parse("#D8DCD5"));
    public Avalonia.Thickness BorderThickness => new(IsSelected ? 2 : 1);
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
            OnPropertyChanged(nameof(BorderThickness));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Dispose() => Preview.Dispose();
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
