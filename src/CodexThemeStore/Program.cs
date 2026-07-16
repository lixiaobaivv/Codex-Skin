using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using Microsoft.Win32;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            NativeMethods.FreeConsole();
            ApplicationConfiguration.Initialize();
            Application.Run(new ThemeStoreForm());
            return 0;
        }

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
                MessageBox.Show(
                    ex.Message,
                    "Dream Skin 导入失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll")]
    internal static extern bool FreeConsole();
}

internal sealed class ThemeStoreForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(246, 246, 242);
    private static readonly Color Ink = Color.FromArgb(28, 29, 27);
    private static readonly Color Muted = Color.FromArgb(103, 105, 100);
    private static readonly Color Accent = Color.FromArgb(218, 119, 86);
    private readonly List<ThemePreviewCard> _cards = [];
    private readonly Label _selectionLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progress;
    private readonly Button _applyButton;
    private readonly Button _saveButton;
    private readonly Button _rollbackButton;
    private ThemePreviewCard? _selectedCard;

    public ThemeStoreForm()
    {
        Text = "Codex 主题商店";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 680);
        ClientSize = new Size(1120, 790);
        BackColor = Canvas;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

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
        var versionLabel = new Label
        {
            AutoSize = true,
            Text = "4 个主题",
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Accent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(header.Width - 80, 14),
        };
        header.Resize += (_, _) => versionLabel.Left = Math.Max(0, header.ClientSize.Width - versionLabel.Width);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(versionLabel);
        root.Controls.Add(header, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 0, 0, 10),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.Controls.Add(grid, 0, 1);

        var themes = LoadPreviewThemes();
        for (var index = 0; index < themes.Count; index++)
        {
            var card = new ThemePreviewCard(themes[index]);
            card.ThemeSelected += (_, _) => SelectCard(card);
            _cards.Add(card);
            grid.Controls.Add(card, index % 2, index / 2);
        }

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
            Size = new Size(250, 3),
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

        if (_cards.Count > 0) SelectCard(_cards[0]);
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
        var themeDir = ThemeStoreApp.FindThemeDir();
        var definitions = new[]
        {
            ("dilraba-star", "热巴星球", "星光蓝紫 · 浅色"),
            ("jackson-sage", "千玺星球", "鼠尾草绿 · 浅色"),
            ("kun-stage", "KUN 舞台", "黑金舞台 · 深色"),
            ("enfp-pop", "ENFP 小宇宙", "活力彩色 · 浅色"),
        };
        return definitions.Select(item =>
        {
            var theme = ThemeDefinition.Load(Path.Combine(themeDir, $"{item.Item1}.json"));
            return new ThemePreviewModel(item.Item1, item.Item2, item.Item3, theme.ResolvePreviewImage());
        }).ToList();
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
            var result = app.Run(["apply", selected.Id]);
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
        _applyButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _rollbackButton.Enabled = !busy;
        foreach (var card in _cards) card.Enabled = !busy;
    }
}

internal sealed record ThemePreviewModel(string Id, string Name, string Description, string PreviewPath);

internal sealed class ThemePreviewCard : UserControl
{
    private static readonly Color Accent = Color.FromArgb(218, 119, 86);
    private readonly Label _selectedLabel;
    private bool _selected;

    public ThemePreviewCard(ThemePreviewModel model)
    {
        Model = model;
        Dock = DockStyle.Fill;
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
    private readonly string _themeDir;
    private readonly string _storeStateDir;

    public ThemeStoreApp()
    {
        _webviewDir = FindWebviewDir();
        _themeDir = FindThemeDir();
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
                if (args.Skip(2).Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
                {
                    Apply(imported.ManifestPath);
                }
                else
                {
                    Console.WriteLine("主题尚未应用。确认后可再次使用 import ... --apply，或使用 apply <theme.json>。");
                }
                return 0;
            case "protocol":
                return ManageProtocol(args.Skip(1).ToArray());
            case "launch":
                LaunchStore();
                return 0;
            case "status":
                Status();
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
            Console.WriteLine($"{i + 1}. {Path.GetFileNameWithoutExtension(themes[i])} - {theme.DisplayName}");
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
        Console.WriteLine("  Codex-Skin.exe apply jackson-sage");
        Console.WriteLine("  Codex-Skin.exe apply kun-stage");
        Console.WriteLine("  Codex-Skin.exe apply enfp-pop");
        Console.WriteLine("  Codex-Skin.exe apply C:\\path\\theme.json");
        Console.WriteLine("  Codex-Skin.exe import C:\\path\\theme.dreamskin [--apply]");
        Console.WriteLine("  Codex-Skin.exe protocol register|unregister|status|self-test");
        Console.WriteLine("  Codex-Skin.exe launch");
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
        if (applyNow == DialogResult.Yes) Apply(imported.ManifestPath);
        return 0;
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
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)}\t{theme.DisplayName}\t{theme.Variant}");
        }
    }

    private bool Apply(string input)
    {
        var themePath = ResolveThemeInput(input);
        var theme = ThemeDefinition.Load(themePath);
        var js = JsBuilder.Build(theme.Copy, theme.Home, theme.CodeThemeId, BuildStoreLogoUrl(theme));

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
        if (!Directory.Exists(_themeDir)) yield break;
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["dilraba-star.json"] = 0,
            ["jackson-sage.json"] = 1,
            ["kun-stage.json"] = 2,
            ["enfp-pop.json"] = 3,
        };
        foreach (var file in Directory.GetFiles(_themeDir, "*.json")
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

        var named = Path.Combine(_themeDir, trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed}.json");
        if (File.Exists(named)) return named;
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

    internal static string FindThemeDir()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "themes"),
            Path.Combine(Directory.GetCurrentDirectory(), "themes"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "themes")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "themes")),
        };
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.json").Length > 0) return candidate;
        }
        throw new DirectoryNotFoundException("Cannot find themes directory.");
    }
}

internal static class StoreCodexBridge
{
    private const int DebugPort = 9229;
    private const string LiveStyleId = "codex-theme-store-live-style";
    private const string ActiveInjectionStorageKey = "__codexThemeStoreActiveInjection";
    private const string NewDocumentScriptStorageKey = "__codexThemeStoreNewDocumentScript";
    private const string InjectionIdProperty = "injectionId";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static bool IsStoreRunning()
    {
        foreach (var process in GetStoreProcesses())
        {
            process.Dispose();
            return true;
        }
        return false;
    }

    public static bool IsDebugPortReady()
    {
        try
        {
            using var response = Http.GetAsync($"http://127.0.0.1:{DebugPort}/json/version").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return false;
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("webSocketDebuggerUrl", out var urlNode) &&
                   urlNode.ValueKind == JsonValueKind.String &&
                   IsAllowedDebuggerWebSocketUrl(urlNode.GetString());
        }
        catch
        {
            return false;
        }
    }

