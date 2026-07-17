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
    private ThemeCardModel? _selected;

    public MainWindow()
    {
        InitializeComponent();
        ThemeList.ItemsSource = _visibleThemes;
        CategoryCombo.ItemsSource = new[] { "全部分类", "人物", "动漫", "游戏", "风景", "极简", "节日", "其他" };
        CategoryCombo.SelectedIndex = 0;
        SourceCombo.ItemsSource = ThemeRepositoryClient.Sources;
        var settings = ThemeRepositoryClient.LoadSettings();
        SourceCombo.SelectedItem = ThemeRepositoryClient.Sources.FirstOrDefault(item => item.Id == settings.SourceId) ?? ThemeRepositoryClient.Sources[0];

        CategoryCombo.SelectionChanged += (_, _) => ApplyFilter();
        ThemeList.SelectionChanged += (_, _) => SelectTheme(ThemeList.SelectedItem as ThemeCardModel);
        RefreshButton.Click += async (_, _) => await RefreshAsync(false);
        RepositoryButton.Click += async (_, _) => await ConfigureRepositoryAsync();
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
        var directory = FindThemeDirectory();
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var theme = ThemeDefinition.Load(file);
            theme.ValidateAssets();
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
    }

    private static string FindThemeDirectory()
    {
        foreach (var directory in new[]
                 {
                     ThemeRepositoryClient.CacheThemeDirectory,
                     Path.Combine(AppContext.BaseDirectory, "themes"),
                     Path.Combine(Directory.GetCurrentDirectory(), "themes"),
                 })
        {
            if (!Directory.Exists(directory)) continue;
            var files = Directory.GetFiles(directory, "*.json");
            if (files.Length == 0) continue;
            try
            {
                foreach (var file in files) ThemeDefinition.Load(file).ValidateAssets();
                return directory;
            }
            catch when (Path.GetFullPath(directory).Equals(Path.GetFullPath(ThemeRepositoryClient.CacheThemeDirectory), StringComparison.Ordinal))
            {
            }
        }
        throw new DirectoryNotFoundException("找不到可用主题目录。");
    }

    private void ApplyFilter()
    {
        var category = CategoryCombo.SelectedItem as string ?? "全部分类";
        var previousSelection = _selected;
        _visibleThemes.Clear();
        foreach (var theme in _allThemes.Where(theme => category == "全部分类" || theme.Category == category))
            _visibleThemes.Add(theme);
        SelectTheme(previousSelection is not null && _visibleThemes.Contains(previousSelection)
            ? previousSelection
            : _visibleThemes.FirstOrDefault());
        CatalogLabel.Text = category == "全部分类" ? $"{_allThemes.Count} 个主题" : $"{_visibleThemes.Count} / {_allThemes.Count} 个主题";
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
        SetBusy(true, $"正在通过 {source.Name} 同步主题...");
        try
        {
            var result = await new ThemeRepositoryClient().SyncAsync(settings);
            LoadLocalThemes();
            StatusLabel.Text = $"已更新 {result.ThemeCount} 个主题";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = silent ? "同步失败，已使用本地主题" : "主题刷新失败";
            if (!silent) await ShowErrorAsync(ex.Message);
        }
        finally
        {
            SetBusy(false, StatusLabel.Text ?? "准备就绪");
        }
    }

    private async Task ConfigureRepositoryAsync()
    {
        var dialog = new RepositorySettingsWindow(ThemeRepositoryClient.LoadSettings());
        var settings = await dialog.ShowDialog<ThemeRepositorySettings?>(this);
        if (settings is null) return;
        ThemeRepositoryClient.SaveSettings(settings);
        StatusLabel.Text = $"主题仓库已设为 {settings.Repository}";
        await RefreshAsync(false);
    }

    private async Task ApplyAsync(bool restart)
    {
        if (_selected is null) return;
        if (restart && !await ConfirmAsync("应用主题会关闭当前 Codex，然后以主题模式重新打开。")) return;
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

    private void SetBusy(bool busy, string status)
    {
        BusyBar.IsVisible = busy;
        StatusLabel.Text = status;
        CategoryCombo.IsEnabled = !busy;
        SourceCombo.IsEnabled = !busy;
        RepositoryButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        ThemeList.IsEnabled = !busy;
        SetCommandAvailability(!busy);
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
            Title = "Codex Theme Store",
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
            Title = "Codex Theme Store",
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
