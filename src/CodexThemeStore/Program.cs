using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CodexThemeStore.Core;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var externalImport = args.Length == 1 &&
            (args[0].StartsWith("dreamskin:", StringComparison.OrdinalIgnoreCase) ||
             args[0].EndsWith(".dreamskin", StringComparison.OrdinalIgnoreCase));
        if (args.Length == 0 || externalImport)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new ThemeStoreForm(externalImport ? args[0] : null));
            return 0;
        }

        NativeMethods.AttachParentConsole();
        try
        {
            var app = new ThemeStoreApp();
            return app.Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            if (args.Length == 1 && args[0].StartsWith("dreamskin:", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(ex.Message, "Dream Skin 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
    }
}

internal static class NativeMethods
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);

    internal static void AttachParentConsole()
    {
        if (!AttachConsole(AttachParentProcess)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
    }
}

internal sealed class ThemeStoreForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(246, 246, 242);
    private static readonly Color Ink = Color.FromArgb(28, 29, 27);
    private static readonly Color Muted = Color.FromArgb(103, 105, 100);
    private static readonly Color Accent = Color.FromArgb(218, 119, 86);
    private readonly List<ThemePreviewCard> _cards = [];
    private readonly FlowLayoutPanel _grid;
    private readonly ComboBox _sourceCombo;
    private readonly ComboBox _categoryCombo;
    private readonly Button _refreshButton;
    private readonly Label _themeCountLabel;
    private readonly Label _selectionLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progress;
    private readonly Button _applyButton;
    private readonly Button _saveButton;
    private readonly Button _rollbackButton;
    private readonly string? _externalImport;
    private ThemePreviewCard? _selectedCard;

    public ThemeStoreForm(string? externalImport = null)
    {
        _externalImport = externalImport;
        Text = "Codex 主题商店";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 680);
        ClientSize = new Size(1120, 790);
        BackColor = Canvas;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(28, 22, 28, 22),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        var title = new Label
        {
            AutoSize = true,
            Text = "Codex 主题商店",
            Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Ink,
            Location = new Point(0, 0),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = "选择预览，自动适配 Store 与 Patched 版本",
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Muted,
            Location = new Point(2, 48),
        };
        _themeCountLabel = new Label
        {
            AutoSize = true,
            Text = "正在读取主题",
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Accent,
        };
        _categoryCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 112,
            Height = 32,
            Font = new Font("Microsoft YaHei UI", 9F),
        };
        _categoryCombo.Items.AddRange(new object[] { "全部分类", "人物", "动漫", "游戏", "风景", "极简", "节日", "其他" });
        _categoryCombo.SelectedIndex = 0;
        _categoryCombo.SelectedIndexChanged += (_, _) => ReloadThemeCards();

        _sourceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 132,
            Height = 32,
            Font = new Font("Microsoft YaHei UI", 9F),
            DataSource = ThemeRepositoryClient.Sources.ToList(),
            DisplayMember = nameof(ThemeRepositorySource.Name),
        };
        var savedSettings = ThemeRepositoryClient.LoadSettings();
        _sourceCombo.SelectedItem = ThemeRepositoryClient.Sources.FirstOrDefault(item => item.Id == savedSettings.SourceId) ?? ThemeRepositoryClient.Sources[0];

        _refreshButton = CreateButton("刷新主题", 92, false);
        _refreshButton.Height = 32;
        _refreshButton.Click += async (_, _) => await RefreshThemesAsync(false);

        void LayoutHeaderControls()
        {
            var right = header.ClientSize.Width;
            _refreshButton.Location = new Point(Math.Max(0, right - _refreshButton.Width), 7);
            _sourceCombo.Location = new Point(Math.Max(0, _refreshButton.Left - _sourceCombo.Width - 10), 7);
            _categoryCombo.Location = new Point(Math.Max(0, _sourceCombo.Left - _categoryCombo.Width - 10), 7);
            _themeCountLabel.Location = new Point(Math.Max(0, _categoryCombo.Left - _themeCountLabel.Width - 16), 14);
        }
        header.Resize += (_, _) => LayoutHeaderControls();
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_themeCountLabel);
        header.Controls.Add(_categoryCombo);
        header.Controls.Add(_sourceCombo);
        header.Controls.Add(_refreshButton);
        LayoutHeaderControls();
        root.Controls.Add(header, 0, 0);

        _grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 10),
        };
        _grid.Resize += (_, _) => LayoutThemeCards();
        root.Controls.Add(_grid, 0, 1);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(18, 14, 18, 14),
            Margin = new Padding(8, 8, 8, 0),
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(221, 221, 215));
            e.Graphics.DrawLine(pen, 0, 0, footer.ClientSize.Width, 0);
        };
        root.Controls.Add(footer, 0, 2);

        _selectionLabel = new Label
        {
            AutoSize = true,
            Text = "请选择主题",
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Ink,
            Location = new Point(18, 14),
        };
        _statusLabel = new Label
        {
            AutoSize = false,
            Text = "准备就绪",
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Muted,
            Location = new Point(18, 42),
            Size = new Size(390, 38),
            AutoEllipsis = false,
        };
        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false,
            Location = new Point(18, 86),
            Size = new Size(320, 8),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };
        footer.Controls.Add(_selectionLabel);
        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(_progress);

        _rollbackButton = CreateButton("恢复默认", 96, false);
        _saveButton = CreateButton("仅保存主题", 112, false);
        _applyButton = CreateButton("应用并重启 Codex", 168, true);
        _rollbackButton.Click += async (_, _) => await RollbackAsync();
        _saveButton.Click += async (_, _) => await ApplyAsync(restart: false);
        _applyButton.Click += async (_, _) => await ApplyAsync(restart: true);
        footer.Controls.Add(_rollbackButton);
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_applyButton);
        footer.Resize += (_, _) => LayoutFooterButtons(footer);
        LayoutFooterButtons(footer);

        ReloadThemeCards();
        Shown += async (_, _) =>
        {
            if (_externalImport is not null) await HandleExternalImportAsync(_externalImport);
            else await RefreshThemesAsync(true);
        };
    }

    private static Button CreateButton(string text, int width, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(205, 205, 198);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(198, 98, 67) : Color.FromArgb(245, 245, 241);
        return button;
    }

    private void LayoutFooterButtons(Control footer)
    {
        const int gap = 10;
        var right = footer.ClientSize.Width - footer.Padding.Right;
        _applyButton.Location = new Point(right - _applyButton.Width, 30);
        right = _applyButton.Left - gap;
        _saveButton.Location = new Point(right - _saveButton.Width, 30);
        right = _saveButton.Left - gap;
        _rollbackButton.Location = new Point(right - _rollbackButton.Width, 30);

        // Let status text use every pixel left of the buttons and wrap instead of being hidden.
        var statusRight = Math.Max(220, _rollbackButton.Left - 18);
        _statusLabel.Width = Math.Max(180, statusRight - _statusLabel.Left);
    }

    private static List<ThemePreviewModel> LoadPreviewThemes()
    {
        return ThemeStoreApp.LoadThemes(allowEmpty: true)
            .Select(theme => new ThemePreviewModel(
                theme.CodeThemeId,
                theme.DisplayName,
                theme.Description,
                theme.Category,
                theme.ResolvePreviewImage(),
                theme.SourcePath))
            .ToList();
    }

    private void ReloadThemeCards()
    {
        var selectedId = _selectedCard?.Model.Id;
        _selectedCard = null;
        foreach (var card in _cards) card.Dispose();
        _cards.Clear();
        _grid.Controls.Clear();

        var category = _categoryCombo.SelectedItem?.ToString() ?? "全部分类";
        var allThemes = LoadPreviewThemes();
        var visibleThemes = category == "全部分类"
            ? allThemes
            : allThemes.Where(theme => theme.Category == category).ToList();
        foreach (var theme in visibleThemes)
        {
            var card = new ThemePreviewCard(theme);
            card.ThemeSelected += (_, _) => SelectCard(card);
            _cards.Add(card);
            _grid.Controls.Add(card);
        }
        _themeCountLabel.Text = category == "全部分类" ? $"{allThemes.Count} 个主题" : $"{visibleThemes.Count} / {allThemes.Count}";
        LayoutThemeCards();
        var selected = _cards.FirstOrDefault(card => card.Model.Id == selectedId) ?? _cards.FirstOrDefault();
        if (selected is not null) SelectCard(selected);
        else
        {
            _selectionLabel.Text = "当前分类暂无主题";
            _statusLabel.Text = "请选择其他分类或刷新主题仓库";
        }
    }

    private void LayoutThemeCards()
    {
        if (_grid.ClientSize.Width <= 0) return;
        var availableWidth = Math.Max(400, _grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        var cardWidth = Math.Max(360, (availableWidth - 32) / 2);
        var cardHeight = Math.Max(240, (_grid.ClientSize.Height - 34) / 2);
        foreach (var card in _cards) card.Size = new Size(cardWidth, cardHeight);
    }

    private async Task RefreshThemesAsync(bool silent)
    {
        var source = _sourceCombo.SelectedItem as ThemeRepositorySource ?? ThemeRepositoryClient.Sources[0];
        var current = ThemeRepositoryClient.LoadSettings();
        var settings = current with { SourceId = source.Id };
        SetBusy(true, silent ? "正在同步最新主题..." : $"正在通过 {source.Name} 刷新...");
        try
        {
            var result = await new ThemeRepositoryClient().SyncAsync(settings);
            ReloadThemeCards();
            _statusLabel.Text = $"已通过 {result.SourceName} 更新 {result.ThemeCount} 个主题";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = silent && _cards.Count > 0 ? "远程同步失败，已使用本地缓存" : "同步失败，请切换线路后重试";
            if (!silent) MessageBox.Show(this, ex.Message, "刷新主题", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private async Task HandleExternalImportAsync(string value)
    {
        try
        {
            DreamSkinImportResult imported;
            if (value.StartsWith("dreamskin:", StringComparison.OrdinalIgnoreCase))
            {
                var request = DreamSkinProtocol.Parse(value);
                var confirmation = MessageBox.Show(
                    this,
                    $"来源：{request.PackageUri.Host}\n主题：{request.Id ?? "未提供"}\n版本：{request.Version ?? "未提供"}\n大小：{request.Size:N0} 字节\n\n" +
                    "客户端会自动尝试当前线路和备用镜像，并校验 SHA-256、Ed25519 签名及全部图片。是否继续？",
                    "导入 Dream Skin 主题",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes) return;

                SetBusy(true, "正在准备下载主题...");
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
                var reporter = new Progress<DreamSkinDownloadProgress>(UpdateDownloadProgress);
                var preferredSource = (_sourceCombo.SelectedItem as ThemeRepositorySource)?.Id
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
                var confirmation = MessageBox.Show(
                    this,
                    $"验证并安装本地主题包？\n\n{Path.GetFileName(path)}\n\n安装后不会自动应用。",
                    "导入 Dream Skin 主题",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes) return;
                SetBusy(true, "正在验证主题签名和图片...");
                imported = await Task.Run(() => DreamSkinPackageInstaller.ImportLocal(path, platform: "windows"));
            }

            ReloadThemeCards();
            _statusLabel.Text = $"{imported.DisplayName} 已安全安装";
            var applyNow = MessageBox.Show(
                this,
                $"{imported.DisplayName} 已通过签名验证并安装。\n\n是否立即应用并重启 Codex？",
                "主题安装完成",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (applyNow != DialogResult.Yes) return;

            SetBusy(true, "正在应用主题并重启 Codex...");
            await Task.Run(() =>
            {
                var app = new ThemeStoreApp();
                if (app.Run(["apply", ThemeStoreApp.ResolveImportedThemeForApply(imported)]) != 0)
                    throw new InvalidOperationException("主题应用失败。");
                if (app.Run(["launch"]) != 0)
                    throw new InvalidOperationException("Codex 主题启动失败。");
            });
            _statusLabel.Text = $"{imported.DisplayName} 已应用";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "主题导入失败";
            MessageBox.Show(this, ex.Message, "Dream Skin 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void UpdateDownloadProgress(DreamSkinDownloadProgress progress)
    {
        var percent = progress.TotalBytes <= 0
            ? 0
            : (int)Math.Clamp(progress.BytesReceived * 100 / progress.TotalBytes, 0, 100);
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = percent;
        _statusLabel.Text = progress.Stage == "正在下载"
            ? $"正在通过 {progress.SourceName} 下载主题：{percent}%"
            : $"{progress.Stage}（{progress.SourceName}）";
    }

    private void SelectCard(ThemePreviewCard card)
    {
        _selectedCard = card;
        foreach (var item in _cards) item.Selected = ReferenceEquals(item, card);
        _selectionLabel.Text = $"已选择：{card.Model.Name}";
        _statusLabel.Text = "可以仅保存，或应用后重新启动 Codex";
    }

    private async Task ApplyAsync(bool restart)
    {
        if (_selectedCard is null) return;
        if (restart)
        {
            var answer = MessageBox.Show(
                this,
                "应用主题会关闭当前 Codex，然后以主题模式重新打开。是否继续？",
                "应用主题",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (answer != DialogResult.Yes) return;
        }

        var selected = _selectedCard.Model;
        var succeeded = await RunBusyAsync(restart ? "正在应用并重新启动 Codex..." : "正在保存主题...", () =>
        {
            var app = new ThemeStoreApp();
            var result = app.Run(["apply", selected.SourcePath]);
            if (result != 0) throw new InvalidOperationException("主题保存失败。");
            if (restart)
            {
                result = app.Run(["launch"]);
                if (result != 0) throw new InvalidOperationException("Codex 主题启动失败。");
            }
        });
        if (succeeded) _statusLabel.Text = restart ? $"{selected.Name} 已应用" : $"{selected.Name} 已保存";
    }

    private async Task RollbackAsync()
    {
        var answer = MessageBox.Show(
            this,
            "恢复 Codex 默认主题并清除已保存主题？",
            "恢复默认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        var succeeded = await RunBusyAsync("正在恢复默认主题...", () =>
        {
            var result = new ThemeStoreApp().Run(["rollback"]);
            if (result != 0) throw new InvalidOperationException("主题回滚失败。");
        });
        if (succeeded) _statusLabel.Text = "已恢复默认主题";
    }

    private async Task<bool> RunBusyAsync(string message, Action action)
    {
        SetBusy(true, message);
        try
        {
            await Task.Run(action);
            return true;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "操作失败";
            MessageBox.Show(this, ex.Message, "Codex 主题商店", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _statusLabel.Text = message;
        _progress.Visible = busy;
        _applyButton.Enabled = !busy && _selectedCard is not null;
        _saveButton.Enabled = !busy && _selectedCard is not null;
        _rollbackButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _sourceCombo.Enabled = !busy;
        _categoryCombo.Enabled = !busy;
        foreach (var card in _cards) card.Enabled = !busy;
        if (!busy)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Value = 0;
        }
    }
}

internal sealed record ThemePreviewModel(string Id, string Name, string Description, string Category, string PreviewPath, string SourcePath);

internal sealed class ThemePreviewCard : UserControl
{
    private static readonly Color Accent = Color.FromArgb(218, 119, 86);
    private readonly Label _selectedLabel;
    private bool _selected;

    public ThemePreviewCard(ThemePreviewModel model)
    {
        Model = model;
        Dock = DockStyle.None;
        Margin = new Padding(8);
        Padding = new Padding(2);
        BackColor = Color.White;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(layout);

        var preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(235, 235, 230),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadPreview(model.PreviewPath),
            Cursor = Cursors.Hand,
        };
        layout.Controls.Add(preview, 0, 0);

        var details = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 7, 14, 5) };
        var name = new Label
        {
            AutoSize = true,
            Text = model.Name,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(28, 29, 27),
            Location = new Point(14, 7),
            Cursor = Cursors.Hand,
        };
        var description = new Label
        {
            AutoSize = true,
            Text = model.Description,
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(108, 109, 105),
            Location = new Point(14, 29),
            Cursor = Cursors.Hand,
        };
        _selectedLabel = new Label
        {
            AutoSize = true,
            Text = "已选择",
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Accent,
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand,
        };
        details.Resize += (_, _) => _selectedLabel.Location = new Point(details.ClientSize.Width - _selectedLabel.Width - 14, 18);
        details.Controls.Add(name);
        details.Controls.Add(description);
        details.Controls.Add(_selectedLabel);
        layout.Controls.Add(details, 0, 1);

        foreach (Control control in new Control[] { this, layout, preview, details, name, description, _selectedLabel })
        {
            control.Click += (_, _) => ThemeSelected?.Invoke(this, EventArgs.Empty);
        }
        Resize += (_, _) => UpdateRegion();
    }

    public event EventHandler? ThemeSelected;
    public ThemePreviewModel Model { get; }
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            _selectedLabel.Visible = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), 8);
        using var pen = new Pen(Selected ? Accent : Color.FromArgb(210, 210, 203), Selected ? 3F : 1F);
        e.Graphics.DrawPath(pen, path);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 8);
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Image LoadPreview(string path)
    {
        using var source = Image.FromFile(path);
        const int width = 960;
        var height = Math.Max(1, (int)Math.Round(source.Height * (width / (double)source.Width)));
        var result = new Bitmap(width, height);
        result.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }
}

internal static class ThemeHook
{
    public const string LinkId = "codex-theme-store-stylesheet";
    public const string ScriptId = "codex-theme-store-script";
    public const string Begin = "<!-- codex-theme-store:begin -->";
    public const string End = "<!-- codex-theme-store:end -->";
}

internal sealed class ThemeStoreApp
{
    private readonly string? _webviewDir;
    private readonly string _storeStateDir;

    public ThemeStoreApp()
    {
        _webviewDir = FindWebviewDir();
        _storeStateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStore");
    }

    public int Run(string[] args)
    {
        if (args.Length == 0)
        {
            ShowMenu();
            return 0;
        }

        if (args.Length == 1 && args[0].StartsWith("dreamskin:", StringComparison.OrdinalIgnoreCase))
        {
            return ImportFromProtocol(args[0]);
        }

        var command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "list":
                ListThemes();
                return 0;
            case "apply":
                Apply(args.Length > 1 ? string.Join(" ", args.Skip(1)) : "dilraba-star");
                return 0;
            case "import":
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: Codex-Skin.exe import C:\\path\\theme.dreamskin [--apply]");
                    return 1;
                }
                var imported = DreamSkinPackageInstaller.ImportLocal(args[1]);
                Console.WriteLine($"已验证并安装主题: {imported.DisplayName} ({imported.Id} {imported.Version})");
                Console.WriteLine($"包 SHA-256: {imported.PackageSha256}");
                if (args.Skip(2).Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase))) Apply(imported.ManifestPath);
                else Console.WriteLine("主题尚未应用。确认后可再次使用 import ... --apply，或使用 apply <theme.json>。");
                return 0;
            case "protocol":
                return ManageProtocol(args.Skip(1).ToArray());
            case "launch":
                LaunchStore();
                return 0;
            case "status":
                Status();
                return 0;
            case "refresh":
                RefreshRepository(args.Length > 1 ? args[1] : null);
                return 0;
            case "rollback":
                Rollback();
                return 0;
            case "help":
            case "-h":
            case "--help":
                PrintUsage();
                return 0;
            default:
                Console.WriteLine($"Unknown command: {command}");
                PrintUsage();
                return 1;
        }
    }

    private void ShowMenu()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Codex 主题商店（自动适配版）");
        Console.WriteLine();
        var themes = GetThemeFiles().ToList();
        for (var i = 0; i < themes.Count; i++)
        {
            var theme = ThemeDefinition.Load(themes[i]);
            Console.WriteLine($"{i + 1}. {theme.CodeThemeId} - {theme.DisplayName}");
        }
        Console.WriteLine("R. 回滚主题");
        Console.WriteLine("S. 查看状态");
        Console.WriteLine();
        Console.Write("请选择主题编号: ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        if (input.Equals("r", StringComparison.OrdinalIgnoreCase))
        {
            Rollback();
            PauseForMenu();
            return;
        }
        if (input.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            Status();
            PauseForMenu();
            return;
        }
        if (int.TryParse(input, out var index) && index >= 1 && index <= themes.Count)
        {
            var storeNeedsLaunch = Apply(themes[index - 1]);
            if (storeNeedsLaunch)
            {
                Console.WriteLine();
                Console.Write("检测到 Store Codex。现在关闭并以主题模式重新打开？[Y/n]: ");
                var restart = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(restart) || restart.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchStore();
                }
            }
            PauseForMenu();
            return;
        }
        Console.WriteLine("没有选择主题。");
        PauseForMenu();
    }

    private static void PauseForMenu()
    {
        Console.WriteLine();
        Console.Write("按 Enter 退出...");
        Console.ReadLine();
    }

    private void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Codex-Skin.exe list");
        Console.WriteLine("  Codex-Skin.exe apply dilraba-star");
        Console.WriteLine("  Codex-Skin.exe apply C:\\path\\theme.json");
        Console.WriteLine("  Codex-Skin.exe import C:\\path\\theme.dreamskin [--apply]");
        Console.WriteLine("  Codex-Skin.exe protocol register|unregister|status|self-test");
        Console.WriteLine("  Codex-Skin.exe launch");
        Console.WriteLine("  Codex-Skin.exe refresh [github|gh-proxy|ghfast]");
        Console.WriteLine("  Codex-Skin.exe status");
        Console.WriteLine("  Codex-Skin.exe rollback");
    }

    private int ImportFromProtocol(string value)
    {
        var request = DreamSkinProtocol.Parse(value);
        var hint = request.Id is null ? "未提供" : request.Id;
        var version = request.Version is null ? "未提供" : request.Version;
        var confirmation = MessageBox.Show(
            $"来源：{request.PackageUri.Host}\n主题：{hint}\n版本：{version}\n大小：{request.Size:N0} 字节\n\n客户端将下载、校验签名并安装主题，但不会自动应用。是否继续？",
            "导入 Dream Skin 主题",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            Console.WriteLine("DSI_USER_CANCELLED: 用户取消导入。");
            return 0;
        }

        var imported = DreamSkinDownloadService.DownloadAndImport(request);
        Console.WriteLine($"已验证并安装主题: {imported.DisplayName} ({imported.Id} {imported.Version})");
        var applyNow = MessageBox.Show(
            $"{imported.DisplayName} 已安全安装。是否立即应用？",
            "主题安装完成",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (applyNow == DialogResult.Yes) Apply(ResolveImportedThemeForApply(imported));
        return 0;
    }

    internal static string ResolveImportedThemeForApply(DreamSkinImportResult imported)
    {
        var official = LoadThemes().FirstOrDefault(theme =>
            theme.CodeThemeId.Equals(imported.Id, StringComparison.Ordinal) &&
            ThemeCatalog.CompareVersions(theme.Version, imported.Version) >= 0);
        return official?.SourcePath ?? imported.ManifestPath;
    }

    private static int ManageProtocol(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: Codex-Skin.exe protocol register|unregister|status|self-test");
            return 1;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "register":
                DreamSkinShellIntegration.Register();
                Console.WriteLine("已为当前用户注册 dreamskin:// 和 .dreamskin 文件关联。");
                return 0;
            case "unregister":
                DreamSkinShellIntegration.Unregister();
                Console.WriteLine("已移除当前用户的 dreamskin:// 和 .dreamskin 文件关联。");
                return 0;
            case "status":
                Console.WriteLine(DreamSkinShellIntegration.IsRegistered() ? "registered" : "not registered");
                return 0;
            case "self-test":
                DreamSkinProtocol.RunSelfTest();
                Console.WriteLine("dreamskin:// protocol self-test passed.");
                return 0;
            default:
                Console.Error.WriteLine("Usage: Codex-Skin.exe protocol register|unregister|status|self-test");
                return 1;
        }
    }

    private void ListThemes()
    {
        foreach (var file in GetThemeFiles())
        {
            var theme = ThemeDefinition.Load(file);
            Console.WriteLine($"{theme.CodeThemeId}\t{theme.DisplayName}\t{theme.Variant}");
        }
    }

    private static void RefreshRepository(string? sourceId)
    {
        var settings = ThemeRepositoryClient.LoadSettings();
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            var source = ThemeRepositoryClient.Sources.FirstOrDefault(item => item.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"未知加速源: {sourceId}");
            settings = settings with { SourceId = source.Id };
        }
        var result = new ThemeRepositoryClient().SyncAsync(settings).GetAwaiter().GetResult();
        Console.WriteLine($"已通过 {result.SourceName} 更新 {result.ThemeCount} 个主题。");
    }

    private bool Apply(string input)
    {
        var themePath = ResolveThemeInput(input);
        var theme = ThemeDefinition.Load(themePath);
        var js = JsBuilder.Build(theme.Copy, theme.Home, theme.CodeThemeId, BuildStoreLogoUrl(theme), BuildStorePetUrl(theme));

        Directory.CreateDirectory(_storeStateDir);
        var storeCss = CssBuilder.Build(theme, BuildStoreBackgroundUrl(theme));
        File.WriteAllText(Path.Combine(_storeStateDir, "codex-theme.css"), storeCss, Encoding.UTF8);
        File.WriteAllText(Path.Combine(_storeStateDir, "codex-theme.js"), js, Encoding.UTF8);
        File.WriteAllText(Path.Combine(_storeStateDir, "current-theme.json"), theme.RawJson, Encoding.UTF8);

        if (_webviewDir is not null)
        {
            var storeDir = Path.Combine(_webviewDir, "theme-store");
            var assetDir = Path.Combine(storeDir, "assets");
            var backupDir = Path.Combine(storeDir, "backups");
            Directory.CreateDirectory(assetDir);
            Directory.CreateDirectory(backupDir);
            var backgroundUrl = InstallBackground(theme, assetDir);
            File.WriteAllText(Path.Combine(storeDir, "codex-theme.css"), CssBuilder.Build(theme, backgroundUrl), Encoding.UTF8);
            File.WriteAllText(Path.Combine(storeDir, "codex-theme.js"), js, Encoding.UTF8);
            File.WriteAllText(Path.Combine(storeDir, "current-theme.json"), theme.RawJson, Encoding.UTF8);
            InstallHook(backupDir);
            Console.WriteLine($"Patched 版: 已写入 {_webviewDir}");
        }

        Console.WriteLine($"已选择主题: {theme.DisplayName} ({theme.Variant})");
        var storeRunning = StoreCodexBridge.IsStoreRunning();
        if (StoreCodexBridge.TryInjectSavedTheme(_storeStateDir, TimeSpan.FromSeconds(8)))
        {
            Console.WriteLine("Store 版: 已注入当前窗口。");
            return false;
        }

        if (storeRunning)
        {
            Console.WriteLine("Store 版: 正在运行，但启动时没有开启主题注入端口。");
            Console.WriteLine("请运行 Codex-Skin.exe launch，或在双击菜单中确认主题重启。");
            return true;
        }

        Console.WriteLine("Store 版: 主题已保存。运行 Codex-Skin.exe launch 可按主题模式启动。");
        return true;
    }

    private void Status()
    {
        Console.WriteLine($"Store Codex: {(StoreCodexBridge.IsStoreRunning() ? "running" : "not running")}");
        Console.WriteLine($"Store injection port: {(StoreCodexBridge.IsDebugPortReady() ? "ready" : "not available")}");

        var savedMetaPath = Path.Combine(_storeStateDir, "current-theme.json");
        if (File.Exists(savedMetaPath))
        {
            var savedTheme = ThemeDefinition.Load(savedMetaPath);
            Console.WriteLine($"Saved theme: {savedTheme.DisplayName} ({savedTheme.Variant})");
            if (!string.IsNullOrWhiteSpace(savedTheme.BackgroundImage))
            {
                Console.WriteLine($"Background source: {savedTheme.BackgroundImage}");
            }
        }
        else
        {
            Console.WriteLine("Saved theme: none");
        }

        if (_webviewDir is null)
        {
            Console.WriteLine("Patched Codex: not installed");
            return;
        }

        var indexPath = Path.Combine(_webviewDir!, "index.html");
        var hooked = File.Exists(indexPath) && File.ReadAllText(indexPath).Contains(ThemeHook.LinkId, StringComparison.Ordinal);
        Console.WriteLine($"Patched Codex: {(hooked ? "hook installed" : "hook not installed")}");
        Console.WriteLine($"Patched webview: {_webviewDir}");
    }

    private void Rollback()
    {
        if (_webviewDir is not null)
        {
            var indexPath = Path.Combine(_webviewDir, "index.html");
            if (File.Exists(indexPath))
            {
                var index = File.ReadAllText(indexPath);
                var block = new Regex($"{Regex.Escape(ThemeHook.Begin)}[\\s\\S]*?{Regex.Escape(ThemeHook.End)}\\s*", RegexOptions.Multiline);
                index = block.Replace(index, "");
                File.WriteAllText(indexPath, index, Encoding.UTF8);
            }

            var storeDir = Path.Combine(_webviewDir, "theme-store");
            Directory.CreateDirectory(storeDir);
            File.WriteAllText(Path.Combine(storeDir, "codex-theme.css"), "/* Codex theme store disabled. */\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(storeDir, "codex-theme.js"), "/* Codex theme store disabled. */\n", Encoding.UTF8);
        }

        StoreCodexBridge.TryRemoveLiveTheme(TimeSpan.FromSeconds(2));
        if (Directory.Exists(_storeStateDir))
        {
            foreach (var file in new[] { "codex-theme.css", "codex-theme.js", "current-theme.json" })
            {
                var path = Path.Combine(_storeStateDir, file);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        Console.WriteLine("Rolled back saved and live Codex themes.");
    }

    private void LaunchStore()
    {
        var cssPath = Path.Combine(_storeStateDir, "codex-theme.css");
        var jsPath = Path.Combine(_storeStateDir, "codex-theme.js");
        if (!File.Exists(cssPath) || !File.Exists(jsPath))
        {
            Console.WriteLine("No saved theme. Applying dilraba-star first.");
            Apply("dilraba-star");
        }

        Console.WriteLine("正在关闭并以主题模式重新启动 Store Codex...");
        StoreCodexBridge.RestartAndInject(_storeStateDir, TimeSpan.FromSeconds(90));
        Console.WriteLine("Store Codex 已启动，主题注入成功。");
    }

    private IEnumerable<string> GetThemeFiles()
    {
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["dilraba-star.json"] = 0,
            ["jackson-sage.json"] = 1,
            ["kun-stage.json"] = 2,
            ["enfp-pop.json"] = 3,
        };
        foreach (var file in LoadThemes().Select(theme => theme.SourcePath)
                     .OrderBy(file => priority.GetValueOrDefault(Path.GetFileName(file), 100))
                     .ThenBy(Path.GetFileName))
        {
            yield return file;
        }
    }

    private string ResolveThemeInput(string input)
    {
        var trimmed = input.Trim().Trim('"');
        if (File.Exists(trimmed)) return Path.GetFullPath(trimmed);

        if (trimmed.StartsWith("codex-theme-v1:", StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), $"codex-theme-{Guid.NewGuid():N}.json");
            var json = trimmed["codex-theme-v1:".Length..]
                .Replace('“', '"')
                .Replace('”', '"')
                .Replace('‘', '\'')
                .Replace('’', '\'');
            File.WriteAllText(temp, json, Encoding.UTF8);
            return temp;
        }

        var id = trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(trimmed) : trimmed;
        var named = LoadThemes().FirstOrDefault(theme => theme.CodeThemeId.Equals(id, StringComparison.Ordinal))?.SourcePath;
        if (named is not null) return named;
        throw new FileNotFoundException($"Theme not found: {input}");
    }

    private string? InstallBackground(ThemeDefinition theme, string assetDir)
    {
        if (string.IsNullOrWhiteSpace(theme.BackgroundImage)) return null;
        var source = theme.ResolveBackgroundImage();
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        var sourcePath = source;
        if (!File.Exists(sourcePath)) throw new FileNotFoundException($"Theme image not found: {sourcePath}");

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif" };
        if (!allowed.Contains(ext)) throw new InvalidOperationException($"Unsupported background image type: {ext}");

        var destination = Path.Combine(assetDir, $"background-{SafeAssetName(theme.CodeThemeId)}{ext}");
        File.Copy(sourcePath, destination, true);
        return $"./assets/{Path.GetFileName(destination)}";
    }

    private static string? BuildStoreBackgroundUrl(ThemeDefinition theme)
    {
        if (string.IsNullOrWhiteSpace(theme.BackgroundImage)) return null;
        return BuildStoreImageUrl(theme.ResolveBackgroundImage());
    }

    private static string? BuildStoreLogoUrl(ThemeDefinition theme)
    {
        if (string.IsNullOrWhiteSpace(theme.LogoImage)) return null;
        return BuildStoreImageUrl(theme.ResolveLogoImage());
    }

    private static string? BuildStorePetUrl(ThemeDefinition theme)
    {
        if (string.IsNullOrWhiteSpace(theme.PetImage)) return null;
        return BuildStoreImageUrl(theme.ResolvePetImage());
    }

    private static string BuildStoreImageUrl(string source)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        var sourcePath = source;
        if (!File.Exists(sourcePath)) throw new FileNotFoundException($"Theme image not found: {sourcePath}");
        var mediaType = Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => throw new InvalidOperationException($"Unsupported background image type: {Path.GetExtension(sourcePath)}"),
        };
        return $"data:{mediaType};base64,{Convert.ToBase64String(File.ReadAllBytes(sourcePath))}";
    }

    private static string SafeAssetName(string value)
    {
        var safe = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "theme" : safe;
    }

    private void InstallHook(string backupDir)
    {
        var indexPath = Path.Combine(_webviewDir!, "index.html");
        if (!File.Exists(indexPath)) throw new FileNotFoundException($"index.html not found: {indexPath}");

        var index = File.ReadAllText(indexPath);
        var hook = $"{ThemeHook.Begin}\n    <link id=\"{ThemeHook.LinkId}\" rel=\"stylesheet\" crossorigin href=\"./theme-store/codex-theme.css\">\n    <script id=\"{ThemeHook.ScriptId}\" type=\"module\" crossorigin src=\"./theme-store/codex-theme.js\"></script>\n    {ThemeHook.End}";
        var block = new Regex($"{Regex.Escape(ThemeHook.Begin)}[\\s\\S]*?{Regex.Escape(ThemeHook.End)}", RegexOptions.Multiline);

        if (!index.Contains(ThemeHook.LinkId, StringComparison.Ordinal))
        {
            File.WriteAllText(Path.Combine(backupDir, $"index.{DateTime.UtcNow:yyyyMMddHHmmss}.html.bak"), index, Encoding.UTF8);
        }

        if (block.IsMatch(index))
        {
            index = block.Replace(index, hook);
        }
        else if (index.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            index = Regex.Replace(index, "</head>", hook + "\n</head>", RegexOptions.IgnoreCase);
        }
        else
        {
            index += "\n" + hook + "\n";
        }

        File.WriteAllText(indexPath, index, Encoding.UTF8);
    }

    private static string? FindWebviewDir()
    {
        var env = Environment.GetEnvironmentVariable("CODEX_THEME_WEBVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env, "index.html"))) return env;

        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Codex-Patched",
            "resources",
            "app",
            "webview");
        if (File.Exists(Path.Combine(defaultPath, "index.html"))) return defaultPath;

        return null;
    }

    internal static IReadOnlyList<ThemeDefinition> LoadThemes(bool allowEmpty = false)
    {
        return ThemeCatalog.Load(new[]
        {
            ThemeRepositoryClient.CacheThemeDirectory,
            Path.Combine(AppContext.BaseDirectory, "themes"),
            Path.Combine(Directory.GetCurrentDirectory(), "themes"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "themes")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "themes")),
        }, platform: "windows", allowEmpty: allowEmpty);
    }
}

