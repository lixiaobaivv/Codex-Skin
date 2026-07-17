using System.Text.RegularExpressions;
using Avalonia.Controls;
using CodexThemeStore.Core;

namespace CodexThemeStore.Desktop;

public sealed partial class RepositorySettingsWindow : Window
{
    private readonly string _sourceId;

    public RepositorySettingsWindow() : this(ThemeRepositorySettings.Default)
    {
    }

    public RepositorySettingsWindow(ThemeRepositorySettings settings)
    {
        InitializeComponent();
        _sourceId = settings.SourceId;
        RepositoryBox.Text = settings.Repository;
        BranchBox.Text = settings.Branch;
        CancelButton.Click += (_, _) => Close(null);
        SaveButton.Click += (_, _) => Save();
    }

    private void Save()
    {
        var repository = RepositoryBox.Text?.Trim() ?? "";
        var branch = BranchBox.Text?.Trim() ?? "";
        if (!Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ||
            !Regex.IsMatch(branch, "^[A-Za-z0-9._/-]+$") || branch.Contains("..", StringComparison.Ordinal))
        {
            ValidationLabel.Text = "请输入有效的仓库和分支。";
            return;
        }
        Close(new ThemeRepositorySettings(repository, branch, _sourceId));
    }
}