    public static bool TryInjectSavedTheme(string stateDir, TimeSpan timeout)
    {
        var cssPath = Path.Combine(stateDir, "codex-theme.css");
        var jsPath = Path.Combine(stateDir, "codex-theme.js");
        if (!File.Exists(cssPath) || !File.Exists(jsPath)) return false;
        try
        {
            var css = File.ReadAllText(cssPath, Encoding.UTF8);
            var js = File.ReadAllText(jsPath, Encoding.UTF8);
            var injectionId = Guid.NewGuid().ToString("N");
            var expectedThemeId = TryReadSavedThemeId(stateDir);
            var themeExpression = $@"(() => {{
  let style = document.getElementById({JsonSerializer.Serialize(LiveStyleId)});
  if (!style) {{
    style = document.createElement('style');
    style.id = {JsonSerializer.Serialize(LiveStyleId)};
    const styleRoot = document.head || document.documentElement;
    if (!styleRoot) return false;
    styleRoot.appendChild(style);
  }}
  style.dataset.codexThemeStoreInjectionId = {JsonSerializer.Serialize(injectionId)};
  style.textContent = {JsonSerializer.Serialize(css)};
  {js}
  if (!globalThis.__codexThemeStore) return false;
  globalThis.__codexThemeStore[{JsonSerializer.Serialize(InjectionIdProperty)}] = {JsonSerializer.Serialize(injectionId)};
  return true;
}})()";
            var activationExpression = $@"(() => {{
  try {{
    localStorage.setItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}, {JsonSerializer.Serialize(injectionId)});
    if (localStorage.getItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}) !== {JsonSerializer.Serialize(injectionId)}) return false;
  }} catch {{
    return false;
  }}
  return {themeExpression};
}})()";
            var newDocumentExpression = $@"(() => {{
  if (window.top !== window) return;
  const applySavedTheme = () => {{
    try {{
      if (localStorage.getItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}) !== {JsonSerializer.Serialize(injectionId)}) return;
    }} catch {{
      return;
    }}
    {themeExpression};
  }};
  if (document.documentElement) applySavedTheme();
  else document.addEventListener('DOMContentLoaded', applySavedTheme, {{ once: true }});
}})()";
            var verificationExpression = $@"(() => {{
  let isActive = false;
  try {{
    isActive = localStorage.getItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}) === {JsonSerializer.Serialize(injectionId)};
  }} catch {{}}
  const style = document.getElementById({JsonSerializer.Serialize(LiveStyleId)});
  const store = globalThis.__codexThemeStore;
  const home = document.getElementById('codex-theme-home');
  const themeId = home?.dataset?.themeId || '';
  return isActive &&
    style?.id === {JsonSerializer.Serialize(LiveStyleId)} &&
    style.dataset.codexThemeStoreInjectionId === {JsonSerializer.Serialize(injectionId)} &&
    !!store && store[{JsonSerializer.Serialize(InjectionIdProperty)}] === {JsonSerializer.Serialize(injectionId)} &&
    themeId.length > 0 &&
    ({JsonSerializer.Serialize(expectedThemeId)} === null || themeId === {JsonSerializer.Serialize(expectedThemeId)});
}})()";
            return InjectOnPagesAsync(activationExpression, newDocumentExpression, verificationExpression, timeout)
                .GetAwaiter().GetResult() > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRemoveLiveTheme(TimeSpan timeout)
    {
        const string expression = @"(() => {
  try { localStorage.removeItem('__codexThemeStoreActiveInjection'); } catch {}
  globalThis.__codexThemeStore?.observer?.disconnect();
  delete globalThis.__codexThemeStore;
  document.getElementById('codex-theme-store-live-style')?.remove();
  setTimeout(() => location.reload(), 0);
  return true;
})()";
        try
        {
            return EvaluateOnPagesAsync(expression, timeout).GetAwaiter().GetResult() > 0;
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

    private static void StartStoreApp(string executable)
    {
        const string appUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        var arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={DebugPort}";
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

    private static string? TryReadSavedThemeId(string stateDir)
    {
        var metadataPath = Path.Combine(stateDir, "current-theme.json");
        if (!File.Exists(metadataPath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
            if (!document.RootElement.TryGetProperty("codeThemeId", out var themeIdNode) ||
                themeIdNode.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var themeId = themeIdNode.GetString();
            return string.IsNullOrWhiteSpace(themeId) ? null : themeId;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int> InjectOnPagesAsync(
        string activationExpression,
        string newDocumentExpression,
        string verificationExpression,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        var targets = await GetCodexPageTargetsAsync(timeoutSource.Token);
        var successes = 0;
        foreach (var target in targets)
        {
            if (await InjectAsync(
                    target,
                    activationExpression,
                    newDocumentExpression,
                    verificationExpression,
                    timeoutSource.Token))
            {
                successes++;
            }
        }
        return successes;
    }

    private static async Task<int> EvaluateOnPagesAsync(string expression, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        var targets = await GetCodexPageTargetsAsync(timeoutSource.Token);
        var successes = 0;
        foreach (var target in targets)
        {
            if (await EvaluateAsync(target, expression, timeoutSource.Token)) successes++;
        }
        return successes;
    }

    private static async Task<List<string>> GetCodexPageTargetsAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync($"http://127.0.0.1:{DebugPort}/json/list", cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var targets = new List<(int Priority, string WebSocketUrl)>();
        foreach (var target in document.RootElement.EnumerateArray())
        {
            if (!target.TryGetProperty("type", out var typeNode) ||
                !string.Equals(typeNode.GetString(), "page", StringComparison.Ordinal))
            {
                continue;
            }

            var priority = GetCodexTargetPriority(target);
            if (priority < 0 ||
                !target.TryGetProperty("webSocketDebuggerUrl", out var webSocketNode) ||
                webSocketNode.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var webSocketUrl = webSocketNode.GetString();
            if (!IsAllowedDebuggerWebSocketUrl(webSocketUrl)) continue;
            targets.Add((priority, webSocketUrl!));
        }

        return targets
            .OrderBy(target => target.Priority)
            .ThenBy(target => target.WebSocketUrl, StringComparer.Ordinal)
            .Select(target => target.WebSocketUrl)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static int GetCodexTargetPriority(JsonElement target)
    {
        var url = target.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? "" : "";
        var title = target.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? "" : "";
        var isAppRoot = url.StartsWith("app:///", StringComparison.OrdinalIgnoreCase);
        var isCodex = url.Contains("codex", StringComparison.OrdinalIgnoreCase) ||
                      title.Contains("codex", StringComparison.OrdinalIgnoreCase);
        if (isAppRoot && isCodex) return 0;
        if (isAppRoot) return 1;
        if (isCodex) return 2;
        return -1;
    }

    private static bool IsAllowedDebuggerWebSocketUrl(string? webSocketUrl)
    {
        if (string.IsNullOrWhiteSpace(webSocketUrl) ||
            !Uri.TryCreate(webSocketUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != DebugPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> InjectAsync(
        string webSocketUrl,
        string activationExpression,
        string newDocumentExpression,
        string verificationExpression,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedDebuggerWebSocketUrl(webSocketUrl)) return false;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);

        var pageEnableResponse = await SendCommandAsync(socket, 1, "Page.enable", new { }, cancellationToken);
        if (!IsSuccessfulCommand(pageEnableResponse)) return false;

        var previousScriptResponse = await SendRuntimeEvaluateAsync(
            socket,
            2,
            $@"(() => {{ try {{ return localStorage.getItem({JsonSerializer.Serialize(NewDocumentScriptStorageKey)}) || ''; }} catch {{ return ''; }} }})()",
            cancellationToken);
        if (TryGetStringEvaluation(previousScriptResponse, out var previousScriptId) &&
            !string.IsNullOrWhiteSpace(previousScriptId))
        {
            await RemoveNewDocumentScriptAsync(socket, previousScriptId, 3, cancellationToken);
        }

        var addScriptResponse = await SendCommandAsync(socket, 4, "Page.addScriptToEvaluateOnNewDocument", new
        {
            source = newDocumentExpression,
        }, cancellationToken);
        if (!IsSuccessfulCommand(addScriptResponse) ||
            !TryGetCommandResult(addScriptResponse, out var addScriptResult) ||
            !addScriptResult.TryGetProperty("identifier", out var identifierNode) ||
            identifierNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var identifier = identifierNode.GetString();
        var persistScriptResponse = await SendRuntimeEvaluateAsync(
            socket,
            5,
            $@"(() => {{ try {{ localStorage.setItem({JsonSerializer.Serialize(NewDocumentScriptStorageKey)}, {JsonSerializer.Serialize(identifier)}); return true; }} catch {{ return false; }} }})()",
            cancellationToken);
        if (!IsTrueEvaluation(persistScriptResponse))
        {
            await RemoveNewDocumentScriptAsync(socket, identifier, 6, cancellationToken);
            return false;
        }

        var activationResponse = await SendRuntimeEvaluateAsync(socket, 7, activationExpression, cancellationToken);
        if (!IsSuccessfulEvaluation(activationResponse))
        {
            await RemoveNewDocumentScriptAsync(socket, identifier, 8, cancellationToken);
            return false;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var verificationResponse = await SendRuntimeEvaluateAsync(socket, 10 + attempt, verificationExpression, cancellationToken);
            if (IsTrueEvaluation(verificationResponse)) return true;
            if (attempt < 4) await Task.Delay(50, cancellationToken);
        }

        await RemoveNewDocumentScriptAsync(socket, identifier, 20, cancellationToken);
        return false;
    }

    private static async Task<bool> EvaluateAsync(string webSocketUrl, string expression, CancellationToken cancellationToken)
    {
        if (!IsAllowedDebuggerWebSocketUrl(webSocketUrl)) return false;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);
        var response = await SendRuntimeEvaluateAsync(socket, 1, expression, cancellationToken);
        return IsSuccessfulEvaluation(response);
    }

    private static Task<JsonElement?> SendRuntimeEvaluateAsync(
        ClientWebSocket socket,
        int id,
        string expression,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(socket, id, "Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true,
        }, cancellationToken);
    }

    private static async Task RemoveNewDocumentScriptAsync(
        ClientWebSocket socket,
        string? identifier,
        int id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return;
        try
        {
            await SendCommandAsync(socket, id, "Page.removeScriptToEvaluateOnNewDocument", new
            {
                identifier,
            }, cancellationToken);
        }
        catch
        {
            // The target may have closed while a failed injection was being cleaned up.
        }
    }

    private static async Task<JsonElement?> SendCommandAsync(
        ClientWebSocket socket,
        int id,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id,
            method,
            @params = parameters,
        });
        await socket.SendAsync(request, WebSocketMessageType.Text, true, cancellationToken);

        var buffer = new byte[64 * 1024];
        using var response = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
            {
                if (result.EndOfMessage) response.SetLength(0);
                continue;
            }

            response.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            using var document = JsonDocument.Parse(response.ToArray());
            if (!document.RootElement.TryGetProperty("id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.Number ||
                idNode.GetInt32() != id)
            {
                response.SetLength(0);
                continue;
            }
            return document.RootElement.Clone();
        }
    }

    private static bool IsSuccessfulCommand(JsonElement? response)
    {
        return response is { } value && !value.TryGetProperty("error", out _);
    }

    private static bool TryGetCommandResult(JsonElement? response, out JsonElement result)
    {
        result = default;
        return IsSuccessfulCommand(response) && response!.Value.TryGetProperty("result", out result);
    }

    private static bool IsSuccessfulEvaluation(JsonElement? response)
    {
        return TryGetCommandResult(response, out var evaluation) &&
               !evaluation.TryGetProperty("exceptionDetails", out _) &&
               evaluation.TryGetProperty("result", out _);
    }

    private static bool IsTrueEvaluation(JsonElement? response)
    {
        if (!IsSuccessfulEvaluation(response) ||
            !TryGetCommandResult(response, out var evaluation) ||
            !evaluation.TryGetProperty("result", out var remoteResult) ||
            !remoteResult.TryGetProperty("value", out var value))
        {
            return false;
        }
        return value.ValueKind == JsonValueKind.True;
    }

    private static bool TryGetStringEvaluation(JsonElement? response, out string? value)
    {
        value = null;
        if (!IsSuccessfulEvaluation(response) ||
            !TryGetCommandResult(response, out var evaluation) ||
            !evaluation.TryGetProperty("result", out var remoteResult) ||
            !remoteResult.TryGetProperty("value", out var valueNode) ||
            valueNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = valueNode.GetString();
        return true;
    }
}

internal sealed class ThemeDefinition
{
    private readonly JsonNode _root;

    private ThemeDefinition(JsonNode root, string rawJson, string sourcePath)
    {
        _root = root;
        RawJson = rawJson;
        SourcePath = sourcePath;
    }

    public string RawJson { get; }
    public string SourcePath { get; }
    public JsonNode? Copy => _root["copy"];
    public JsonNode? Home => _root["home"] ?? BuildDreamSkinHome();
    public string DisplayName => GetString("displayName", GetString("name", GetString("codeThemeId", "theme")));
    public string CodeThemeId => GetString("codeThemeId", GetString("id", "absolutely"));
    public string Variant
    {
        get
        {
            if (_root["variant"] is not null)
            {
                return GetString("variant", "light").Equals("dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
            }
            return ColorUtil.Luminance(GetStoreColor("background", "#f5f4ee")) < 0.35 ? "dark" : "light";
        }
    }
    public string Accent => GetThemeString("accent", GetStoreColor("accent", "#da7756"));
    public string Ink => GetThemeString("ink", GetStoreColor("text", "#141413"));
    public string Surface => GetThemeString("surface", GetStoreColor("panel", GetStoreColor("background", "#f5f4ee")));
    public string DiffAdded => GetSemanticString("diffAdded", GetStoreColor("highlight", "#00c853"));
    public string DiffRemoved => GetSemanticString("diffRemoved", GetStoreColor("accentAlt", "#ff5f38"));
    public string Skill => GetSemanticString("skill", GetStoreColor("secondary", "#cc7d5e"));
    public string UiFont => GetFontString("ui", "ui-serif, Georgia, Cambria, \"Times New Roman\", Times, \"Noto Serif SC\", serif");
    public string DisplayFont => GetFontString("display", "ui-serif, Georgia, Cambria, \"Times New Roman\", Times, \"Noto Serif SC\", serif");
    public string CodeFont => GetFontString("code", "JetBrainsMono NFM");
    public bool OpaqueWindows => GetThemeBool("opaqueWindows", true);
    public double Contrast => Clamp(GetThemeDouble("contrast", 45), 0, 100);
    public string? BackgroundImage => GetThemeNullableString("backgroundImage") ?? _root["image"]?.GetValue<string>();
    public string? LogoImage => GetThemeNullableString("logoImage");
    public string? PreviewImage => _root["previewImage"]?.GetValue<string>() ?? _root["assets"]?["preview"]?["path"]?.GetValue<string>();
    public double BackgroundImageOpacity => Clamp(GetThemeDouble("backgroundImageOpacity", 0.18), 0, 1);
    public double BackgroundImageBlur => Clamp(GetThemeDouble("backgroundImageBlur", 0), 0, 24);

    public string ResolveBackgroundImage()
    {
        return ResolveAsset(BackgroundImage);
    }

    public string ResolveLogoImage()
    {
        return ResolveAsset(LogoImage);
    }

    public string ResolvePreviewImage()
    {
        return ResolveAsset(PreviewImage ?? BackgroundImage);
    }

    private string ResolveAsset(string? value)
    {
        var source = value?.Trim() ?? "";
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }
        if (Path.IsPathRooted(source)) return Path.GetFullPath(source);
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath)!, source));
    }

    public static ThemeDefinition Load(string path)
    {
        var raw = File.ReadAllText(path, Encoding.UTF8);
        var root = JsonNode.Parse(raw) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
        return new ThemeDefinition(root, raw, Path.GetFullPath(path));
    }

    private string GetString(string key, string fallback) => _root[key]?.GetValue<string>() ?? fallback;
    private string GetThemeString(string key, string fallback) => _root["theme"]?[key]?.GetValue<string>() ?? fallback;
    private string GetStoreColor(string key, string fallback)
    {
        var value = _root["colors"]?[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) return value;
        var rgba = Regex.Match(value, "^rgba\\((\\d{1,3}),[ ]*(\\d{1,3}),[ ]*(\\d{1,3}),[ ]*(?:0|1|0?\\.\\d{1,3}|1\\.0{1,3})\\)$");
        if (!rgba.Success) return fallback;
        return $"#{int.Parse(rgba.Groups[1].Value):x2}{int.Parse(rgba.Groups[2].Value):x2}{int.Parse(rgba.Groups[3].Value):x2}";
    }
    private JsonNode? BuildDreamSkinHome()
    {
        if (_root["packageVersion"] is null) return null;
        return new JsonObject
        {
            ["brand"] = GetString("name", CodeThemeId),
            ["eyebrow"] = GetString("brandSubtitle", "Dream Skin"),
            ["badge"] = GetString("statusText", "已验证主题"),
            ["title"] = GetString("tagline", "我们该构建什么？"),
            ["subtitle"] = GetString("description", ""),
            ["sidebarProject"] = GetString("projectLabel", GetString("name", CodeThemeId)),
            ["footerNote"] = GetString("quote", GetString("brandSubtitle", "Dream Skin")),
            ["quickActions"] = new JsonArray(),
            ["tags"] = new JsonArray(),
            ["sidebarItems"] = new JsonArray(),
            ["tasks"] = new JsonArray(),
        };
    }
    private string? GetThemeNullableString(string key)
    {
        var node = _root["theme"]?[key];
        if (node is null) return null;
        return node.GetValueKind() == JsonValueKind.Null ? null : node.GetValue<string>();
    }
    private string GetFontString(string key, string fallback) => _root["theme"]?["fonts"]?[key]?.GetValue<string>() ?? fallback;
    private string GetSemanticString(string key, string fallback) => _root["theme"]?["semanticColors"]?[key]?.GetValue<string>() ?? fallback;
    private bool GetThemeBool(string key, bool fallback) => _root["theme"]?[key]?.GetValue<bool>() ?? fallback;
    private double GetThemeDouble(string key, double fallback) => _root["theme"]?[key]?.GetValue<double>() ?? fallback;
    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);
}

internal static class CssBuilder
{
    public static string Build(ThemeDefinition theme, string? backgroundUrl)
    {
        var dark = theme.Variant == "dark" || ColorUtil.Luminance(theme.Surface) < 0.35;
        var contrast = theme.Contrast / 100.0;
        var baseMix = dark ? "#000000" : "#ffffff";
        var under = ColorUtil.Mix(theme.Surface, dark ? "#000000" : "#d9d4c6", dark ? 0.18 : 0.22);
        var elevated = ColorUtil.Mix(theme.Surface, baseMix, dark ? 0.10 : 0.44);
        var elevated2 = ColorUtil.Mix(theme.Surface, baseMix, dark ? 0.16 : 0.64);
        var softAccent = ColorUtil.Mix(theme.Surface, theme.Accent, dark ? 0.22 : 0.12);
        var activeAccent = ColorUtil.Mix(theme.Accent, dark ? "#ffffff" : "#000000", dark ? 0.18 : 0.08);
        var secondaryText = ColorUtil.Alpha(theme.Ink, dark ? 0.80 : 0.76);
        var tertiaryText = ColorUtil.Alpha(theme.Ink, dark ? 0.60 : 0.56);
        var border = ColorUtil.Alpha(theme.Ink, dark ? 0.20 + contrast * 0.10 : 0.12 + contrast * 0.08);
        var borderHeavy = ColorUtil.Alpha(theme.Ink, dark ? 0.30 + contrast * 0.12 : 0.18 + contrast * 0.10);
        var hover = ColorUtil.Alpha(theme.Accent, dark ? 0.20 : 0.12);
        var buttonText = ColorUtil.ReadableText(theme.Accent);
        var dangerBg = ColorUtil.Mix(theme.Surface, theme.DiffRemoved, dark ? 0.20 : 0.11);
        var successBg = ColorUtil.Mix(theme.Surface, theme.DiffAdded, dark ? 0.17 : 0.10);

        var backgroundCss = "";
        if (!string.IsNullOrWhiteSpace(backgroundUrl))
        {
            backgroundCss = $@"
:root {{
  --codex-theme-background-image: url(""{CssEscape(backgroundUrl)}"");
}}

html[data-codex-window-type=""electron""] .main-surface,
html[data-codex-window-type=""electron""] .browser-main-surface {{
  position: relative;
}}

html[data-codex-window-type=""electron""] .main-surface::before,
html[data-codex-window-type=""electron""] .browser-main-surface::before {{
  content: """";
  position: absolute;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background-image: var(--codex-theme-background-image);
  background-size: cover;
  background-position: center top;
  background-repeat: no-repeat;
  opacity: {CssNumber(theme.BackgroundImageOpacity)};
  -webkit-mask-image: linear-gradient(to bottom, #000 0%, #000 50%, rgba(0, 0, 0, 0.82) 64%, rgba(0, 0, 0, 0.34) 82%, transparent 100%);
  mask-image: linear-gradient(to bottom, #000 0%, #000 50%, rgba(0, 0, 0, 0.82) 64%, rgba(0, 0, 0, 0.34) 82%, transparent 100%);
  -webkit-mask-size: 100% 100%;
  mask-size: 100% 100%;
  filter: blur({CssNumber(theme.BackgroundImageBlur)}px) saturate(1.08) contrast(1.02);
  transform: {(theme.BackgroundImageBlur > 0 ? "scale(1.03)" : "none")};
  transition: opacity 180ms ease;
}}

html[data-codex-window-type=""electron""] .main-surface > *,
html[data-codex-window-type=""electron""] .browser-main-surface > * {{
  position: relative;
  z-index: 1;
}}

html[data-codex-window-type=""electron""] .main-surface.codex-theme-home-active::before,
html[data-codex-window-type=""electron""] .browser-main-surface.codex-theme-home-active::before {{
  opacity: 0;
}}
";
        }

        var baseCss = $@"/* Generated by Codex-Skin.exe. */
:root {{
  --codex-theme-id: ""{CssEscape(theme.CodeThemeId)}"";
  --codex-theme-variant: {theme.Variant};
  --codex-theme-accent: {theme.Accent};
  --codex-theme-surface: {theme.Surface};
  --codex-theme-ink: {theme.Ink};
  --codex-theme-skill: {theme.Skill};
}}

:root,
html[data-codex-window-type=""electron""],
html[data-codex-window-type=""electron""] body {{
  color-scheme: {(dark ? "dark" : "light")};
  --vscode-font-family: {theme.UiFont};
  --vscode-editor-font-family: {QuoteFont(theme.CodeFont)}, ui-monospace, ""SFMono-Regular"", ""SF Mono"", Menlo, Consolas, ""Liberation Mono"", monospace;
  --font-sans-default: {theme.UiFont};
  --font-mono-default: {QuoteFont(theme.CodeFont)}, ui-monospace, ""SFMono-Regular"", ""SF Mono"", Menlo, Consolas, ""Liberation Mono"", monospace;

  --color-background-surface: {theme.Surface};
  --color-background-surface-under: {under};
  --color-background-panel: {ColorUtil.Mix(theme.Surface, under, 0.46)};
  --color-background-elevated-primary: {elevated};
  --color-background-elevated-primary-opaque: {elevated};
  --color-background-elevated-secondary: {elevated2};
  --color-background-elevated-secondary-opaque: {elevated2};
  --color-background-control: {softAccent};
  --color-background-control-opaque: {softAccent};
  --color-background-accent: {softAccent};
  --color-background-accent-hover: {hover};
  --color-background-accent-active: {ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)};
  --color-background-button-primary: {theme.Accent};
  --color-background-button-primary-hover: {activeAccent};
  --color-background-button-primary-active: {ColorUtil.Mix(theme.Accent, "#000000", dark ? 0.05 : 0.16)};
  --color-background-button-secondary: {elevated2};
  --color-background-button-secondary-hover: {ColorUtil.Mix(elevated2, theme.Accent, dark ? 0.16 : 0.08)};
  --color-background-button-tertiary-hover: {hover};
  --color-background-status-error: {dangerBg};
  --color-background-status-success: {successBg};

  --color-text-foreground: {theme.Ink};
  --color-text-foreground-secondary: {secondaryText};
  --color-text-foreground-tertiary: {tertiaryText};
  --color-text-accent: {theme.Accent};
  --color-text-button-primary: {buttonText};
  --color-text-button-secondary: {theme.Ink};
  --color-text-button-tertiary: {theme.Ink};
  --color-text-error: {theme.DiffRemoved};
  --color-text-success: {theme.DiffAdded};
  --color-border: {border};
  --color-border-light: {ColorUtil.Alpha(theme.Ink, dark ? 0.12 : 0.07)};
  --color-border-heavy: {borderHeavy};
  --color-border-focus: {theme.Accent};
  --color-accent-blue: {theme.Accent};
  --color-accent-green: {theme.DiffAdded};
  --color-accent-orange: {theme.DiffRemoved};
  --color-accent-purple: {theme.Skill};
  --color-accent-red: {theme.DiffRemoved};
  --color-accent-yellow: #c59b21;
  --color-icon-primary: {theme.Ink};
  --color-icon-secondary: {secondaryText};
  --color-icon-tertiary: {tertiaryText};
  --color-icon-accent: {theme.Accent};
  --color-icon-error: {theme.DiffRemoved};
  --color-icon-success: {theme.DiffAdded};

  --color-decoration-added: {theme.DiffAdded};
  --color-decoration-deleted: {theme.DiffRemoved};
  --color-decoration-modified: {theme.Accent};
  --color-decoration-unchanged: {tertiaryText};
  --color-editor-added: {successBg};
  --color-editor-deleted: {dangerBg};

  --vscode-foreground: {theme.Ink};
  --vscode-disabledForeground: {tertiaryText};
  --vscode-descriptionForeground: {secondaryText};
  --vscode-errorForeground: {theme.DiffRemoved};
  --vscode-icon-foreground: {theme.Ink};
  --vscode-focusBorder: {theme.Accent};
  --vscode-textLink-foreground: {theme.Accent};
  --vscode-textLink-activeForeground: {activeAccent};
  --vscode-editor-background: {theme.Surface};
  --vscode-editor-foreground: {theme.Ink};
  --vscode-editorCursor-foreground: {theme.Accent};
  --vscode-sideBar-background: {under};
  --vscode-sideBar-foreground: {theme.Ink};
  --vscode-sideBarTitle-foreground: {theme.Ink};
  --vscode-panel-background: {under};
  --vscode-activityBar-background: {under};
  --vscode-activityBar-activeBorder: {theme.Accent};
  --vscode-activityBarBadge-background: {theme.Accent};
  --vscode-activityBarBadge-foreground: {buttonText};
  --vscode-badge-background: {softAccent};
  --vscode-badge-foreground: {theme.Ink};
  --vscode-input-background: {elevated2};
  --vscode-input-foreground: {theme.Ink};
  --vscode-input-placeholderForeground: {tertiaryText};
  --vscode-input-border: {border};
  --vscode-dropdown-background: {elevated2};
  --vscode-button-background: {theme.Accent};
  --vscode-button-foreground: {buttonText};
  --vscode-button-secondaryBackground: {elevated2};
  --vscode-button-secondaryForeground: {theme.Ink};
  --vscode-button-secondaryHoverBackground: {ColorUtil.Mix(elevated2, theme.Accent, dark ? 0.16 : 0.08)};
  --vscode-list-hoverBackground: {hover};
  --vscode-list-activeSelectionBackground: {softAccent};
  --vscode-list-activeSelectionForeground: {theme.Ink};
  --vscode-list-activeSelectionIconForeground: {theme.Accent};
  --vscode-list-focusOutline: {theme.Accent};
  --vscode-menu-background: {elevated2};
  --vscode-menu-border: {border};
  --vscode-menubar-selectionBackground: {hover};
  --vscode-menubar-selectionForeground: {theme.Ink};
  --vscode-scrollbarSlider-background: {ColorUtil.Alpha(theme.Ink, dark ? 0.22 : 0.16)};
  --vscode-scrollbarSlider-hoverBackground: {ColorUtil.Alpha(theme.Accent, dark ? 0.34 : 0.26)};
  --vscode-scrollbarSlider-activeBackground: {ColorUtil.Alpha(theme.Accent, dark ? 0.46 : 0.38)};
  --vscode-progressBar-background: {theme.Accent};
  --vscode-gitDecoration-addedResourceForeground: {theme.DiffAdded};
  --vscode-gitDecoration-deletedResourceForeground: {theme.DiffRemoved};
  --vscode-gitDecoration-modifiedResourceForeground: {theme.Accent};
  --vscode-terminal-background: {theme.Surface};
  --vscode-terminal-foreground: {theme.Ink};
}}

html[data-codex-window-type=""electron""].electron-opaque,
html[data-codex-window-type=""electron""].electron-opaque body {{
  background-color: {(theme.OpaqueWindows ? under : "transparent")} !important;
  background-image: none !important;
}}

html[data-codex-window-type=""electron""] .main-surface,
html[data-codex-window-type=""electron""] .browser-main-surface,
.main-surface,
.browser-main-surface {{
  background-color: {ColorUtil.Alpha(theme.Surface, dark ? 0.66 : 0.58)} !important;
  backdrop-filter: blur({(dark ? 8 : 5)}px) saturate(1.04) !important;
  box-shadow: 0 0 0 1px {ColorUtil.Alpha(theme.Accent, dark ? 0.32 : 0.24)}, 0 18px 48px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.12)} !important;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel,
.app-shell-left-panel {{
  background: {ColorUtil.Alpha(ColorUtil.Mix(under, theme.Surface, dark ? 0.20 : 0.28), dark ? 0.88 : 0.84)} !important;
  backdrop-filter: blur(18px) saturate(1.08) !important;
  border-right: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)} !important;
}}

{backgroundCss}

html[data-codex-window-type=""electron""] #codex-theme-home {{
  display: none;
  position: absolute !important;
  inset: 46px 0 138px;
  z-index: 6 !important;
  overflow-y: auto;
  padding: 24px 32px 28px;
  background: {theme.Surface};
}}

html[data-codex-window-type=""electron""] .codex-theme-home-active #codex-theme-home {{
  display: block;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-shell {{
  width: min(1480px, 100%);
  margin: 0 auto;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-hero {{
  min-height: 300px;
  display: flex;
  align-items: center;
  padding: 38px 42px;
  overflow: hidden;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.44 : 0.28)};
  border-radius: 8px;
  background-color: {theme.Surface};
  background-image: linear-gradient(90deg, {theme.Surface} 0%, {theme.Surface} 42%, {ColorUtil.Alpha(theme.Surface, 0.92)} 54%, {ColorUtil.Alpha(theme.Surface, 0.24)} 72%, transparent 100%), var(--codex-theme-background-image);
  background-size: 100% 100%, auto 100%;
  background-position: center, right center;
  background-repeat: no-repeat;
  box-shadow: 0 18px 44px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.10)};
}}

html[data-codex-window-type=""electron""] .codex-theme-home-copy {{
  width: min(560px, 48%);
  position: relative;
  z-index: 1;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-brand {{
  margin: 0 0 6px;
  color: {theme.Accent};
  font-size: 36px;
  line-height: 1.15;
  font-weight: 800;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-eyebrow {{
  margin: 0 0 20px;
  color: {secondaryText};
  font-size: 14px;
  font-weight: 600;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-title {{
  margin: 0 0 12px;
  color: {theme.Ink};
  font-size: 32px;
  line-height: 1.25;
  font-weight: 800;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-subtitle {{
  margin: 0;
  color: {secondaryText};
  font-size: 16px;
  line-height: 1.6;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-actions {{
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(280px, 100%), 1fr));
  gap: 14px;
  margin-top: 18px;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action {{
  min-height: 142px;
  display: grid;
  grid-template-columns: 48px minmax(0, 1fr);
  grid-template-rows: auto 1fr;
  column-gap: 12px;
  row-gap: 6px;
  padding: 18px;
  text-align: left;
  color: {theme.Ink};
  background: {elevated2};
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.34 : 0.22)};
  border-radius: 8px;
  box-shadow: 0 10px 26px {ColorUtil.Alpha(theme.Ink, dark ? 0.18 : 0.08)};
  transition: transform 140ms ease, border-color 140ms ease, box-shadow 140ms ease;
  min-width: 0;
  overflow: hidden;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action:hover {{
  transform: translateY(-2px);
  border-color: {ColorUtil.Alpha(theme.Accent, 0.72)};
  box-shadow: 0 14px 32px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.12)};
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action-icon {{
  grid-row: 1 / span 2;
  width: 46px;
  height: 46px;
  display: grid;
  place-items: center;
  color: {buttonText};
  background: {theme.Accent};
  border-radius: 50%;
  font-family: var(--font-mono-default);
  font-size: 17px;
  font-weight: 800;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action-title {{
  align-self: end;
  font-size: 15px;
  line-height: 1.35;
  font-weight: 700;
  min-width: 0;
  overflow-wrap: anywhere;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action-description {{
  color: {secondaryText};
  font-size: 12px;
  line-height: 1.5;
  min-width: 0;
  overflow-wrap: anywhere;
}}

@media (max-width: 1120px) {{
  html[data-codex-window-type=""electron""] #codex-theme-home {{ padding-inline: 22px; }}
  html[data-codex-window-type=""electron""] .codex-theme-home-hero {{ min-height: 280px; padding: 30px; }}
  html[data-codex-window-type=""electron""] .codex-theme-home-title {{ font-size: 28px; }}
}}

@media (max-width: 720px) {{
  html[data-codex-window-type=""electron""] .codex-theme-home-copy {{ width: 72%; }}
}}

html[data-codex-window-type=""electron""] .composer-surface-chrome {{
  background: {ColorUtil.Alpha(elevated2, dark ? 0.90 : 0.88)} !important;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.68 : 0.52)} !important;
  border-radius: 20px !important;
  backdrop-filter: blur(22px) saturate(1.16) !important;
  box-shadow: 0 0 0 1px {ColorUtil.Alpha(theme.Accent, dark ? 0.16 : 0.10)}, 0 12px 34px {ColorUtil.Alpha(theme.Ink, dark ? 0.32 : 0.16)} !important;
}}

html[data-codex-window-type=""electron""] .app-header-tint {{
  background: {ColorUtil.Alpha(under, dark ? 0.94 : 0.92)} !important;
  border-bottom: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.30 : 0.18)} !important;
  backdrop-filter: blur(20px) saturate(1.08) !important;
  box-shadow: 0 4px 18px {ColorUtil.Alpha(theme.Ink, dark ? 0.20 : 0.08)} !important;
}}

html[data-codex-window-type=""electron""] [data-content-search-unit-key$="":assistant""] {{
  margin-block: 4px 10px !important;
  padding: 14px 16px !important;
  background: {ColorUtil.Alpha(theme.Surface, dark ? 0.76 : 0.72)} !important;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.30 : 0.18)} !important;
  border-radius: 16px !important;
  backdrop-filter: blur(16px) saturate(1.08) !important;
  box-shadow: 0 10px 28px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.11)} !important;
}}

html[data-codex-window-type=""electron""] [data-user-message-bubble=""true""] {{
  background: {ColorUtil.Alpha(softAccent, dark ? 0.88 : 0.84)} !important;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.42 : 0.30)} !important;
  box-shadow: 0 8px 24px {ColorUtil.Alpha(theme.Ink, dark ? 0.20 : 0.10)} !important;
  backdrop-filter: blur(14px) saturate(1.10) !important;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel .absolute.bottom-0.z-20[class*=""inset-x-0""] {{
  background: linear-gradient(to bottom, {ColorUtil.Alpha(under, 0.18)}, {ColorUtil.Alpha(under, dark ? 0.96 : 0.92)} 38%) !important;
  border-top: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)} !important;
  backdrop-filter: blur(18px) saturate(1.08) !important;
}}

html[data-codex-window-type=""electron""] [aria-label=""打开设置""] {{
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.24 : 0.16)} !important;
  background: {ColorUtil.Alpha(softAccent, dark ? 0.38 : 0.48)} !important;
}}

html[data-codex-window-type=""electron""] button,
html[data-codex-window-type=""electron""] input,
html[data-codex-window-type=""electron""] textarea {{
  font-family: var(--vscode-font-family);
}}

html[data-codex-window-type=""electron""] ::selection {{
  background: {ColorUtil.Alpha(theme.Accent, dark ? 0.42 : 0.28)};
}}
";
        return baseCss + BuildLayoutCss(theme, dark, under, elevated2, softAccent, secondaryText, tertiaryText, buttonText);
    }

    private static string BuildLayoutCss(
        ThemeDefinition theme,
        bool dark,
        string under,
        string elevated2,
        string softAccent,
        string secondaryText,
        string tertiaryText,
        string buttonText)
    {
        var sidebarSurface = ColorUtil.Mix(under, dark ? "#000000" : "#ffffff", dark ? 0.08 : 0.34);
        var homeSurface = ColorUtil.Mix(theme.Surface, dark ? "#000000" : "#ffffff", dark ? 0.04 : 0.16);
        var cardSurface = ColorUtil.Alpha(elevated2, dark ? 0.92 : 0.94);
        var heroText = theme.CodeThemeId == "dilraba-star" ? "#ffffff" : theme.Ink;
        var heroSecondary = theme.CodeThemeId == "dilraba-star" ? "rgb(255 255 255 / 0.84)" : secondaryText;
        var heroShadow = theme.CodeThemeId is "kun-stage" or "dilraba-star"
            ? "0 2px 18px rgb(0 0 0 / 0.48)"
            : "0 2px 14px rgb(255 255 255 / 0.72)";
        var actionOne = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.DiffRemoved, dark ? 0.18 : 0.09), dark ? 0.94 : 0.96);
        var actionTwo = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.DiffAdded, dark ? 0.16 : 0.08), dark ? 0.94 : 0.96);
        var actionThree = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.Accent, dark ? 0.19 : 0.08), dark ? 0.94 : 0.96);
        var actionFour = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.Skill, dark ? 0.16 : 0.07), dark ? 0.94 : 0.96);
        var cardRadius = theme.CodeThemeId == "enfp-pop" ? 26 : theme.CodeThemeId == "kun-stage" ? 22 : 24;
        var heroRadius = theme.CodeThemeId == "enfp-pop" ? 28 : 30;

        return $$"""

/* High-fidelity theme shell, injected over the live Codex surfaces. */
html[data-codex-window-type="electron"] {
  --codex-theme-sidebar-width: clamp(244px, 20.4vw, 326px);
  --codex-theme-card-radius: {{cardRadius}}px;
  --codex-theme-hero-radius: {{heroRadius}}px;
}

html[data-codex-window-type="electron"] #codex-theme-sidebar {
  position: absolute;
  inset: 0;
  z-index: 50;
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding: 20px 16px 12px;
  color: {{theme.Ink}};
  background: {{ColorUtil.Alpha(sidebarSurface, dark ? 0.97 : 0.95)}};
  border-right: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.34 : 0.20)}};
  backdrop-filter: blur(24px) saturate(1.08);
  font-family: {{theme.UiFont}};
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-head,
html[data-codex-window-type="electron"] .codex-theme-sidebar-profile,
html[data-codex-window-type="electron"] .codex-theme-masthead {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-logo {
  min-width: 0;
  min-height: 46px;
  display: flex;
  flex: 1;
  align-items: center;
  padding: 0 6px 8px;
  color: {{theme.Ink}};
  font-family: {{theme.DisplayFont}};
  font-size: 28px;
  line-height: 1;
  font-weight: 800;
  letter-spacing: -0.04em;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-logo-image {
  width: auto;
  max-width: min(158px, 100%);
  height: 38px;
  display: block;
  object-fit: contain;
  object-position: left center;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-theme {
  max-width: 48%;
  overflow: hidden;
  color: {{theme.Accent}};
  font-size: 11px;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-scroll {
  min-height: 0;
  flex: 1;
  overflow: auto;
  padding: 4px 2px 12px;
  scrollbar-width: thin;
  scrollbar-color: {{ColorUtil.Alpha(theme.Accent, 0.44)}} transparent;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-nav {
  display: grid;
  gap: 5px;
  margin-bottom: 16px;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-button,
html[data-codex-window-type="electron"] .codex-theme-sidebar-row,
html[data-codex-window-type="electron"] .codex-theme-sidebar-progress,
html[data-codex-window-type="electron"] .codex-theme-sidebar-profile {
  width: 100%;
  border: 0;
  color: inherit;
  font: inherit;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-button,
html[data-codex-window-type="electron"] .codex-theme-sidebar-row {
  min-height: 40px;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  text-align: left;
  background: transparent;
  border-radius: 10px;
  cursor: pointer;
  transition: background-color 140ms ease, color 140ms ease, transform 140ms ease;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-button {
  font-size: 15px;
  line-height: 1.4;
  font-weight: 650;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-button:hover,
html[data-codex-window-type="electron"] .codex-theme-sidebar-row:hover,
html[data-codex-window-type="electron"] .codex-theme-sidebar-button:focus-visible,
html[data-codex-window-type="electron"] .codex-theme-sidebar-row:focus-visible {
  color: {{theme.Accent}};
  background: {{ColorUtil.Alpha(softAccent, dark ? 0.48 : 0.72)}};
  outline: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.48)}};
  outline-offset: -1px;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-button:active,
html[data-codex-window-type="electron"] .codex-theme-sidebar-row:active {
  transform: scale(0.985);
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-icon,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon {
  flex: 0 0 auto;
  display: grid;
  place-items: center;
  color: {{theme.Accent}};
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-icon[hidden],
html[data-codex-window-type="electron"] .codex-theme-home-action-icon[hidden] {
  display: none !important;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-icon {
  width: 22px;
  height: 22px;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-icon svg,
html[data-codex-window-type="electron"] .codex-theme-sidebar-icon img,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon svg,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon img {
  width: 100%;
  height: 100%;
  object-fit: contain;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-icon svg,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon svg {
  stroke: currentColor;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-section {
  margin-top: 16px;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-section-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0 7px 6px;
  color: {{theme.Accent}};
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 0.08em;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-section-title::after {
  content: "";
  height: 1px;
  flex: 1;
  border-top: 1px dashed {{ColorUtil.Alpha(theme.Accent, dark ? 0.40 : 0.30)}};
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-project {
  margin: 0 9px 4px;
  font-size: 15px;
  font-weight: 800;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-row {
  min-height: 34px;
  padding-block: 6px;
  color: {{secondaryText}};
  font-size: 14.25px;
  line-height: 1.45;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-task-icon {
  width: 18px;
  height: 18px;
  padding: 3px;
  color: {{buttonText}};
  background: {{theme.Accent}};
  border-radius: 50%;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-progress {
  margin-top: 10px;
  padding: 10px 11px;
  background: {{ColorUtil.Alpha(softAccent, dark ? 0.52 : 0.76)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.48 : 0.34)}};
  border-radius: 12px;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-progress-copy {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  color: {{theme.Ink}};
  font-size: 12px;
  font-weight: 700;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-progress-track {
  height: 4px;
  margin-top: 8px;
  overflow: hidden;
  background: {{ColorUtil.Alpha(theme.Ink, dark ? 0.16 : 0.10)}};
  border-radius: 999px;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-progress-value {
  width: 72%;
  height: 100%;
  background: {{theme.Accent}};
  border-radius: inherit;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-profile {
  min-height: 54px;
  gap: 8px;
  padding: 10px 8px 0;
  background: transparent;
  border-top: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)}};
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-profile-name {
  min-width: 0;
  flex: 1;
  overflow: hidden;
  text-align: left;
  font-size: 13px;
  font-weight: 750;
  text-overflow: ellipsis;
  white-space: nowrap;
}

html[data-codex-window-type="electron"] .codex-theme-sidebar-profile-label {
  max-width: 48%;
  overflow: hidden;
  padding: 4px 8px;
  color: {{theme.Accent}};
  background: {{ColorUtil.Alpha(softAccent, dark ? 0.52 : 0.76)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.26)}};
  border-radius: 999px;
  font-size: 10px;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

html[data-codex-window-type="electron"] #codex-theme-home {
  inset: 44px 0 150px;
  padding: 16px 28px 30px;
  color: {{theme.Ink}};
  background: {{homeSurface}};
  font-family: {{theme.UiFont}};
  scrollbar-width: thin;
  scrollbar-color: {{ColorUtil.Alpha(theme.Accent, 0.46)}} transparent;
}

html[data-codex-window-type="electron"] .codex-theme-home-shell {
  width: min(1440px, 100%);
}

html[data-codex-window-type="electron"] .codex-theme-masthead {
  min-height: 68px;
  gap: 18px;
  padding: 4px 10px 14px;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-title {
  color: {{theme.Ink}};
  font-family: {{theme.DisplayFont}};
  font-size: 18px;
  line-height: 1.25;
  font-weight: 850;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-note {
  margin-top: 4px;
  color: {{secondaryText}};
  font-size: 12px;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-badge {
  max-width: 38%;
  padding: 8px 13px;
  overflow: hidden;
  color: {{theme.Accent}};
  background: {{ColorUtil.Alpha(softAccent, dark ? 0.48 : 0.74)}};
  border: 1px dashed {{ColorUtil.Alpha(theme.Accent, 0.64)}};
  border-radius: 999px;
  font-size: 12px;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

html[data-codex-window-type="electron"] .codex-theme-home-hero {
  position: relative;
  min-height: 390px;
  height: clamp(390px, 48vh, 520px);
  display: block;
  padding: 0;
  overflow: hidden;
  isolation: isolate;
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.66 : 0.40)}};
  border-radius: var(--codex-theme-hero-radius);
  background-color: {{theme.Surface}};
  background-image: var(--codex-theme-background-image);
  background-size: cover;
  background-position: center 30%;
  background-repeat: no-repeat;
  box-shadow: 0 24px 58px {{ColorUtil.Alpha(theme.Ink, dark ? 0.34 : 0.15)}};
}

html[data-codex-window-type="electron"] .codex-theme-home-copy {
  position: absolute;
  inset: 46% auto auto 38px;
  z-index: 2;
  width: min(43%, 560px);
  transform: translateY(-14%);
  color: {{heroText}};
  text-shadow: {{heroShadow}};
}

html[data-codex-window-type="electron"] .codex-theme-home-brand {
  display: none;
}

html[data-codex-window-type="electron"] .codex-theme-home-eyebrow {
  display: inline-flex;
  margin: 0 0 12px;
  padding: 6px 10px;
  color: {{heroText}};
  background: {{ColorUtil.Alpha(theme.Surface, dark ? 0.62 : 0.72)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.58)}};
  border-radius: 999px;
  backdrop-filter: blur(12px);
  font-size: 11px;
  letter-spacing: 0.08em;
}

html[data-codex-window-type="electron"] .codex-theme-home-title {
  margin: 0 0 10px;
  color: inherit;
  font-family: {{theme.DisplayFont}};
  font-size: clamp(28px, 2.8vw, 44px);
  line-height: 1.16;
  font-weight: 900;
  letter-spacing: -0.035em;
}

html[data-codex-window-type="electron"] .codex-theme-home-subtitle {
  max-width: 520px;
  color: {{heroSecondary}};
  font-size: clamp(14px, 1.15vw, 18px);
  line-height: 1.55;
  font-weight: 650;
}

html[data-codex-window-type="electron"] .codex-theme-home-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  margin-top: 16px;
}

html[data-codex-window-type="electron"] .codex-theme-home-tag {
  padding: 5px 9px;
  color: {{heroText}};
  background: {{ColorUtil.Alpha(theme.Surface, dark ? 0.62 : 0.72)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.42)}};
  border-radius: 999px;
  backdrop-filter: blur(10px);
  font-size: 11px;
  font-weight: 750;
}

html[data-codex-window-type="electron"] .codex-theme-home-actions {
  position: relative;
  z-index: 3;
  gap: 14px;
  margin: -104px 28px 0;
}

html[data-codex-window-type="electron"] .codex-theme-home-action {
  min-height: 184px;
  display: grid;
  grid-template-columns: 1fr;
  grid-template-rows: 54px auto 1fr;
  justify-items: center;
  gap: 8px;
  padding: 18px 16px 15px;
  text-align: center;
  color: {{theme.Ink}};
  background: {{cardSurface}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.54 : 0.34)}};
  border-radius: var(--codex-theme-card-radius);
  box-shadow: 0 16px 38px {{ColorUtil.Alpha(theme.Ink, dark ? 0.28 : 0.14)}};
  backdrop-filter: blur(22px) saturate(1.12);
}

html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(1) { background: {{actionOne}}; }
html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(2) { background: {{actionTwo}}; }
html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(3) { background: {{actionThree}}; }
html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(4) { background: {{actionFour}}; }

html[data-codex-window-type="electron"] .codex-theme-home-action:hover {
  transform: translateY(-5px);
  border-color: {{theme.Accent}};
  box-shadow: 0 22px 46px {{ColorUtil.Alpha(theme.Ink, dark ? 0.36 : 0.19)}};
}

html[data-codex-window-type="electron"] .codex-theme-home-action:focus-visible {
  outline: 3px solid {{ColorUtil.Alpha(theme.Accent, 0.44)}};
  outline-offset: 3px;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon {
  grid-row: auto;
  width: 52px;
  height: 52px;
  color: {{theme.Accent}};
  background: {{ColorUtil.Alpha(theme.Surface, dark ? 0.66 : 0.82)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.44)}};
  border-radius: 50%;
  font-size: 0;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon svg,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon img {
  width: 25px;
  height: 25px;
}

html[data-codex-theme-id="kun-stage"] .codex-theme-sidebar-icon img,
html[data-codex-theme-id="kun-stage"] .codex-theme-home-action-icon img {
  filter: invert(81%) sepia(25%) saturate(697%) hue-rotate(358deg) brightness(90%);
}

html[data-codex-theme-id="jackson-sage"] .codex-theme-sidebar-icon img,
html[data-codex-theme-id="jackson-sage"] .codex-theme-home-action-icon img {
  filter: invert(57%) sepia(17%) saturate(825%) hue-rotate(39deg) brightness(93%);
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-sidebar-icon img,
html[data-codex-theme-id="enfp-pop"] .codex-theme-home-action-icon img {
  filter: invert(59%) sepia(85%) saturate(626%) hue-rotate(121deg) brightness(88%);
}

html[data-codex-theme-id="dilraba-star"] .codex-theme-sidebar-icon img,
html[data-codex-theme-id="dilraba-star"] .codex-theme-home-action-icon img {
  filter: invert(36%) sepia(93%) saturate(2494%) hue-rotate(247deg) brightness(94%);
}

html[data-codex-window-type="electron"] .codex-theme-home-action-title {
  align-self: auto;
  font-size: 17px;
  line-height: 1.45;
  font-weight: 850;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-description {
  color: {{secondaryText}};
  font-size: 13px;
  line-height: 1.55;
}

html[data-codex-window-type="electron"] .composer-surface-chrome {
  min-height: 146px;
  padding: 12px !important;
  background: {{ColorUtil.Alpha(elevated2, dark ? 0.94 : 0.93)}} !important;
  border-color: {{ColorUtil.Alpha(theme.Accent, dark ? 0.76 : 0.58)}} !important;
  border-radius: 24px !important;
  box-shadow: 0 0 0 1px {{ColorUtil.Alpha(theme.Accent, 0.12)}}, 0 18px 42px {{ColorUtil.Alpha(theme.Ink, dark ? 0.34 : 0.18)}} !important;
}

html[data-codex-window-type="electron"] .composer-surface-chrome:focus-within {
  box-shadow: 0 0 0 3px {{ColorUtil.Alpha(theme.Accent, 0.24)}}, 0 22px 48px {{ColorUtil.Alpha(theme.Ink, dark ? 0.38 : 0.20)}} !important;
}

html[data-codex-window-type="electron"] .composer-surface-chrome .ProseMirror {
  min-height: 64px;
  color: {{theme.Ink}} !important;
  caret-color: {{theme.Accent}} !important;
  font-size: 16.5px;
  line-height: 1.6;
}

html[data-codex-window-type="electron"] [role="dialog"],
html[data-codex-window-type="electron"] [data-radix-popper-content-wrapper] > * {
  color: {{theme.Ink}} !important;
  background: {{ColorUtil.Alpha(elevated2, dark ? 0.97 : 0.96)}} !important;
  border-color: {{ColorUtil.Alpha(theme.Accent, dark ? 0.50 : 0.34)}} !important;
  border-radius: 18px !important;
  box-shadow: 0 24px 68px {{ColorUtil.Alpha(theme.Ink, dark ? 0.42 : 0.23)}} !important;
  backdrop-filter: blur(26px) saturate(1.12) !important;
}

html[data-codex-window-type="electron"] [data-content-search-unit-key$=":assistant"] {
  border-radius: 20px !important;
}

html[data-codex-window-type="electron"] [data-user-message-bubble="true"] {
  border-radius: 20px 20px 6px 20px !important;
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-hero {
  height: clamp(330px, 42vh, 450px);
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-actions {
  margin: 16px 18px 0;
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-action {
  min-height: 180px;
  border-radius: 26px;
}

html[data-codex-theme-id="dilraba-star"] .codex-theme-home-copy {
  inset-block-start: 45%;
}

html[data-codex-window-type="electron"][data-codex-theme-id="kun-stage"] .codex-theme-home-action {
  background: {{ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, "#ffffff", 0.06), 0.94)}} !important;
}

html[data-codex-window-type="electron"][data-codex-theme-id="jackson-sage"] .codex-theme-home-action {
  background: {{ColorUtil.Alpha(elevated2, 0.96)}} !important;
}

html[data-codex-window-type="electron"][data-codex-theme-id="dilraba-star"] .codex-theme-home-action {
  background: {{ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, "#ffffff", 0.36), 0.92)}} !important;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-compact .codex-theme-home-actions {
  gap: 12px;
  margin: -56px 18px 0;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-compact .codex-theme-home-action {
  min-height: 174px;
  padding-inline: 14px;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-compact .codex-theme-home-hero {
  min-height: 300px;
  height: clamp(300px, 38vh, 390px);
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-actions {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 14px;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-copy {
  width: 60%;
}

@media (max-width: 1280px) {
  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    gap: 12px;
    margin: -56px 18px 0;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-action {
    min-height: 174px;
    padding-inline: 14px;
  }
}

@media (max-width: 1120px) {
  html[data-codex-window-type="electron"] #codex-theme-home {
    padding-inline: 18px;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-copy {
    width: 48%;
    inset-inline-start: 26px;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    margin: -36px 16px 0;
  }
}

@media (max-width: 900px) {
  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    margin-top: 14px;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-copy {
    width: 60%;
  }
}

@media (max-width: 680px) {
  html[data-codex-window-type="electron"] #codex-theme-home {
    inset-block-end: 138px;
  }

  html[data-codex-window-type="electron"] .codex-theme-masthead-badge,
  html[data-codex-window-type="electron"] .codex-theme-home-tags {
    display: none;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-hero {
    min-height: 330px;
    height: 42vh;
    background-position: 62% center;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-copy {
    inset-inline: 20px;
    width: auto;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    grid-template-columns: 1fr;
  }
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-actions {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin: 14px 8px 0;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-action {
  min-height: 168px;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-hero {
  min-height: 300px;
  height: clamp(300px, 38vh, 340px);
}

@media (max-width: 520px) {
  html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-actions {
    grid-template-columns: 1fr;
  }
}

@media (prefers-reduced-motion: reduce) {
  html[data-codex-window-type="electron"] .codex-theme-home-action,
  html[data-codex-window-type="electron"] .codex-theme-sidebar-button,
  html[data-codex-window-type="electron"] .codex-theme-sidebar-row {
    transition: none !important;
  }
}
""";
    }

    private static string QuoteFont(string font) => font.Contains(',') || font.Contains('"') || font.Contains('\'') ? font : $"\"{font.Replace("\"", "\\\"")}\"";
    private static string CssEscape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(")", "\\)", StringComparison.Ordinal);
    private static string CssNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal static class JsBuilder
{
    public static string Build(JsonNode? copy, JsonNode? home, string themeId, string? logoUrl) => BuildV2(copy, home, themeId, logoUrl);

    private static string BuildLegacy(JsonNode? copy, JsonNode? home, string themeId)
    {
        var configNode = new JsonObject
        {
            ["copy"] = copy?.DeepClone() ?? new JsonObject(),
            ["home"] = home?.DeepClone() ?? new JsonObject(),
            ["themeId"] = themeId,
        };
        var payload = configNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return $@"(() => {{
const config = {payload};
globalThis.__codexThemeStore?.observer?.disconnect();
globalThis.__codexThemeStore?.main?.classList.remove(""codex-theme-home-active"");
document.getElementById(""codex-theme-home"")?.remove();
globalThis.__codexThemeStore = globalThis.__codexThemeStore || {{}};

function replaceTextValue(value, replacements) {{
  if (!value || !replacements) return value;
  const trimmed = value.trim();
  if (!trimmed) return value;
  const replacement = replacements[trimmed];
  if (!replacement) return value;
  return value.replace(trimmed, replacement);
}}

function applyThemeCopy() {{
  const copy = config.copy || {{}};
  if (copy.title) document.title = copy.title;

  const textMap = copy.replaceText || {{}};
  const placeholderMap = copy.replacePlaceholders || {{}};
  const skip = new Set([""SCRIPT"", ""STYLE"", ""TEXTAREA"", ""INPUT"", ""CODE"", ""PRE""]);
  const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT);
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);

  for (const node of nodes) {{
    const parent = node.parentElement;
    if (!parent || skip.has(parent.tagName)) continue;
    const next = replaceTextValue(node.nodeValue, textMap);
    if (next !== node.nodeValue) node.nodeValue = next;
  }}

  for (const el of document.querySelectorAll(""input[placeholder], textarea[placeholder]"")) {{
    const next = replaceTextValue(el.getAttribute(""placeholder""), placeholderMap);
    if (next !== el.getAttribute(""placeholder"")) el.setAttribute(""placeholder"", next);
  }}

  for (const el of document.querySelectorAll(""[aria-label], [title]"")) {{
    for (const attr of [""aria-label"", ""title""]) {{
      if (!el.hasAttribute(attr)) continue;
      const next = replaceTextValue(el.getAttribute(attr), textMap);
      if (next !== el.getAttribute(attr)) el.setAttribute(attr, next);
    }}
  }}
}}

function element(tag, className, text) {{
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text) node.textContent = text;
  return node;
}}

function fillComposer(prompt) {{
  const editor = document.querySelector("".ProseMirror"");
  if (!editor) return;
  editor.focus();
  const selection = window.getSelection();
  const range = document.createRange();
  range.selectNodeContents(editor);
  selection?.removeAllRanges();
  selection?.addRange(range);
  if (!document.execCommand(""insertText"", false, prompt)) {{
    editor.textContent = prompt;
    editor.dispatchEvent(new InputEvent(""input"", {{ bubbles: true, inputType: ""insertText"", data: prompt }}));
  }}
  editor.dispatchEvent(new Event(""change"", {{ bubbles: true }}));
}}

function createThemeHome() {{
  const homeConfig = config.home || {{}};
  const section = element(""section"", ""codex-theme-home"");
  section.id = ""codex-theme-home"";
  section.dataset.themeId = config.themeId || ""theme"";

  const shell = element(""div"", ""codex-theme-home-shell"");
  const hero = element(""div"", ""codex-theme-home-hero"");
  const copy = element(""div"", ""codex-theme-home-copy"");
  copy.append(
    element(""div"", ""codex-theme-home-brand"", homeConfig.brand || ""Codex""),
    element(""div"", ""codex-theme-home-eyebrow"", homeConfig.eyebrow || """"),
    element(""h1"", ""codex-theme-home-title"", homeConfig.title || ""我们该构建什么？""),
    element(""p"", ""codex-theme-home-subtitle"", homeConfig.subtitle || """")
  );
  hero.appendChild(copy);

  const actions = element(""div"", ""codex-theme-home-actions"");
  for (const action of homeConfig.quickActions || []) {{
    const button = element(""button"", ""codex-theme-home-action"");
    button.type = ""button"";
    button.setAttribute(""aria-label"", action.title || ""快速操作"");
    button.append(
      element(""span"", ""codex-theme-home-action-icon"", action.icon || ""+""),
      element(""span"", ""codex-theme-home-action-title"", action.title || ""快速操作""),
      element(""span"", ""codex-theme-home-action-description"", action.description || """")
    );
    button.addEventListener(""click"", () => fillComposer(action.prompt || action.title || """"));
    actions.appendChild(button);
  }}

  shell.append(hero, actions);
  section.appendChild(shell);
  return section;
}}

function applyThemeHome() {{
  const main = document.querySelector("".main-surface, .browser-main-surface"");
  if (!main) return;
  let home = document.getElementById(""codex-theme-home"");
  if (!home) {{
    home = createThemeHome();
    main.appendChild(home);
  }} else if (home.parentElement !== main) {{
    main.appendChild(home);
  }}

  const hasComposer = !!document.querySelector("".composer-surface-chrome .ProseMirror"");
  const hasMessages = !!document.querySelector('[data-content-search-unit-key$="":user""], [data-content-search-unit-key$="":assistant""]');
  main.classList.toggle(""codex-theme-home-active"", hasComposer && !hasMessages);
  globalThis.__codexThemeStore.main = main;
}}

let queued = false;
function queueApply() {{
  if (queued) return;
  queued = true;
  requestAnimationFrame(() => {{
    queued = false;
    applyThemeCopy();
    applyThemeHome();
  }});
}}

applyThemeCopy();
applyThemeHome();
const observer = new MutationObserver(queueApply);
observer.observe(document.documentElement, {{
  subtree: true,
  childList: true,
  characterData: true,
  attributes: true,
  attributeFilter: [""placeholder"", ""aria-label"", ""title""]
}});
globalThis.__codexThemeStore = {{ observer, applyThemeCopy, applyThemeHome, main: document.querySelector("".main-surface, .browser-main-surface"") }};
}})();
";
    }

    private static string BuildV2(JsonNode? copy, JsonNode? home, string themeId, string? logoUrl)
    {
        var configNode = new JsonObject
        {
            ["copy"] = copy?.DeepClone() ?? new JsonObject(),
            ["home"] = home?.DeepClone() ?? new JsonObject(),
            ["themeId"] = themeId,
            ["logoUrl"] = logoUrl,
        };
        var payload = configNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return $$"""
(() => {
  const config = {{payload}};
  const prior = globalThis.__codexThemeStore;
  prior?.observer?.disconnect();
  if (prior?.resizeHandler) window.removeEventListener("resize", prior.resizeHandler);
  prior?.main?.classList.remove("codex-theme-home-active");
  document.getElementById("codex-theme-home")?.remove();
  document.getElementById("codex-theme-sidebar")?.remove();

  const copyConfig = config.copy || {};
  const homeConfig = config.home || {};
  const sidebarLabels = homeConfig.sidebarLabels || {};
  const previousSidebarLabels = prior?.sidebarLabels || {};
  const themeId = config.themeId || "theme";
  const logoUrl = config.logoUrl || "";
  const originals = prior?.originals || { text: new WeakMap(), attributes: new WeakMap() };
  document.documentElement.dataset.codexThemeId = themeId;

  function normalizeText(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function localized(value) {
    return copyConfig.replaceText?.[value] || value;
  }

  function replaceTextValue(value, replacements) {
    if (!value || !replacements) return value;
    const trimmed = value.trim();
    if (!trimmed) return value;
    const replacement = replacements[trimmed];
    return replacement ? value.replace(trimmed, replacement) : value;
  }

  function applyTextReplacement(node, replacements) {
    const current = node.nodeValue || "";
    let state = originals.text.get(node);
    if (!state || current !== state.applied) {
      state = { original: current, applied: current };
    }
    const next = replaceTextValue(state.original, replacements);
    state.applied = next;
    originals.text.set(node, state);
    if (current !== next) node.nodeValue = next;
  }

  function applyAttributeReplacement(node, attribute, replacements) {
    const current = node.getAttribute(attribute);
    if (current === null) return;
    let attributes = originals.attributes.get(node);
    if (!attributes) {
      attributes = new Map();
      originals.attributes.set(node, attributes);
    }
    let state = attributes.get(attribute);
    if (!state || current !== state.applied) {
      state = { original: current, applied: current };
    }
    const next = replaceTextValue(state.original, replacements);
    state.applied = next;
    attributes.set(attribute, state);
    if (current !== next) node.setAttribute(attribute, next);
  }

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null && text !== "") node.textContent = text;
    return node;
  }

  function isThemeNode(node) {
    return !!node?.closest?.("#codex-theme-home, #codex-theme-sidebar");
  }

  function nativeControls() {
    return Array.from(document.querySelectorAll("button, a, [role='button']"))
      .filter(node => !isThemeNode(node));
  }

  function findNativeControl(aliases) {
    const wanted = (aliases || []).map(normalizeText).filter(Boolean);
    if (!wanted.length) return null;
    for (const control of nativeControls()) {
      const haystacks = [
        normalizeText(control.textContent),
        normalizeText(control.getAttribute("aria-label")),
        normalizeText(control.getAttribute("title")),
      ].filter(Boolean);
      if (haystacks.some(haystack => wanted.some(alias => haystack === alias || haystack.includes(alias)))) {
        return control;
      }
    }
    return null;
  }

  function findNativeSidebar() {
    const labeled = Array.from(document.querySelectorAll("nav[aria-label]"))
      .find(node => {
        const label = normalizeText(node.getAttribute("aria-label"));
        return label === "\u5df2\u5b89\u6392\u4efb\u52a1\u6587\u4ef6\u5939" || label === "Scheduled tasks folder";
      });
    if (labeled) return labeled;

    return Array.from(document.querySelectorAll("nav"))
      .find(node => {
        if (isThemeNode(node)) return false;
        const rect = node.getBoundingClientRect();
        if (rect.left > 24 || rect.width < 180 || rect.width > 420 || rect.height < window.innerHeight * 0.45) return false;
        const text = normalizeText(node.textContent);
        return ["\u65b0\u5efa\u4efb\u52a1", "New Task", "\u65b0\u5efa\u70b9\u5b50", "\u9879\u76ee", "Projects"]
          .some(alias => text.includes(alias));
      }) || null;
  }

  function sidebarAliases(base, configured, previous) {
    return Array.from(new Set([...(base || []), configured, previous].map(normalizeText).filter(Boolean)));
  }

  function setSidebarLeafText(scope, aliases, value, predicate) {
    if (!scope || !value) return false;
    const wanted = sidebarAliases(aliases);
    const candidates = Array.from(scope.querySelectorAll("span, div"));
    const target = candidates.find(node => {
      if (node.childElementCount > 0 || !wanted.includes(normalizeText(node.textContent))) return false;
      return !predicate || predicate(node);
    });
    if (!target || target.textContent === value) return !!target;
    target.textContent = value;
    return true;
  }

  function applyNativeSidebarLabels() {
    const sidebar = findNativeSidebar();
    if (!sidebar) return null;

    const definitions = {
      newTask: sidebarAliases(
        ["\u65b0\u5efa\u4efb\u52a1", "New Task", "New task", "\u65b0\u5efa\u70b9\u5b50", "\u65b0\u5efa\u7075\u611f", "\u65b0\u5efa\u624b\u8d26", "\u65b0\u5efa\u821e\u53f0", "New Spark", "Star Task", "New Note", "New Stage"],
        sidebarLabels.newTask,
        previousSidebarLabels.newTask
      ),
      scheduled: sidebarAliases(
        ["\u5df2\u5b89\u6392", "Scheduled", "\u7075\u611f\u6392\u671f"],
        sidebarLabels.scheduled,
        previousSidebarLabels.scheduled
      ),
      plugins: sidebarAliases(
        ["\u63d2\u4ef6", "\u6280\u80fd", "Skills", "Plugins", "\u6280\u80fd\u6e38\u4e50\u573a", "\u70ed\u5df4\u6280\u80fd", "\u5343\u73ba\u6280\u80fd", "\u821e\u53f0\u6280\u80fd", "Playground", "Star Skills", "Studio", "Setlist"],
        sidebarLabels.plugins,
        previousSidebarLabels.plugins
      ),
      projects: sidebarAliases(["\u9879\u76ee", "Projects"], sidebarLabels.projects, previousSidebarLabels.projects),
      tasks: sidebarAliases(["\u4efb\u52a1", "Tasks"], sidebarLabels.tasks, previousSidebarLabels.tasks),
      settings: sidebarAliases(["\u8bbe\u7f6e", "Settings"], sidebarLabels.settings, previousSidebarLabels.settings),
    };

    setSidebarLeafText(sidebar, definitions.newTask, sidebarLabels.newTask);
    setSidebarLeafText(sidebar, definitions.scheduled, sidebarLabels.scheduled);
    setSidebarLeafText(sidebar, definitions.plugins, sidebarLabels.plugins);

    const isSectionHeading = node => !!node.closest("button[class*='group/section-toggle'], [role='button'][class*='group/section-toggle']");
    setSidebarLeafText(sidebar, definitions.projects, sidebarLabels.projects, isSectionHeading);
    setSidebarLeafText(sidebar, definitions.tasks, sidebarLabels.tasks, isSectionHeading);

    const settingsControl = Array.from(sidebar.querySelectorAll("button, a, [role='button']"))
      .find(node => {
        const aria = normalizeText(node.getAttribute("aria-label"));
        return aria === "\u6253\u5f00\u8bbe\u7f6e" || aria === "Open settings" || definitions.settings.includes(normalizeText(node.textContent));
      });
    setSidebarLeafText(settingsControl, definitions.settings, sidebarLabels.settings);

    sidebar.dataset.codexThemeSidebarLabels = themeId;
    return sidebar;
  }

  function sanitizeClone(node) {
    const clone = node.cloneNode(true);
    if (clone.removeAttribute) clone.removeAttribute("id");
    for (const child of clone.querySelectorAll?.("[id]") || []) child.removeAttribute("id");
    clone.setAttribute?.("aria-hidden", "true");
    clone.removeAttribute?.("aria-label");
    clone.removeAttribute?.("title");
    return clone;
  }

  function cloneNativeIcon(aliases, fallbackIndex) {
    const control = findNativeControl(aliases);
    const preferred = control?.querySelector("svg, img");
    if (preferred) return sanitizeClone(preferred);

    const pool = Array.from(document.querySelectorAll("button svg, a svg, [role='button'] svg, button img, a img"))
      .filter(node => !isThemeNode(node));
    if (!pool.length) return null;
    return sanitizeClone(pool[Math.abs(fallbackIndex || 0) % pool.length]);
  }

  function setIconHint(slot, aliases, index) {
    slot.dataset.iconAliases = (aliases || []).join("|");
    slot.dataset.iconIndex = String(index || 0);
    hydrateIcon(slot);
  }

  function hydrateIcon(slot) {
    if (!slot || slot.childElementCount) return;
    const aliases = (slot.dataset.iconAliases || "").split("|").filter(Boolean);
    const icon = cloneNativeIcon(aliases, Number(slot.dataset.iconIndex || 0));
    if (icon) {
      slot.hidden = false;
      slot.appendChild(icon);
    } else {
      slot.hidden = true;
    }
  }

  function hydrateThemeIcons() {
    for (const slot of document.querySelectorAll("#codex-theme-home [data-icon-index], #codex-theme-sidebar [data-icon-index]")) {
      hydrateIcon(slot);
    }
  }

  function triggerNative(aliases) {
    const target = findNativeControl(aliases);
    if (!target) return false;
    target.click();
    return true;
  }

  function fillComposer(prompt) {
    const editor = document.querySelector(".ProseMirror");
    if (!editor) return;
    editor.focus();
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(editor);
    selection?.removeAllRanges();
    selection?.addRange(range);
    if (!document.execCommand("insertText", false, prompt)) {
      editor.textContent = prompt;
      editor.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: prompt }));
    }
    editor.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function applyThemeCopy() {
    document.documentElement.dataset.codexThemeId = themeId;
    if (copyConfig.title) document.title = copyConfig.title;

    const textMap = copyConfig.replaceText || {};
    const placeholderMap = copyConfig.replacePlaceholders || {};
    const skip = new Set(["SCRIPT", "STYLE", "TEXTAREA", "INPUT", "CODE", "PRE"]);
    const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT);
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);

    for (const node of nodes) {
      const parent = node.parentElement;
      if (!parent || skip.has(parent.tagName) || isThemeNode(parent)) continue;
      applyTextReplacement(node, textMap);
    }

    for (const input of document.querySelectorAll("input[placeholder], textarea[placeholder]")) {
      applyAttributeReplacement(input, "placeholder", placeholderMap);
    }

    for (const node of document.querySelectorAll("[aria-label], [title]")) {
      if (isThemeNode(node)) continue;
      for (const attribute of ["aria-label", "title"]) {
        applyAttributeReplacement(node, attribute, textMap);
      }
    }

    const editor = document.querySelector(".ProseMirror");
    if (editor && homeConfig.composerHint) {
      editor.setAttribute("aria-label", homeConfig.composerHint);
      editor.dataset.placeholder = homeConfig.composerHint;
    }
  }

  function sidebarButton(label, aliases, iconIndex) {
    const button = element("button", "codex-theme-sidebar-button");
    button.type = "button";
    button.setAttribute("aria-label", label);
    const icon = element("span", "codex-theme-sidebar-icon");
    setIconHint(icon, aliases, iconIndex);
    button.append(icon, element("span", "", label));
    button.addEventListener("click", () => triggerNative(aliases));
    return button;
  }

  function sidebarRow(text, kind, index) {
    const button = element("button", "codex-theme-sidebar-row");
    button.type = "button";
    button.dataset.kind = kind;
    if (kind === "task") {
      const icon = element("span", "codex-theme-sidebar-icon codex-theme-sidebar-task-icon");
      setIconHint(icon, ["已完成", "完成", "Completed"], index + 14);
      button.appendChild(icon);
    }
    button.appendChild(element("span", "", text));
    button.addEventListener("click", () => fillComposer(kind === "task" ? `请继续处理任务：${text}` : `请打开并梳理：${text}`));
    return button;
  }

  function createBrandLogo() {
    const logo = element("div", "codex-theme-sidebar-logo");
    if (!logoUrl) {
      logo.textContent = "Codex";
      return logo;
    }
    const image = element("img", "codex-theme-sidebar-logo-image");
    image.src = logoUrl;
    image.alt = "Codex";
    image.decoding = "async";
    image.draggable = false;
    logo.appendChild(image);
    return logo;
  }

  function createThemeSidebar() {
    const sidebar = element("aside", "codex-theme-sidebar");
    sidebar.id = "codex-theme-sidebar";
    sidebar.dataset.themeId = themeId;

    const head = element("div", "codex-theme-sidebar-head");
    head.append(
      createBrandLogo(),
      element("div", "codex-theme-sidebar-theme", homeConfig.brand || themeId)
    );

    const scroll = element("div", "codex-theme-sidebar-scroll");
    const nav = element("nav", "codex-theme-sidebar-nav");
    const navItems = [
      [localized("新建任务"), ["新建任务", "New Task", "New task"], 0],
      [localized("已安排"), ["已安排", "Scheduled"], 1],
      [localized("技能"), ["技能", "Skills"], 2],
      [localized("站点"), ["站点", "Sites"], 3],
      [localized("拉取请求"), ["拉取请求", "Pull requests", "Pull Requests"], 4],
      [localized("聊天"), ["聊天", "Chat"], 5],
    ];
    for (const [label, aliases, iconIndex] of navItems) nav.appendChild(sidebarButton(label, aliases, iconIndex));
    scroll.appendChild(nav);

    const projectSection = element("section", "codex-theme-sidebar-section");
    projectSection.append(
      element("div", "codex-theme-sidebar-section-title", localized("项目")),
      element("div", "codex-theme-sidebar-project", homeConfig.sidebarProject || homeConfig.brand || "主题项目")
    );
    for (const [index, item] of (homeConfig.sidebarItems || []).entries()) {
      projectSection.appendChild(sidebarRow(item, "project", index));
    }
    scroll.appendChild(projectSection);

    const taskSection = element("section", "codex-theme-sidebar-section");
    taskSection.appendChild(element("div", "codex-theme-sidebar-section-title", localized("任务")));
    for (const [index, task] of (homeConfig.tasks || []).entries()) {
      taskSection.appendChild(sidebarRow(task, "task", index));
    }
    scroll.appendChild(taskSection);

    const progress = element("div", "codex-theme-sidebar-progress");
    const progressRaw = homeConfig.progressLabel || localized("继续设置");
    const progressMatch = progressRaw.match(/(\d+%|\d+\s*\/\s*\d+)/);
    const progressValueText = progressMatch?.[1] || "72%";
    const progressLabel = normalizeText(progressRaw.replace(progressMatch?.[0] || "", "").replace(/[·•—-]+$/, ""));
    let progressPercent = 72;
    if (progressValueText.endsWith("%")) {
      progressPercent = Math.max(0, Math.min(100, Number.parseFloat(progressValueText)));
    } else if (progressValueText.includes("/")) {
      const [done, total] = progressValueText.split("/").map(value => Number.parseFloat(value));
      progressPercent = total > 0 ? Math.max(0, Math.min(100, done / total * 100)) : 0;
    }
    const progressCopy = element("div", "codex-theme-sidebar-progress-copy");
    progressCopy.append(
      element("span", "", progressLabel || localized("继续设置")),
      element("span", "", progressValueText)
    );
    const track = element("div", "codex-theme-sidebar-progress-track");
    const progressValue = element("div", "codex-theme-sidebar-progress-value");
    progressValue.style.width = `${progressPercent}%`;
    track.appendChild(progressValue);
    progress.append(progressCopy, track);
    scroll.appendChild(progress);

    const profile = element("button", "codex-theme-sidebar-profile");
    profile.type = "button";
    profile.append(
      element("span", "codex-theme-sidebar-profile-name", "momo"),
      element("span", "codex-theme-sidebar-profile-label", homeConfig.profileLabel || homeConfig.brand || themeId)
    );
    profile.addEventListener("click", () => triggerNative(["打开设置", "Settings", "设置"]));

    sidebar.append(head, scroll, profile);
    return sidebar;
  }

  function applyThemeSidebar() {
    // Never cover or replace Codex's native sidebar. Project and task data in
    // that panel belongs to the user; themes may style it but must not create
    // demo navigation on top of it.
    document.getElementById("codex-theme-sidebar")?.remove();
    return null;
  }

  function createThemeHome() {
    const section = element("section", "codex-theme-home");
    section.id = "codex-theme-home";
    section.dataset.themeId = themeId;

    const shell = element("div", "codex-theme-home-shell");
    const masthead = element("header", "codex-theme-masthead");
    const mastheadCopy = element("div", "codex-theme-masthead-copy");
    mastheadCopy.append(
      element("div", "codex-theme-masthead-title", homeConfig.eyebrow || homeConfig.brand || "Codex"),
      element("div", "codex-theme-masthead-note", homeConfig.footerNote || homeConfig.subtitle || "")
    );
    masthead.append(
      mastheadCopy,
      element("div", "codex-theme-masthead-badge", homeConfig.badge || homeConfig.brand || themeId)
    );

    const hero = element("div", "codex-theme-home-hero");
    const heroCopy = element("div", "codex-theme-home-copy");
    heroCopy.append(
      element("div", "codex-theme-home-brand", homeConfig.brand || "Codex"),
      element("div", "codex-theme-home-eyebrow", homeConfig.eyebrow || ""),
      element("h1", "codex-theme-home-title", homeConfig.title || "我们该构建什么？"),
      element("p", "codex-theme-home-subtitle", homeConfig.subtitle || "")
    );
    const tags = element("div", "codex-theme-home-tags");
    for (const tag of homeConfig.tags || []) tags.appendChild(element("span", "codex-theme-home-tag", tag));
    heroCopy.appendChild(tags);
    hero.appendChild(heroCopy);

    const actions = element("div", "codex-theme-home-actions");
    for (const [index, action] of (homeConfig.quickActions || []).entries()) {
      const button = element("button", "codex-theme-home-action");
      button.type = "button";
      button.setAttribute("aria-label", action.title || "快速操作");
      const icon = element("span", "codex-theme-home-action-icon");
      setIconHint(icon, [action.title || "", action.description || ""], index + 6);
      button.append(
        icon,
        element("span", "codex-theme-home-action-title", action.title || "快速操作"),
        element("span", "codex-theme-home-action-description", action.description || "")
      );
      button.addEventListener("click", () => fillComposer(action.prompt || action.title || ""));
      actions.appendChild(button);
    }

    shell.append(masthead, hero, actions);
    section.appendChild(shell);
    return section;
  }

  function applyThemeHome() {
    const main = document.querySelector(".main-surface, .browser-main-surface");
    if (!main) return null;
    let home = document.getElementById("codex-theme-home");
    if (home?.dataset.themeId !== themeId) {
      home?.remove();
      home = null;
    }
    if (!home) home = createThemeHome();
    if (home.parentElement !== main) main.appendChild(home);

    const composer = document.querySelector(".composer-surface-chrome");
    const hasComposer = !!composer?.querySelector(".ProseMirror");
    const hasMessages = !!document.querySelector('[data-content-search-unit-key$=":user"], [data-content-search-unit-key$=":assistant"]');
    const mainRect = main.getBoundingClientRect();
    home.classList.toggle("codex-theme-home-compact", mainRect.width <= 1180);
    home.classList.toggle("codex-theme-home-narrow", mainRect.width <= 760);
    if (hasComposer) {
      const composerRect = composer.getBoundingClientRect();
      const reserve = Math.min(280, Math.max(138, Math.ceil(mainRect.bottom - composerRect.top + 18)));
      home.style.bottom = `${reserve}px`;
    } else {
      home.style.removeProperty("bottom");
    }
    main.classList.toggle("codex-theme-home-active", hasComposer && !hasMessages);
    globalThis.__codexThemeStore.main = main;
    return home;
  }

  let queued = false;
  function queueApply() {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => {
      queued = false;
      applyThemeCopy();
      applyNativeSidebarLabels();
      applyThemeSidebar();
      applyThemeHome();
      hydrateThemeIcons();
    });
  }

  globalThis.__codexThemeStore = {
    observer: null,
    themeId,
    applyThemeCopy,
    applyNativeSidebarLabels,
    applyThemeSidebar,
    applyThemeHome,
    originals,
    sidebarLabels,
    main: null,
    resizeHandler: null,
  };

  applyThemeCopy();
  applyNativeSidebarLabels();
  applyThemeSidebar();
  applyThemeHome();
  hydrateThemeIcons();

  const observer = new MutationObserver(queueApply);
  observer.observe(document.documentElement, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ["placeholder", "aria-label", "title", "class"],
  });
  globalThis.__codexThemeStore.observer = observer;
  const resizeHandler = () => queueApply();
  window.addEventListener("resize", resizeHandler, { passive: true });
  globalThis.__codexThemeStore.resizeHandler = resizeHandler;
})();
""";
    }
}

internal static class ColorUtil
{
    public static string Mix(string a, string b, double weight)
    {
        var ca = Parse(a);
        var cb = Parse(b);
        return ToHex(
            ca.R * (1 - weight) + cb.R * weight,
            ca.G * (1 - weight) + cb.G * weight,
            ca.B * (1 - weight) + cb.B * weight);
    }

    public static string Alpha(string hex, double opacity)
    {
        var c = Parse(hex);
        return string.Create(CultureInfo.InvariantCulture, $"rgb({c.R} {c.G} {c.B} / {Math.Clamp(opacity, 0, 1):0.000})");
    }

    public static double Luminance(string hex)
    {
        var c = Parse(hex);
        double Linear(int value)
        {
            var x = value / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    public static string ReadableText(string hex) => Luminance(hex) > 0.55 ? "#141413" : "#ffffff";

    private static (int R, int G, int B) Parse(string hex)
    {
        if (!Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) throw new InvalidOperationException($"Invalid color: {hex}");
        return (
            Convert.ToInt32(hex.Substring(1, 2), 16),
            Convert.ToInt32(hex.Substring(3, 2), 16),
            Convert.ToInt32(hex.Substring(5, 2), 16));
    }

    private static string ToHex(double r, double g, double b)
    {
        static int Clamp(double value) => Math.Clamp((int)Math.Round(value), 0, 255);
        return $"#{Clamp(r):x2}{Clamp(g):x2}{Clamp(b):x2}";
    }
}