internal static class StoreCodexBridge
{
    private const int DebugPort = 9229;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly CdpThemeInjector SharedInjector = new();

    public static CodexInstallation? DiscoverInstallation()
    {
        var executable = FindStoreExecutable();
        return executable is null ? null : new CodexInstallation(Path.GetDirectoryName(executable)!, executable, "Codex");
    }

    public static void StopStore() => StopStoreProcesses();
    public static void StartStore(CodexInstallation installation, bool enableCdp = true) => StartStoreApp(installation.ExecutablePath, enableCdp);

    public static bool IsStoreRunning()
    {
        foreach (var process in GetStoreProcesses())
        {
            process.Dispose();
            return true;
        }
        return false;
    }

    public static bool IsDebugPortReady() => SharedInjector.IsReadyAsync().GetAwaiter().GetResult();

    public static bool TryInjectSavedTheme(string stateDir, TimeSpan timeout)
    {
        try
        {
            return SharedInjector.InjectAsync(ThemeInjectionPayload.Load(stateDir), timeout).GetAwaiter().GetResult() > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRemoveLiveTheme(TimeSpan timeout)
    {
        try
        {
            return SharedInjector.RemoveAsync(timeout).GetAwaiter().GetResult() > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void RestartAndInject(string stateDir, TimeSpan timeout)
    {
        var executable = FindStoreExecutable() ?? throw new FileNotFoundException("Cannot find the installed Store Codex executable.");
        StopStoreProcesses();

        StartStoreApp(executable);

        var deadline = DateTime.UtcNow + timeout;
        var portBecameReady = false;
        while (DateTime.UtcNow < deadline)
        {
            // Electron may show its window well before DevTools and the app page are ready.
            // Check the cheap endpoint first so an early, half-initialized page does not consume
            // most of the startup budget in a long CDP attempt.
            if (!IsDebugPortReady())
            {
                Thread.Sleep(500);
                continue;
            }

            portBecameReady = true;
            if (TryInjectSavedTheme(stateDir, TimeSpan.FromSeconds(8)))
            {
                Thread.Sleep(1500);
                TryInjectSavedTheme(stateDir, TimeSpan.FromSeconds(8));
                return;
            }
            Thread.Sleep(500);
        }

        if (!IsStoreRunning())
        {
            throw new TimeoutException("Codex 未能完成启动，请重新应用主题。");
        }
        if (portBecameReady || IsDebugPortReady())
        {
            throw new TimeoutException("Codex 已启动，但主题页面仍在加载，暂时无法完成注入。请等待几秒后点击“仅保存主题”即可重试，无需再次重启 Codex。");
        }
        throw new TimeoutException("Codex 已启动，但主题注入端口在 90 秒内仍未就绪。请点击“仅保存主题”重试；Codex 无需再次重启。");
    }

    private static IEnumerable<Process> GetStoreProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                process.Dispose();
                continue;
            }

            if (path is not null && path.Contains("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private static void StopStoreProcesses()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var processes = GetStoreProcesses().ToList();
            if (processes.Count == 0)
            {
                // Codex may already be closed. That is the desired state, not an error.
                return;
            }

            foreach (var process in processes.OrderBy(process => process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Killing a parent can terminate children in the same snapshot.
                    // Re-check the complete process set on the next pass.
                }
                finally
                {
                    process.Dispose();
                }
            }

            Thread.Sleep(250);
        }

        if (IsStoreRunning())
        {
            throw new InvalidOperationException("Codex did not close completely. Please try applying the theme again.");
        }
    }

    private static string? FindStoreExecutable()
    {
        foreach (var process in GetStoreProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && Path.GetFileName(path).Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        // If Codex is not running, there is no process path to inspect. The
        // per-user AppModel repository records the current package install root
        // and is readable without enumerating the protected WindowsApps folder.
        const string packageRepository = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using var repository = Registry.CurrentUser.OpenSubKey(packageRepository);
            var registryCandidate = repository?.GetSubKeyNames()
                .Where(name => name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
                .Select(name =>
                {
                    using var package = repository.OpenSubKey(name);
                    var root = package?.GetValue("PackageRootFolder") as string;
                    return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "app", "ChatGPT.exe");
                })
                .Where(path => path is not null && File.Exists(path))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path!))
                .FirstOrDefault();
            if (registryCandidate is not null) return registryCandidate;
        }
        catch
        {
            // Fall through to the legacy WindowsApps scan below.
        }

        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        if (!Directory.Exists(windowsApps)) return null;
        try
        {
            return Directory.GetDirectories(windowsApps, "OpenAI.Codex_*")
                .Select(directory => Path.Combine(directory, "app", "ChatGPT.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void StartStoreApp(string executable, bool enableCdp = true)
    {
        const string appUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        var arguments = enableCdp ? $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={DebugPort}" : "";
        Exception? activationError = null;

        // Codex is an MSIX packaged desktop app. Launching the executable inside
        // WindowsApps directly can fail after the package container is torn down.
        // ApplicationActivationManager asks Windows to create a fresh container
        // and also supports passing the Electron debugging arguments.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            object? managerObject = null;
            try
            {
                var managerType = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), throwOnError: true)!;
                managerObject = Activator.CreateInstance(managerType);
                var manager = (IApplicationActivationManager)managerObject!;
                var hr = manager.ActivateApplication(appUserModelId, arguments, ActivateOptions.None, out _);
                Marshal.ThrowExceptionForHR(hr);
                return;
            }
            catch (Exception ex)
            {
                activationError = ex;
                Thread.Sleep(500 + attempt * 500);
            }
            finally
            {
                if (managerObject is not null && Marshal.IsComObject(managerObject)) Marshal.FinalReleaseComObject(managerObject);
            }
        }

        // Fallback for installations where package activation is unavailable.
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true,
        };
        Exception? directStartError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Store Codex.");
                return;
            }
            catch (Exception ex)
            {
                directStartError = ex;
                Thread.Sleep(750 + attempt * 750);
            }
        }

        var detail = directStartError?.Message ?? activationError?.Message ?? "Unknown startup error.";
        throw new InvalidOperationException($"Failed to restart Store Codex after it closed: {detail}");
    }

    [Flags]
    private enum ActivateOptions
    {
        None = 0,
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray, IntPtr verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol(IntPtr appUserModelId, IntPtr itemArray, out uint processId);
    }

}

internal sealed class WindowsStoreCodexAdapter : ICodexPlatformAdapter
{
    public string PlatformName => "Windows";
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<CodexInstallation?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StoreCodexBridge.DiscoverInstallation());
    }

    public Task<bool> IsRunningAsync(CodexInstallation installation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StoreCodexBridge.IsStoreRunning());
    }

    public Task StopAsync(CodexInstallation installation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoreCodexBridge.StopStore();
        return Task.CompletedTask;
    }

    public Task StartAsync(CodexInstallation installation, bool enableCdp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoreCodexBridge.StartStore(installation, enableCdp);
        return Task.CompletedTask;
    }

    public Task<bool> InjectAsync(ThemeInjectionPayload payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return InjectCoreAsync(payload, timeout, cancellationToken);
    }

    public Task<bool> RollbackAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RollbackCoreAsync(timeout, cancellationToken);
    }

    private static async Task<bool> InjectCoreAsync(ThemeInjectionPayload payload, TimeSpan timeout, CancellationToken cancellationToken) =>
        await new CdpThemeInjector().InjectAsync(payload, timeout, cancellationToken) > 0;

    private static async Task<bool> RollbackCoreAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        await new CdpThemeInjector().RemoveAsync(timeout, cancellationToken) > 0;
}
