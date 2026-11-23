using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace control_center;

public partial class Form1 : Form
{
    private const string HealthUrl = "http://127.0.0.1:11434/health";
    private const string SmartModelUrl = "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf";
    private const string SmartModelFilename = "qwen2.5-7b-instruct-q4_k_m.gguf";
    private const long SmartModelBytes = 4683074240L;

    private const string LightModelUrl = "https://huggingface.co/IndexTeam/Index-Translate-2B-GGUF/resolve/main/Index-Translate-2B.Q8_0.gguf";
    private const string LightModelFilename = "Index-Translate-2B.Q8_0.gguf";
    private const long LightModelBytes = 2076674560L;

    private const string ModelFilename = SmartModelFilename;
    private const long ExpectedModelBytes = SmartModelBytes;
    public const string AppVersion = "v1.1.3";
    public const string GitHubRepo = "keltuze/AI-Translator";

    private readonly string _baseDir;
    private readonly HttpClient _httpClient;
    private readonly System.Windows.Forms.Timer _healthTimer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _menuItemToggle;
    private readonly ToolStripMenuItem _menuItemShow;
    private readonly ToolStripMenuItem _menuItemExit;

    private bool _isServerHealthy = false;
    private bool _isExiting = false;
    private Process? _curlDownloadProcess = null;
    private CancellationTokenSource? _downloadCts = null;

    // UI Language State
    private string _currentLang = "ru";
    private Button _btnLangRu = null!;
    private Button _btnLangEn = null!;
    private Button _btnLangCn = null!;
    private Button _btnCheckUpdate = null!;
    private ProgressBar _pbAppUpdate = null!;
    private CancellationTokenSource? _updateCts = null;
    private bool _isUpdatingApp = false;

    // UI Controls
    private Label _lblHeaderStatus = null!;
    private Button _btnServerToggle = null!;
    private Label _lblServerHint = null!;
    private Label _lblDiscordTitle = null!;
    private Label _lblDiscordStatus = null!;
    private Button _btnInstallDiscord = null!;
    private Button _btnUninstallDiscord = null!;
    private Label _lblGoogleHint = null!;
    private Label _lblModelTitle = null!;
    private Label _lblModelStatus = null!;
    private Label _lblModelPath = null!;
    private ProgressBar _pbDownload = null!;
    private Label _lblDownloadProgress = null!;
    private Button _btnDownloadModel = null!;
    private Button _btnDownloadLight = null!;
    private Button _btnQuickSwitch = null!;
    private Button _btnBrowseModel = null!;
    private Button _btnCancelDownload = null!;
    private Button _btnOpenFolder = null!;
    private Button _btnDeleteModel = null!;
    private Label _lblMemoTitle = null!;
    private Label _lblMemoText = null!;
    private CheckBox _cbAutostart = null!;
    private Button _btnMinimize = null!;
    private Button _btnExit = null!;
    private Label _lblAuthorFooter = null!;
    private LinkLabel _lnkBoosty = null!;
    private Label _lblVencordThanks = null!;
    private LinkLabel _lnkVencord = null!;
    private Label _lblRepoFooter = null!;
    private LinkLabel _lnkRepo = null!;
    private Label _lblNotification = null!;
    private System.Windows.Forms.Timer _notificationTimer = null!;

    // Quick Translate Side Panel
    private bool _isSidePanelOpen = false;
    private const int BaseFormWidth = 780;
    private const int SidePanelWidth = 380;
    private int _savedFormLeft = -1;
    private CancellationTokenSource? _quickTransCancelSource;

    private Button _btnToggleSidePanel = null!;
    private Panel _pnlSideTranslator = null!;
    private Label _lblSideTitle = null!;
    private Button _btnCloseSide = null!;
    private ComboBox _cbSourceLanguage = null!;
    private Button _btnSidePaste = null!;
    private Button _btnSideClear = null!;
    private TextBox _txtSideInput = null!;
    private Button _btnSideTranslate = null!;
    private Button _btnAutoTranslate = null!;
    private bool _isAutoTranslateEnabled = false;
    private string _lastTranslatedText = "";
    private System.Windows.Forms.Timer? _autoTranslateDebounceTimer;
    private Label _lblSideStatus = null!;
    private Label _lblTargetLang = null!;
    private ComboBox _cbTargetLanguage = null!;
    private Button _btnSwapLanguages = null!;
    private Button _btnSideCopy = null!;
    private TextBox _txtSideOutput = null!;
    private Label _lblSideHint = null!;

    public Form1()
    {
        Program.Log("Form1 constructor starting");
        try
        {
            ChangeWindowMessageFilter(Program.WM_SHOW_AI_TRANSLATOR, MSGFLT_ADD);
        }
        catch { }
        _baseDir = DetectBaseDir();
        LoadConfig();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };

        // Tray Menu & NotifyIcon
        _trayMenu = new ContextMenuStrip();
        _menuItemToggle = new ToolStripMenuItem("▶ Запустить сервер (GPU / Vulkan)", null, (s, e) => ToggleServer());
        _menuItemShow = new ToolStripMenuItem("🌐 Панель управления", null, (s, e) => ShowWindow());
        _menuItemExit = new ToolStripMenuItem("❌ Выход", null, (s, e) => FullExit());

        _trayMenu.Items.Add(_menuItemShow);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_menuItemToggle);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_menuItemExit);

        _notifyIcon = new NotifyIcon
        {
            Text = "AI Translator (Остановлен)",
            ContextMenuStrip = _trayMenu,
            Visible = true,
            Icon = CreateCircleIcon(false)
        };
        _notifyIcon.DoubleClick += (s, e) => ShowWindow();

        BuildModernUi();

        _healthTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _healthTimer.Tick += async (s, e) =>
        {
            try
            {
                await CheckHealthAsync();
            }
            catch (Exception ex)
            {
                Program.Log($"HealthTimer exception: {ex}");
            }
        };
        _healthTimer.Start();

        _notificationTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _notificationTimer.Tick += (s, e) =>
        {
            _lblNotification.Text = "";
            _notificationTimer.Stop();
        };

        UpdateDiscordStatus();
        UpdateModelStatus();
        EnsureEngineExtracted();

        // Autostart check
        _cbAutostart.Checked = IsAutostartEnabled();

        // Background check for updates from GitHub
        Task.Run(async () =>
        {
            await Task.Delay(2500);
            try
            {
                if (!IsDisposed)
                {
                    BeginInvoke(new Action(() => CheckForUpdates(isManualClick: false)));
                }
            }
            catch { }
        });

        // Check command line arguments for starting minimized
        string[] args = Environment.GetCommandLineArgs();
        foreach (var a in args)
        {
            if (a.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                Opacity = 0;
                break;
            }
        }
    }

    protected override void OnShown(EventArgs e)
    {
        Program.Log("Form1 OnShown");
        base.OnShown(e);
        string[] args = Environment.GetCommandLineArgs();
        foreach (var a in args)
        {
            if (a.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                Hide();
                Opacity = 1;
                break;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Program.Log($"Form1 OnFormClosing: CloseReason={e.CloseReason}, isExiting={_isExiting}");
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }

        _healthTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log($"Form1 OnFormClosed: CloseReason={e.CloseReason}");
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Program.WM_SHOW_AI_TRANSLATOR)
        {
            Program.Log("WM_SHOW_AI_TRANSLATOR received in WndProc, showing window");
            ShowWindow();
        }
        base.WndProc(ref m);
    }

    private void ShowWindow()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ShowWindow));
            return;
        }

        if (!Visible)
        {
            Show();
        }
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        ShowInTaskbar = true;
        TopMost = true;
        BringToFront();
        Activate();
        TopMost = false;
        Program.SetForegroundWindow(Handle);
        try
        {
            LoadConfig();
            UpdateModelStatus();
        }
        catch { }
    }

    private void HideWindow()
    {
        Hide();
        ShowInTaskbar = false;
        _notifyIcon.ShowBalloonTip(1500, "AI Translator", "Программа свернута в системный трей возле часов.", ToolTipIcon.Info);

        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

    private void FullExit()
    {
        _isExiting = true;
        _updateCts?.Cancel();
        _curlDownloadProcess?.Kill();
        Close();
        Application.Exit();
    }

    private string DetectBaseDir()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                string? dir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
            string? baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
                return baseDir.TrimEnd('\\', '/');
        }
        catch { }

        return @"C:\AI Translate";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);
    private const uint MSGFLT_ADD = 1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    private static Icon CreateCircleIcon(bool isActive)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            var outerColor = isActive ? Color.FromArgb(34, 197, 94) : Color.FromArgb(140, 145, 155);
            using (var brush = new SolidBrush(outerColor))
            {
                g.FillEllipse(brush, 2, 2, size - 4, size - 4);
            }

            using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 2))
            {
                g.DrawEllipse(pen, 2, 2, size - 4, size - 4);
            }

            using (var whiteBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(whiteBrush, 10, 10, size - 20, size - 20);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        var tempIcon = Icon.FromHandle(hIcon);
        var icon = (Icon)tempIcon.Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private void BuildModernUi()
    {
        Text = "AI Translator Control Center";
        Size = new Size(780, 765);
        MinimumSize = new Size(760, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(28, 29, 33);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16),
            AutoScroll = true
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // Header
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // Server control
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 114)); // Discord card
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 135)); // Model card
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); // Usage memo
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); // Bottom bar
        CreateSideTranslatorPanel();
        Controls.Add(mainPanel);
        Controls.Add(_pnlSideTranslator);

        // 1. Header
        var headerPanel = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label
        {
            Text = "🌐 AI Translator",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(0, 8)
        };

        // Language Buttons: [ RU ] [ EN ] [ CN ]
        var pnlLang = new FlowLayoutPanel
        {
            Location = new Point(160, 8),
            Size = new Size(138, 32),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _btnLangRu = CreateLangButton("RU");
        _btnLangRu.Click += (s, e) => SwitchLanguage("ru");

        _btnLangEn = CreateLangButton("EN");
        _btnLangEn.Click += (s, e) => SwitchLanguage("en");

        _btnLangCn = CreateLangButton("CN");
        _btnLangCn.Click += (s, e) => SwitchLanguage("zh");

        pnlLang.Controls.Add(_btnLangRu);
        pnlLang.Controls.Add(_btnLangEn);
        pnlLang.Controls.Add(_btnLangCn);

        _btnCheckUpdate = new Button
        {
            Text = $"⚡ {AppVersion}",
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
            BackColor = Color.FromArgb(55, 60, 68),
            ForeColor = Color.FromArgb(220, 225, 235),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(122, 28),
            Location = new Point(305, 6),
            Cursor = Cursors.Hand
        };
        _btnCheckUpdate.FlatAppearance.BorderSize = 0;
        _btnCheckUpdate.Click += (s, e) => CheckForUpdates(isManualClick: true);

        _pbAppUpdate = new ProgressBar
        {
            Location = new Point(305, 36),
            Size = new Size(122, 4),
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };

        _btnToggleSidePanel = new Button
        {
            Text = "◀ Ручной перевод",
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
            BackColor = Color.FromArgb(55, 60, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(155, 28),
            Location = new Point(434, 6),
            Cursor = Cursors.Hand
        };
        _btnToggleSidePanel.FlatAppearance.BorderSize = 0;
        _btnToggleSidePanel.Click += (s, e) => ToggleSidePanel();

        _lblHeaderStatus = new Label
        {
            Text = "⚪ Сервер остановлен",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Right,
            Width = 145
        };
        headerPanel.Controls.Add(_lblHeaderStatus);
        headerPanel.Controls.Add(_btnToggleSidePanel);
        headerPanel.Controls.Add(_btnCheckUpdate);
        headerPanel.Controls.Add(_pbAppUpdate);
        headerPanel.Controls.Add(pnlLang);
        headerPanel.Controls.Add(lblTitle);
        mainPanel.Controls.Add(headerPanel, 0, 0);

        // 2. Server Control Card
        var pnlServer = CreateCardPanel();
        _btnServerToggle = new Button
        {
            Text = "▶ Запустить сервер",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(34, 197, 94),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(210, 44),
            Location = new Point(14, 18),
            Cursor = Cursors.Hand
        };
        _btnServerToggle.FlatAppearance.BorderSize = 0;
        _btnServerToggle.Click += (s, e) => ToggleServer();

        _lblServerHint = new Label
        {
            Text = "Локальная модель в видеопамяти\nПеред запуском тяжелых 3D-игр нажмите «Остановить», чтобы освободить VRAM.",
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = Color.FromArgb(180, 185, 195),
            Location = new Point(236, 14),
            Size = new Size(470, 56)
        };
        pnlServer.Controls.Add(_btnServerToggle);
        pnlServer.Controls.Add(_lblServerHint);
        mainPanel.Controls.Add(pnlServer, 0, 1);

        // 3. Discord Integration Card
        var pnlDiscord = CreateCardPanel();
        _lblDiscordTitle = new Label
        {
            Text = "1. Интеграция с Discord (плагин Vencord)",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 10),
            AutoSize = true
        };
        _lblDiscordStatus = new Label
        {
            Text = "Проверка статуса...",
            Font = new Font("Segoe UI", 9.2f),
            ForeColor = Color.FromArgb(219, 222, 225),
            Location = new Point(14, 34),
            Size = new Size(335, 40)
        };
        _btnInstallDiscord = new Button
        {
            Text = "🚀 Установить в Discord (1 клик)",
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            BackColor = Color.FromArgb(88, 101, 242), // Discord Blurple
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(198, 42),
            Location = new Point(365, 18),
            Cursor = Cursors.Hand
        };
        _btnInstallDiscord.FlatAppearance.BorderSize = 0;
        _btnInstallDiscord.Click += (s, e) => InstallDiscordPlugin();

        _btnUninstallDiscord = new Button
        {
            Text = "🗑️ Убрать плагин",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(218, 55, 60), // Discord Danger Red #DA373C
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(130, 42),
            Location = new Point(575, 18),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _btnUninstallDiscord.FlatAppearance.BorderSize = 0;
        _btnUninstallDiscord.Click += (s, e) => UninstallDiscordPlugin();

        _lblGoogleHint = new Label
        {
            Text = "💡 Для Google Translate: достаточно нажать только эту кнопку! Сервер и нейросеть можно не качать.",
            Font = new Font("Segoe UI", 8.4f),
            ForeColor = Color.FromArgb(160, 165, 175),
            Location = new Point(14, 82),
            Size = new Size(690, 20)
        };

        pnlDiscord.Controls.Add(_lblDiscordTitle);
        pnlDiscord.Controls.Add(_lblDiscordStatus);
        pnlDiscord.Controls.Add(_btnInstallDiscord);
        pnlDiscord.Controls.Add(_btnUninstallDiscord);
        pnlDiscord.Controls.Add(_lblGoogleHint);
        _btnInstallDiscord.BringToFront();
        _btnUninstallDiscord.BringToFront();
        mainPanel.Controls.Add(pnlDiscord, 0, 2);

        // 4. Model Card
        var pnlModel = CreateCardPanel();
        _lblModelTitle = new Label
        {
            Text = "2. Локальная нейросеть (GGUF / любая модель)",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 10),
            AutoSize = true
        };
        _lblModelStatus = new Label
        {
            Text = "Статус модели...",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 197, 94),
            Location = new Point(460, 10),
            Size = new Size(245, 22),
            TextAlign = ContentAlignment.TopRight
        };
        _lblModelPath = new Label
        {
            Text = "Файл: " + Path.Combine(_baseDir, "models", ModelFilename),
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = Color.FromArgb(160, 165, 175),
            Location = new Point(14, 34),
            Size = new Size(690, 20)
        };

        _pbDownload = new ProgressBar
        {
            Location = new Point(14, 58),
            Size = new Size(690, 18),
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };

        _lblDownloadProgress = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = Color.FromArgb(96, 165, 250),
            Location = new Point(14, 80),
            Size = new Size(480, 20),
            Visible = false
        };

        _btnDownloadModel = new Button
        {
            Text = "🧠 1. Умная (Qwen 7B ~4.7 ГБ)",
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(260, 36),
            Location = new Point(14, 76),
            Cursor = Cursors.Hand
        };
        _btnDownloadModel.FlatAppearance.BorderSize = 0;
        _btnDownloadModel.Click += (s, e) => StartDownload(false);

        _btnDownloadLight = new Button
        {
            Text = "⚡ 2. Легкая (Index 2B ~2.0 ГБ)",
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(260, 36),
            Location = new Point(284, 76),
            Cursor = Cursors.Hand
        };
        _btnDownloadLight.FlatAppearance.BorderSize = 0;
        _btnDownloadLight.Click += (s, e) => StartDownload(true);

        _btnQuickSwitch = new Button
        {
            Text = "⚡ Переключить модель",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(139, 92, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(220, 36),
            Location = new Point(14, 76),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _btnQuickSwitch.FlatAppearance.BorderSize = 0;
        _btnQuickSwitch.Click += (s, e) => ToggleOfficialModel();

        _btnBrowseModel = new Button
        {
            Text = "📁 Указать файл на диске",
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(55, 60, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(200, 36),
            Location = new Point(304, 76),
            Cursor = Cursors.Hand
        };
        _btnBrowseModel.FlatAppearance.BorderSize = 0;
        _btnBrowseModel.Click += (s, e) => BrowseModelFile();

        _btnCancelDownload = new Button
        {
            Text = "⏹ Приостановить",
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(150, 32),
            Location = new Point(555, 78),
            Visible = false,
            Cursor = Cursors.Hand
        };
        _btnCancelDownload.FlatAppearance.BorderSize = 0;
        _btnCancelDownload.Click += (s, e) => CancelDownload();

        _btnOpenFolder = new Button
        {
            Text = "📂 Открыть папку",
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(55, 60, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(215, 36),
            Location = new Point(14, 76),
            Cursor = Cursors.Hand
        };
        _btnOpenFolder.FlatAppearance.BorderSize = 0;
        _btnOpenFolder.Click += (s, e) => OpenModelFolder();

        _btnDeleteModel = new Button
        {
            Text = "🗑️ Удалить модель",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(218, 55, 60), // Discord Danger Red
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(229, 36),
            Location = new Point(476, 76),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _btnDeleteModel.FlatAppearance.BorderSize = 0;
        _btnDeleteModel.Click += (s, e) => DeleteModelFile();

        pnlModel.Controls.Add(_lblModelTitle);
        pnlModel.Controls.Add(_lblModelStatus);
        pnlModel.Controls.Add(_lblModelPath);
        pnlModel.Controls.Add(_pbDownload);
        pnlModel.Controls.Add(_lblDownloadProgress);
        pnlModel.Controls.Add(_btnDownloadModel);
        pnlModel.Controls.Add(_btnDownloadLight);
        pnlModel.Controls.Add(_btnQuickSwitch);
        pnlModel.Controls.Add(_btnBrowseModel);
        pnlModel.Controls.Add(_btnOpenFolder);
        pnlModel.Controls.Add(_btnDeleteModel);
        pnlModel.Controls.Add(_btnCancelDownload);
        mainPanel.Controls.Add(pnlModel, 0, 3);

        // 5. Usage Memo Card
        var pnlMemo = CreateCardPanel();
        _lblMemoTitle = new Label
        {
            Text = "3. Памятка по использованию",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 10),
            AutoSize = true
        };
        _lblMemoText = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = Color.FromArgb(200, 205, 215),
            Location = new Point(14, 34),
            Size = new Size(690, 114)
        };
        pnlMemo.Controls.Add(_lblMemoTitle);
        pnlMemo.Controls.Add(_lblMemoText);
        mainPanel.Controls.Add(pnlMemo, 0, 4);

        // 6. Bottom Bar
        var bottomPanel = new Panel { Dock = DockStyle.Fill };
        _cbAutostart = new CheckBox
        {
            Text = "Запускать свернутым в трей при старте Windows",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(220, 225, 230),
            Location = new Point(0, 10),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        _cbAutostart.CheckedChanged += (s, e) => SetAutostart(_cbAutostart.Checked);

        _btnMinimize = new Button
        {
            Text = "Свернуть в трей",
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(55, 60, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(135, 34),
            Location = new Point(445, 6),
            Cursor = Cursors.Hand
        };
        _btnMinimize.FlatAppearance.BorderSize = 0;
        _btnMinimize.Click += (s, e) => HideWindow();

        _btnExit = new Button
        {
            Text = "Выход",
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(75, 40, 45),
            ForeColor = Color.FromArgb(255, 180, 180),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(110, 34),
            Location = new Point(595, 6),
            Cursor = Cursors.Hand
        };
        _btnExit.FlatAppearance.BorderSize = 0;
        _btnExit.Click += (s, e) => FullExit();

        _lblNotification = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(250, 204, 21),
            Location = new Point(0, 28),
            AutoSize = true
        };

        var pnlCredits = new FlowLayoutPanel
        {
            Location = new Point(0, 46),
            Size = new Size(740, 22),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _lblAuthorFooter = new Label
        {
            Text = "Автор: KelTuze (Discord: @keltuze) • ",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(160, 165, 175),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0),
            Cursor = Cursors.Hand
        };
        _lblAuthorFooter.Click += (s, e) =>
        {
            try
            {
                Clipboard.SetText("keltuze");
                ShowNotification(_currentLang == "zh" ? "作者 Discord: keltuze 已复制到剪贴板！" : _currentLang == "en" ? "Author discord: keltuze copied to clipboard!" : "Ник автора keltuze скопирован в буфер обмена!");
            }
            catch { }
        };

        _lnkBoosty = new LinkLabel
        {
            Text = "Отблагодарить: boosty.to/keltuze",
            Font = new Font("Segoe UI", 8.5f),
            LinkColor = Color.FromArgb(249, 115, 22),
            ActiveLinkColor = Color.FromArgb(251, 146, 60),
            VisitedLinkColor = Color.FromArgb(249, 115, 22),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0),
            Cursor = Cursors.Hand
        };
        _lnkBoosty.LinkClicked += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://boosty.to/keltuze", UseShellExecute = true });
            }
            catch { }
        };

        pnlCredits.Controls.Add(_lblAuthorFooter);
        pnlCredits.Controls.Add(_lnkBoosty);

        var pnlVencordCredits = new FlowLayoutPanel
        {
            Location = new Point(0, 64),
            Size = new Size(740, 20),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _lblVencordThanks = new Label
        {
            Text = "Благодарность: интеграция работает на базе ",
            Font = new Font("Segoe UI", 8.2f),
            ForeColor = Color.FromArgb(140, 145, 155),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0)
        };

        _lnkVencord = new LinkLabel
        {
            Text = "Vencord (github.com/Vendicated/Vencord)",
            Font = new Font("Segoe UI", 8.2f),
            LinkColor = Color.FromArgb(96, 165, 250),
            ActiveLinkColor = Color.FromArgb(147, 197, 253),
            VisitedLinkColor = Color.FromArgb(96, 165, 250),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0),
            Cursor = Cursors.Hand
        };
        _lnkVencord.LinkClicked += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Vendicated/Vencord",
                    UseShellExecute = true
                });
            }
            catch { }
        };

        pnlVencordCredits.Controls.Add(_lblVencordThanks);
        pnlVencordCredits.Controls.Add(_lnkVencord);

        var pnlRepoCredits = new FlowLayoutPanel
        {
            Location = new Point(0, 82),
            Size = new Size(740, 20),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _lblRepoFooter = new Label
        {
            Text = "Проект на GitHub (скачать свежую версию): ",
            Font = new Font("Segoe UI", 8.2f),
            ForeColor = Color.FromArgb(140, 145, 155),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0)
        };

        _lnkRepo = new LinkLabel
        {
            Text = "github.com/keltuze/AI-Translator",
            Font = new Font("Segoe UI", 8.2f),
            LinkColor = Color.FromArgb(96, 165, 250),
            ActiveLinkColor = Color.FromArgb(147, 197, 253),
            VisitedLinkColor = Color.FromArgb(96, 165, 250),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0),
            Cursor = Cursors.Hand
        };
        _lnkRepo.LinkClicked += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/keltuze/AI-Translator",
                    UseShellExecute = true
                });
            }
            catch { }
        };

        pnlRepoCredits.Controls.Add(_lblRepoFooter);
        pnlRepoCredits.Controls.Add(_lnkRepo);

        bottomPanel.Controls.Add(_cbAutostart);
        bottomPanel.Controls.Add(_btnMinimize);
        bottomPanel.Controls.Add(_btnExit);
        bottomPanel.Controls.Add(_lblNotification);
        bottomPanel.Controls.Add(pnlCredits);
        bottomPanel.Controls.Add(pnlVencordCredits);
        bottomPanel.Controls.Add(pnlRepoCredits);
        mainPanel.Controls.Add(bottomPanel, 0, 5);

        ApplyLanguage();
    }

    private static Button CreateLangButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
            Size = new Size(42, 26),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 4, 0)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void SwitchLanguage(string lang)
    {
        _currentLang = lang;
        ApplyLanguage();
        SaveConfig();
        SyncVencordLanguage();
    }

    private void SyncVencordLanguage()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsJson = Path.Combine(appData, "Vencord", "settings", "settings.json");
            if (File.Exists(settingsJson))
            {
                var root = JsonNode.Parse(File.ReadAllText(settingsJson))?.AsObject();
                if (root != null && root["plugins"] is JsonObject plugins && plugins["Translate"] is JsonObject tr)
                {
                    tr["uiLanguage"] = _currentLang;
                    File.WriteAllText(settingsJson, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }

            // Keep Vencord renderer.js synchronized with the latest built-in version
            string vcDist = Path.Combine(appData, "Vencord", "dist");
            if (Directory.Exists(vcDist))
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("control_center.vencord.zip");
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream);
                    var entry = archive.GetEntry("dist/renderer.js");
                    if (entry != null)
                    {
                        string targetRenderer = Path.Combine(vcDist, "renderer.js");
                        var fi = new FileInfo(targetRenderer);
                        if (!fi.Exists || fi.Length != entry.Length)
                        {
                            entry.ExtractToFile(targetRenderer, true);
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void ApplyLanguage()
    {
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        // Update button colors
        UpdateLangBtnStyle(_btnLangRu, _currentLang == "ru");
        UpdateLangBtnStyle(_btnLangEn, _currentLang == "en");
        UpdateLangBtnStyle(_btnLangCn, _currentLang == "zh");

        if (_btnCheckUpdate != null && !_isUpdatingApp && !_btnCheckUpdate.Text.Contains("!"))
        {
            _btnCheckUpdate.Text = ru ? $"⚡ Обновить ({AppVersion})" :
                                   zh ? $"⚡ 检查更新 ({AppVersion})" :
                                        $"⚡ Update ({AppVersion})";
        }

        // Header status
        if (_isServerHealthy)
        {
            _lblHeaderStatus.Text = ru ? "🟢 Сервер активен (GPU / Vulkan)" :
                                   zh ? "🟢 服务器运行中 (GPU / Vulkan)" :
                                        "🟢 Server active (GPU / Vulkan)";
            _lblHeaderStatus.ForeColor = Color.FromArgb(34, 197, 94);
        }
        else
        {
            _lblHeaderStatus.Text = ru ? "⚪ Сервер остановлен" :
                                   zh ? "⚪ 服务器已停止" :
                                        "⚪ Server stopped";
            _lblHeaderStatus.ForeColor = Color.FromArgb(156, 163, 175);
        }

        // Server control
        _btnServerToggle.Text = _isServerHealthy ? 
            (ru ? "⏹ Остановить сервер" : zh ? "⏹ 停止服务器" : "⏹ Stop Server") :
            (ru ? "▶ Запустить сервер" : zh ? "▶ 启动服务器" : "▶ Start Server");

        // Discord card
        _lblDiscordTitle.Text = ru ? "1. Интеграция с Discord (плагин Vencord)" :
                               zh ? "1. Discord 整合 (Vencord 插件)" :
                                    "1. Discord Integration (Vencord Plugin)";

        _lblGoogleHint.Text = ru ? "💡 Для Google Translate: достаточно нажать только эту кнопку! Сервер и нейросеть можно не качать." :
                             zh ? "💡 谷歌翻译模式：仅需点击此安装按钮！无需下载模型或启动本地服务器。" :
                                  "💡 For Google Translate: just click this install button! No need to download the model or run the server.";

        UpdateDiscordStatus();

        // Model card
        _btnDownloadModel.Text = ru ? "📥 Скачать базовую модель (Qwen 2.5 7B)" :
                                zh ? "📥 下载推荐模型 (Qwen 2.5 7B)" :
                                     "📥 Download Default Model (Qwen 2.5 7B)";

        _btnBrowseModel.Text = ru ? "📁 Указать свой .gguf файл" :
                              zh ? "📁 选择本地 .gguf 文件" :
                                   "📁 Select .gguf file";

        _btnCancelDownload.Text = ru ? "⏹ Приостановить" :
                                 zh ? "⏹ 暂停" :
                                      "⏹ Pause";

        UpdateModelStatus();

        // Memo card (universal gaming chat)
        _lblMemoTitle.Text = ru ? "3. Памятка по использованию" :
                            zh ? "3. 使用指南" :
                                 "3. Quick Usage Guide";

        _lblMemoText.Text = ru ?
            "• Автоперевод в Discord: значок 文A возле поля ввода горит зелёным — перевод включён (входящие переводятся в чате, а вы пишете и жмёте Enter — улетает на английском).\n" +
            "• Клик по 文A: быстро включить/выключить автоперевод. ПКМ по нему: выбор языков и провайдера (ИИ / Google).\n" +
            "• Два режима: умный ИИ с игровым сленгом (с сервером) или быстрый Google Translate (без сервера).\n" +
            "• Самопроверка: под каждым вашим отправленным сообщением всегда тихой строкой пишется исходный русский перевод.\n" +
            "• Правила перевода: запрет дословных идиом («кусков пирога»), понимание сленга и защита тегов [[T0]]." :
            zh ?
            "• Discord 自动翻译: 输入框旁的 文A 图标亮绿色表示翻译开启 (聊天消息自动翻译，输入母语按 Enter 即自动发送英语)。\n" +
            "• 点击 文A 图标: 快速开启/关闭自动翻译。右键点击: 设置语言与翻译服务 (AI / 谷歌翻译)。\n" +
            "• 两种模式: 本地智能 AI 支持游戏术语 (需启动服务器) 或在线谷歌翻译 (无需服务器)。\n" +
            "• 发送自检: 您发送的每条消息下方都会以小灰字显示您的原文。\n" +
            "• 翻译规则: 禁止直译成语俚语、支持常用游戏聊天术语，并自动保护 [[T0]] 格式标签。" :
            "• Auto-translate in Discord: green 文A icon near the chat bar means translation is ON (incoming messages are translated in chat, and typing + pressing Enter sends in English).\n" +
            "• Click 文A: quickly toggle auto-translate ON/OFF. Right-click: configure languages and translation provider (AI / Google).\n" +
            "• Two modes: Smart local AI with gaming slang (with server) or online Google Translate (no server needed).\n" +
            "• Self-check: beneath every English message you send, your original text is shown in small gray text.\n" +
            "• Translation rules: literal idioms ('piece of cake') prevented, gaming chat slang and [[T0]] tags preserved.";

        // Bottom bar
        _cbAutostart.Text = ru ? "Запускать свернутым в трей при старте Windows" :
                           zh ? "开机自启并最小化到系统托盘" :
                                "Start minimized to system tray on Windows startup";

        _btnMinimize.Text = ru ? "Свернуть в трей" :
                           zh ? "最小化到托盘" :
                                "Minimize to Tray";

        _btnExit.Text = ru ? "Выход" :
                       zh ? "退出" :
                            "Exit";

        _lblAuthorFooter.Text = ru ? "Автор: KelTuze (Discord: @keltuze) • " :
                               zh ? "作者: KelTuze (Discord: @keltuze) • " :
                                    "Author: KelTuze (Discord: @keltuze) • ";

        _lnkBoosty.Text = ru ? "Отблагодарить: boosty.to/keltuze" :
                         zh ? "赞助支持: boosty.to/keltuze" :
                              "Support: boosty.to/keltuze";

        _lblVencordThanks.Text = ru ? "Благодарность: интеграция работает на базе " :
                                 zh ? "特别致谢: Discord 整合基于开源项目 " :
                                      "Special thanks: Discord integration built on ";

        _lblRepoFooter.Text = ru ? "Проект на GitHub (скачать свежую версию): " :
                              zh ? "GitHub 开源主页 (下载最新版本): " :
                                   "GitHub Project (download latest version): ";

        UpdateSidePanelTexts();

        // Tray menu
        _menuItemShow.Text = ru ? "🌐 Панель управления" : zh ? "🌐 控制面板" : "🌐 Control Panel";
        _menuItemExit.Text = ru ? "❌ Выход" : zh ? "❌ 退出" : "❌ Exit";
        _menuItemToggle.Text = _isServerHealthy ?
            (ru ? "⏹ Остановить сервер (VRAM)" : zh ? "⏹ 停止服务器 (释放显存)" : "⏹ Stop Server (VRAM)") :
            (ru ? "▶ Запустить сервер (GPU / Vulkan)" : zh ? "▶ 启动服务器 (GPU / Vulkan)" : "▶ Start Server (GPU / Vulkan)");
    }

    private static void UpdateLangBtnStyle(Button btn, bool isActive)
    {
        if (isActive)
        {
            btn.BackColor = Color.FromArgb(88, 101, 242);
            btn.ForeColor = Color.White;
        }
        else
        {
            btn.BackColor = Color.FromArgb(50, 54, 60);
            btn.ForeColor = Color.FromArgb(200, 205, 215);
        }
    }

    private Panel CreateCardPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(43, 45, 49),
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private void ShowNotification(string message)
    {
        _lblNotification.Text = message;
        _notificationTimer.Stop();
        _notificationTimer.Start();
    }

    private async Task CheckHealthAsync()
    {
        try
        {
            bool isHealthy = false;
            try
            {
                using var resp = await _httpClient.GetAsync(HealthUrl);
                isHealthy = resp.IsSuccessStatusCode;
            }
            catch { }

            if (isHealthy != _isServerHealthy)
            {
                _isServerHealthy = isHealthy;
                UpdateServerUi();
            }
        }
        catch (Exception ex)
        {
            Program.Log($"CheckHealthAsync exception: {ex}");
        }
    }

    private void UpdateServerUi()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(UpdateServerUi));
            return;
        }

        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        if (_isServerHealthy)
        {
            _lblHeaderStatus.Text = ru ? "🟢 Сервер активен (GPU / Vulkan)" :
                                   zh ? "🟢 服务器运行中 (GPU / Vulkan)" :
                                        "🟢 Server active (GPU / Vulkan)";
            _lblHeaderStatus.ForeColor = Color.FromArgb(34, 197, 94);
            _btnServerToggle.Text = ru ? "⏹ Остановить сервер" : zh ? "⏹ 停止服务器" : "⏹ Stop Server";
            _btnServerToggle.BackColor = Color.FromArgb(239, 68, 68);
            _menuItemToggle.Text = ru ? "⏹ Остановить сервер (VRAM)" : zh ? "⏹ 停止服务器 (释放显存)" : "⏹ Stop Server (VRAM)";
            _notifyIcon.Icon = CreateCircleIcon(true);
            _notifyIcon.Text = "AI Translator: Active";
        }
        else
        {
            _lblHeaderStatus.Text = ru ? "⚪ Сервер остановлен" :
                                   zh ? "⚪ 服务器已停止" :
                                        "⚪ Server stopped";
            _lblHeaderStatus.ForeColor = Color.FromArgb(156, 163, 175);
            _btnServerToggle.Text = ru ? "▶ Запустить сервер" : zh ? "▶ 启动服务器" : "▶ Start Server";
            _btnServerToggle.BackColor = Color.FromArgb(34, 197, 94);
            _menuItemToggle.Text = ru ? "▶ Запустить сервер (GPU / Vulkan)" : zh ? "▶ 启动服务器 (GPU / Vulkan)" : "▶ Start Server (GPU / Vulkan)";
            _notifyIcon.Icon = CreateCircleIcon(false);
            _notifyIcon.Text = "AI Translator: Stopped";
        }
    }

    private void ToggleServer()
    {
        if (_isServerHealthy)
        {
            StopServer();
        }
        else
        {
            StartServer();
        }
    }

    private void EnsureEngineExtracted()
    {
        string engineExe = Path.Combine(_baseDir, "engine", "llama-server.exe");
        if (!File.Exists(engineExe))
        {
            try
            {
                string engineDir = Path.Combine(_baseDir, "engine");
                Directory.CreateDirectory(engineDir);
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("control_center.engine.zip");
                if (stream != null)
                {
                    ShowNotification("Распаковка встроенного движка llama.cpp (Vulkan)...");
                    using var archive = new ZipArchive(stream);
                    archive.ExtractToDirectory(engineDir, true);
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка распаковки движка: {ex.Message}");
            }
        }
    }

    private void StartServer()
    {
        EnsureEngineExtracted();
        string engineExe = Path.Combine(_baseDir, "engine", "llama-server.exe");
        if (!File.Exists(engineExe))
        {
            ShowNotification("Ошибка: engine\\llama-server.exe не найден!");
            return;
        }

        string modelPath = GetCurrentModelPath();
        if (!File.Exists(modelPath))
        {
            ShowNotification("Ошибка: файл модели не найден. Сначала скачайте модель!");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = engineExe,
                Arguments = $"-m \"{modelPath}\" -ngl 99 -c 2048 --port 11434 --host 127.0.0.1 -t 4",
                WorkingDirectory = _baseDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) => AppendLog(e.Data);
            proc.ErrorDataReceived += (s, e) => AppendLog(e.Data);
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            ShowNotification("Запуск сервера в видеопамять (GPU / Vulkan)...");
        }
        catch (Exception ex)
        {
            ShowNotification($"Ошибка запуска: {ex.Message}");
        }
    }

    private void StopServer()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/F /IM llama-server.exe",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1000);

            _isServerHealthy = false;
            UpdateServerUi();
            ShowNotification("Сервер остановлен. Видеопамять (VRAM) освобождена!");
        }
        catch (Exception ex)
        {
            ShowNotification($"Ошибка остановки: {ex.Message}");
        }
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        try
        {
            string logFile = Path.Combine(_baseDir, "server.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {line}\n");
        }
        catch { }
    }

    private void UpdateDiscordStatus()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string discordDir = Path.Combine(localAppData, "Discord");
        bool discordExists = Directory.Exists(discordDir);
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        if (!discordExists)
        {
            _lblDiscordStatus.Text = ru ? "🔴 Discord не найден в LocalAppData. Сначала установите официальный Discord." :
                                    zh ? "🔴 未在 LocalAppData 找到 Discord。请先安装官方 Discord。" :
                                         "🔴 Discord not found in LocalAppData. Please install official Discord first.";
            _lblDiscordStatus.ForeColor = Color.FromArgb(239, 68, 68);
            _btnInstallDiscord.Enabled = false;
            _btnUninstallDiscord.Visible = false;
            _btnInstallDiscord.Size = new Size(340, 42);
            _btnInstallDiscord.Location = new Point(365, 18);
            return;
        }

        _btnInstallDiscord.Enabled = true;
        bool isPatched = IsDiscordPatched(localAppData);

        if (isPatched)
        {
            _lblDiscordStatus.Text = ru ? "🟢 Переводчик Vencord установлен и готов к работе в Discord!" :
                                    zh ? "🟢 Vencord 翻译插件已安装并在 Discord 中就绪！" :
                                         "🟢 Vencord translator installed and ready in Discord!";
            _lblDiscordStatus.ForeColor = Color.FromArgb(34, 197, 94);

            _btnInstallDiscord.Text = ru ? "🔄 Обновить плагин" :
                                     zh ? "🔄 更新插件" :
                                          "🔄 Update Plugin";
            _btnInstallDiscord.Size = new Size(198, 42);
            _btnInstallDiscord.Location = new Point(365, 18);

            _btnUninstallDiscord.Text = ru ? "🗑️ Убрать плагин" :
                                       zh ? "🗑️ 卸载插件" :
                                            "🗑️ Remove Plugin";
            _btnUninstallDiscord.Size = new Size(130, 42);
            _btnUninstallDiscord.Location = new Point(575, 18);
            _btnUninstallDiscord.Visible = true;
            _btnUninstallDiscord.Enabled = true;
        }
        else
        {
            _lblDiscordStatus.Text = ru ? "⚪ Плагин не установлен (обычный чистый Discord)." :
                                    zh ? "⚪ 插件未安装 (官方原版 Discord)。" :
                                         "⚪ Plugin not installed (vanilla Discord).";
            _lblDiscordStatus.ForeColor = Color.FromArgb(156, 163, 175);

            _btnInstallDiscord.Text = ru ? "🚀 Установить в Discord (1 клик)" :
                                     zh ? "🚀 安装至 Discord (一键)" :
                                          "🚀 Install in Discord (1 Click)";
            _btnInstallDiscord.Size = new Size(340, 42);
            _btnInstallDiscord.Location = new Point(365, 18);

            _btnUninstallDiscord.Visible = false;
            _btnUninstallDiscord.Enabled = false;
        }
    }

    private bool IsDiscordPatched(string localAppData)
    {
        try
        {
            string discordLocal = Path.Combine(localAppData, "Discord");
            if (!Directory.Exists(discordLocal)) return false;

            var appDirs = Directory.GetDirectories(discordLocal, "app-*");
            if (appDirs.Length == 0) return false;
            Array.Sort(appDirs);
            string latestApp = appDirs[^1];

            string appAsar = Path.Combine(latestApp, "resources", "app.asar");
            string backupAsar = Path.Combine(latestApp, "resources", "_app.asar");

            if (File.Exists(appAsar) && File.Exists(backupAsar))
            {
                var fiApp = new FileInfo(appAsar);
                var fiBackup = new FileInfo(backupAsar);
                return fiApp.Length < 10000 && fiBackup.Length > 100000;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildVencordAsar(string patcherPath)
    {
        string escapedPath = patcherPath.Replace("\\", "\\\\");
        byte[] indexJs = System.Text.Encoding.UTF8.GetBytes($"require(\"{escapedPath}\")");
        byte[] pkgJson = System.Text.Encoding.UTF8.GetBytes("{\n\t\"name\": \"discord\",\n\t\"main\": \"index.js\"\n}");

        string headerJson = $"{{\"files\":{{\"index.js\":{{\"size\":{indexJs.Length},\"offset\":\"0\"}},\"package.json\":{{\"size\":{pkgJson.Length},\"offset\":\"{indexJs.Length}\"}}}}}}";
        byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(headerJson);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((uint)4);
        bw.Write((uint)(headerBytes.Length + 8));
        bw.Write((uint)(headerBytes.Length + 4));
        bw.Write((uint)headerBytes.Length);
        bw.Write(headerBytes);
        bw.Write(indexJs);
        bw.Write(pkgJson);

        return ms.ToArray();
    }

    private void InstallDiscordPlugin()
    {
        try
        {
            // 1. Kill Discord
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/F /IM Discord.exe",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1000);
            Thread.Sleep(500);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string targetVencord = Path.Combine(appData, "Vencord");

            // Copy Vencord files (from embedded resource or disk fallback)
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("control_center.vencord.zip"))
            {
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream);
                    archive.ExtractToDirectory(targetVencord, true);
                }
                else
                {
                    string srcVencord = Path.Combine(_baseDir, "Vencord_Preconfigured");
                    if (Directory.Exists(srcVencord))
                        CopyDirectory(srcVencord, targetVencord);
                }
            }

            // Patch Discord using official Vencord ASAR injector
            string discordLocal = Path.Combine(localAppData, "Discord");
            if (Directory.Exists(discordLocal))
            {
                string patcherPath = Path.Combine(targetVencord, "dist", "patcher.js");
                byte[] vencordAsar = BuildVencordAsar(patcherPath);

                var appDirs = Directory.GetDirectories(discordLocal, "app-*");
                Array.Sort(appDirs);
                foreach (string targetApp in appDirs)
                {
                    string resources = Path.Combine(targetApp, "resources");
                    if (!Directory.Exists(resources)) continue;

                    string appAsar = Path.Combine(resources, "app.asar");
                    string backupAsar = Path.Combine(resources, "_app.asar");
                    string appFolder = Path.Combine(resources, "app");

                    // Clean up any legacy resources\app folder
                    if (Directory.Exists(appFolder))
                    {
                        try { Directory.Delete(appFolder, true); } catch { }
                    }

                    // Ensure original Discord is backed up to _app.asar
                    if (File.Exists(appAsar) && !File.Exists(backupAsar))
                    {
                        File.Move(appAsar, backupAsar);
                    }
                    else if (File.Exists(appAsar) && File.Exists(backupAsar))
                    {
                        var fiApp = new FileInfo(appAsar);
                        if (fiApp.Length > 1000000)
                        {
                            File.Delete(backupAsar);
                            File.Move(appAsar, backupAsar);
                        }
                    }

                    // Write official 217-byte Vencord bootstrap ASAR
                    File.WriteAllBytes(appAsar, vencordAsar);
                }
            }

            // Configure Vencord Translate plugin defaults according to _currentLang
            try
            {
                string settingsDir = Path.Combine(targetVencord, "settings");
                Directory.CreateDirectory(settingsDir);
                string settingsJson = Path.Combine(settingsDir, "settings.json");

                JsonObject root;
                if (File.Exists(settingsJson))
                {
                    root = JsonNode.Parse(File.ReadAllText(settingsJson))?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (!root.ContainsKey("plugins") || root["plugins"] is not JsonObject)
                    root["plugins"] = new JsonObject();
                var plugins = root["plugins"]!.AsObject();

                if (!plugins.ContainsKey("Translate") || plugins["Translate"] is not JsonObject)
                    plugins["Translate"] = new JsonObject();
                var tr = plugins["Translate"]!.AsObject();

                tr["enabled"] = true;
                tr["autoTranslateIncoming"] = true;
                tr["autoTranslate"] = true;
                tr["service"] = "local_ai";

                if (_currentLang == "en")
                {
                    tr["receivedInput"] = "auto";
                    tr["receivedOutput"] = "en";
                    tr["sentInput"] = "en";
                    tr["sentOutput"] = "ru";
                    tr["uiLanguage"] = "en";
                }
                else if (_currentLang == "zh")
                {
                    tr["receivedInput"] = "auto";
                    tr["receivedOutput"] = "zh-CN";
                    tr["sentInput"] = "zh-CN";
                    tr["sentOutput"] = "ru";
                    tr["uiLanguage"] = "zh";
                }
                else
                {
                    tr["receivedInput"] = "auto";
                    tr["receivedOutput"] = "ru";
                    tr["sentInput"] = "ru";
                    tr["sentOutput"] = "en";
                    tr["uiLanguage"] = "ru";
                }

                File.WriteAllText(settingsJson, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }

            UpdateDiscordStatus();

            // Auto-restart Discord
            string updateExe = Path.Combine(localAppData, "Discord", "Update.exe");
            if (File.Exists(updateExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateExe,
                    Arguments = "--processStart Discord.exe",
                    UseShellExecute = true
                });
                ShowNotification(_currentLang == "zh" ? "翻译插件已成功集成至 Discord！Discord 已重启。" :
                                _currentLang == "en" ? "Translator successfully installed to Discord! Discord restarted." :
                                                     "Переводчик успешно внедрен в Discord! Discord перезапущен.");
            }
            else
            {
                ShowNotification(_currentLang == "zh" ? "翻译插件已成功集成至 Discord！请启动 Discord。" :
                                _currentLang == "en" ? "Translator successfully installed! Please start Discord." :
                                                     "Переводчик успешно внедрен в Discord! Запустите Discord.");
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"Ошибка установки: {ex.Message}");
        }
    }

    private void UninstallDiscordPlugin()
    {
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        string confirmMsg = ru ? "Вы действительно хотите удалить плагин перевода и вернуть Discord в стандартный чистый вид?" :
                            zh ? "您确定要卸载翻译插件并将 Discord 恢复为官方原版吗？" :
                                 "Are you sure you want to remove the translation plugin and restore Discord to vanilla?";
        string confirmTitle = ru ? "Удаление плагина" :
                              zh ? "卸载插件" :
                                   "Remove Plugin";

        var dialogResult = MessageBox.Show(confirmMsg, confirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (dialogResult != DialogResult.Yes) return;

        try
        {
            // 1. Kill Discord
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/F /IM Discord.exe",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1500);
            Thread.Sleep(500);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string discordLocal = Path.Combine(localAppData, "Discord");

            if (Directory.Exists(discordLocal))
            {
                var appDirs = Directory.GetDirectories(discordLocal, "app-*");
                foreach (string targetApp in appDirs)
                {
                    string resources = Path.Combine(targetApp, "resources");
                    if (!Directory.Exists(resources)) continue;

                    string appAsar = Path.Combine(resources, "app.asar");
                    string backupAsar = Path.Combine(resources, "_app.asar");
                    string appFolder = Path.Combine(resources, "app");

                    // Clean up resources\app folder
                    if (Directory.Exists(appFolder))
                    {
                        try { Directory.Delete(appFolder, true); } catch { }
                    }

                    // Restore original _app.asar -> app.asar
                    if (File.Exists(backupAsar))
                    {
                        try
                        {
                            if (File.Exists(appAsar))
                                File.Delete(appAsar);
                            File.Move(backupAsar, appAsar);
                        }
                        catch { }
                    }
                }
            }

            UpdateDiscordStatus();

            // Auto-restart Discord in vanilla state
            string updateExe = Path.Combine(localAppData, "Discord", "Update.exe");
            if (File.Exists(updateExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateExe,
                    Arguments = "--processStart Discord.exe",
                    UseShellExecute = true
                });
            }

            ShowNotification(ru ? "✅ Плагин удалён. Discord запущен в исходном чистом виде!" :
                             zh ? "✅ 插件已卸载。Discord 已恢复官方原版并启动！" :
                                  "✅ Plugin removed. Discord started in vanilla mode!");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ru ? $"Ошибка при удалении плагина: {ex.Message}" :
                            zh ? $"卸载插件时出错: {ex.Message}" :
                                 $"Error removing plugin: {ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(src))
        {
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }

    private string GetCurrentModelPath()
    {
        string defaultPath = Path.Combine(_baseDir, "models", ModelFilename);
        string configPath = Path.Combine(_baseDir, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("model_path", out var p))
                {
                    string val = p.GetString() ?? "";
                    if (!string.IsNullOrEmpty(val) && File.Exists(val))
                        return val;
                }
            }
            catch { }
        }
        return defaultPath;
    }

    private void ToggleOfficialModel()
    {
        string current = Path.GetFileName(GetCurrentModelPath());
        string targetFile = current.Equals(LightModelFilename, StringComparison.OrdinalIgnoreCase) 
            ? SmartModelFilename 
            : LightModelFilename;

        string targetPath = Path.Combine(_baseDir, "models", targetFile);
        if (File.Exists(targetPath))
        {
            SaveCustomModelPath(targetPath);
            UpdateModelStatus();

            bool ru = _currentLang == "ru";
            bool zh = _currentLang == "zh";

            if (_isServerHealthy)
            {
                ShowNotification(ru ? "Перезапуск сервера с выбранной моделью..." :
                                 zh ? "正在切换模型并重启服务..." :
                                      "Switching model and restarting server...");
                StopServer();
                Thread.Sleep(800);
                StartServer();
            }
            else
            {
                ShowNotification(ru ? $"Выбрана модель: {targetFile}" :
                                 zh ? $"已选择模型: {targetFile}" :
                                      $"Selected model: {targetFile}");
            }
        }
    }

    private void UpdateModelStatus()
    {
        string path = GetCurrentModelPath();
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        string fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName)) fileName = SmartModelFilename;

        _lblModelPath.Text = (ru ? "Файл: " : zh ? "文件: " : "File: ") + path;

        string smartPath = Path.Combine(_baseDir, "models", SmartModelFilename);
        string lightPath = Path.Combine(_baseDir, "models", LightModelFilename);

        bool smartExists = File.Exists(smartPath) && new FileInfo(smartPath).Length > 50 * 1024 * 1024;
        bool lightExists = File.Exists(lightPath) && new FileInfo(lightPath).Length > 50 * 1024 * 1024;

        if (File.Exists(path) && new FileInfo(path).Length > 50 * 1024 * 1024)
        {
            var info = new FileInfo(path);
            double gb = info.Length / (1024.0 * 1024.0 * 1024.0);

            _lblModelTitle.Text = ru ? $"2. Локальная нейросеть ({fileName})" :
                                  zh ? $"2. 本地大模型 ({fileName})" :
                                       $"2. Local AI Model ({fileName})";

            _lblServerHint.Text = ru ? 
                $"Модель: {fileName} в видеопамяти\nПеред запуском тяжелых 3D-игр нажмите «Остановить», чтобы освободить {Math.Ceiling(gb):F0} ГБ VRAM." :
                zh ?
                $"模型: {fileName} 载入显存\n在启动大型 3D 游戏前，点击“停止”可释放 {Math.Ceiling(gb):F0} GB 显存。" :
                $"Model: {fileName} in VRAM\nBefore launching heavy 3D games, click 'Stop' to release {Math.Ceiling(gb):F0} GB VRAM.";

            _lblModelStatus.Text = ru ? $"🟢 Готова ({gb:F2} ГБ)" :
                                   zh ? $"🟢 就绪 ({gb:F2} GB)" :
                                        $"🟢 Ready ({gb:F2} GB)";
            _lblModelStatus.ForeColor = Color.FromArgb(34, 197, 94);

            _btnOpenFolder.Text = ru ? "📂 Папка" : zh ? "📂 文件夹" : "📂 Folder";
            _btnOpenFolder.Location = new Point(244, 76);
            _btnOpenFolder.Size = new Size(130, 36);
            _btnOpenFolder.Visible = true;

            _btnBrowseModel.Text = ru ? "📁 Свой .gguf" : zh ? "📁 更改文件" : "📁 Browse";
            _btnBrowseModel.Location = new Point(384, 76);
            _btnBrowseModel.Size = new Size(140, 36);
            _btnBrowseModel.Visible = true;

            _btnDeleteModel.Text = ru ? $"🗑️ Удалить ({gb:F2} ГБ)" :
                                  zh ? $"🗑️ 删除 ({gb:F2} GB)" :
                                       $"🗑️ Delete ({gb:F2} GB)";
            _btnDeleteModel.Location = new Point(534, 76);
            _btnDeleteModel.Size = new Size(170, 36);
            _btnDeleteModel.Visible = true;

            if (smartExists && lightExists)
            {
                bool isActiveLight = fileName.Equals(LightModelFilename, StringComparison.OrdinalIgnoreCase);
                _btnQuickSwitch.Text = isActiveLight ?
                    (ru ? "🧠 Включить Qwen 7B" : zh ? "🧠 切换至 Qwen 7B" : "🧠 Switch to Qwen 7B") :
                    (ru ? "⚡ Включить Index 2B" : zh ? "⚡ 切换至 Index 2B" : "⚡ Switch to Index 2B");
                _btnQuickSwitch.Location = new Point(14, 76);
                _btnQuickSwitch.Size = new Size(220, 36);
                _btnQuickSwitch.Visible = true;
                _btnDownloadModel.Visible = false;
                _btnDownloadLight.Visible = false;
            }
            else if (!lightExists)
            {
                _btnDownloadLight.Text = ru ? "⚡ Скачать легкую (2 ГБ)" : zh ? "⚡ 下载轻量模型 (2 GB)" : "⚡ Download Light (2 GB)";
                _btnDownloadLight.Location = new Point(14, 76);
                _btnDownloadLight.Size = new Size(220, 36);
                _btnDownloadLight.Visible = true;
                _btnDownloadModel.Visible = false;
                _btnQuickSwitch.Visible = false;
            }
            else if (!smartExists)
            {
                _btnDownloadModel.Text = ru ? "🧠 Скачать умную (4.7 ГБ)" : zh ? "🧠 下载聪明大模型 (4.7 GB)" : "🧠 Download Smart (4.7 GB)";
                _btnDownloadModel.Location = new Point(14, 76);
                _btnDownloadModel.Size = new Size(220, 36);
                _btnDownloadModel.Visible = true;
                _btnDownloadLight.Visible = false;
                _btnQuickSwitch.Visible = false;
            }
            else
            {
                _btnDownloadModel.Visible = false;
                _btnDownloadLight.Visible = false;
                _btnQuickSwitch.Visible = false;
            }
        }
        else
        {
            _lblModelTitle.Text = ru ? "2. Локальная нейросеть (выберите модель для скачивания)" :
                                  zh ? "2. 本地大模型 (请选择要下载的模型)" :
                                       "2. Local AI Model (select model to download)";

            _lblServerHint.Text = ru ? 
                "Модель не загружена в видеопамять\nВыберите Умную (для мощных ПК) или Легкую (для ноутбуков/слабых карт)." :
                zh ?
                "模型未载入显存\n请选择聪明大模型 (推荐) 或游戏轻量模型 (低显存)。" :
                "Model not loaded in VRAM\nSelect Smart Model (recommended) or Light Gaming (low VRAM).";

            _lblModelStatus.Text = ru ? "🔴 Не скачана" : zh ? "🔴 未下载" : "🔴 Not downloaded";
            _lblModelStatus.ForeColor = Color.FromArgb(239, 68, 68);

            _btnDownloadModel.Text = ru ? "🧠 1. Умная (Qwen 7B ~4.7 ГБ)" :
                                     zh ? "🧠 1. 聪明模型 (Qwen 7B ~4.7 GB)" :
                                          "🧠 1. Smart (Qwen 7B ~4.7 GB)";
            _btnDownloadModel.Location = new Point(14, 76);
            _btnDownloadModel.Size = new Size(260, 36);
            _btnDownloadModel.Visible = true;

            _btnDownloadLight.Text = ru ? "⚡ 2. Легкая (Index 2B ~2.0 ГБ)" :
                                     zh ? "⚡ 2. 游戏轻量 (Index 2B ~2.0 GB)" :
                                          "⚡ 2. Light Gaming (Index 2B ~2.0 GB)";
            _btnDownloadLight.Location = new Point(284, 76);
            _btnDownloadLight.Size = new Size(260, 36);
            _btnDownloadLight.Visible = true;

            _btnBrowseModel.Text = ru ? "📁 Свой .gguf" : zh ? "📁 本地 .gguf" : "📁 Browse .gguf";
            _btnBrowseModel.Location = new Point(554, 76);
            _btnBrowseModel.Size = new Size(150, 36);
            _btnBrowseModel.Visible = true;

            _btnOpenFolder.Visible = false;
            _btnDeleteModel.Visible = false;
            _btnQuickSwitch.Visible = false;
        }
    }

    private void OpenModelFolder()
    {
        try
        {
            string path = GetCurrentModelPath();
            string modelsDir = Path.GetDirectoryName(path) ?? Path.Combine(_baseDir, "models");
            if (!Directory.Exists(modelsDir))
                Directory.CreateDirectory(modelsDir);

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{modelsDir}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"Ошибка открытия папки: {ex.Message}");
        }
    }

    private void DeleteModelFile()
    {
        string path = GetCurrentModelPath();
        if (!File.Exists(path)) return;

        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        var info = new FileInfo(path);
        double gb = info.Length / (1024.0 * 1024.0 * 1024.0);

        string confirmMsg = ru ? $"Вы действительно хотите удалить файл нейросети ({gb:F2} ГБ), чтобы освободить место на диске?\n\nФайл:\n{path}\n\n(При необходимости вы сможете скачать модель заново в 1 клик или использовать бесплатный режим Google Translate)." :
                            zh ? $"您确定要删除此神经网络模型文件 ({gb:F2} GB) 以释放磁盘空间吗？\n\n文件:\n{path}\n\n(之后您随时可以重新一键下载，或直接使用无需服务器的谷歌翻译模式)。" :
                                 $"Are you sure you want to delete the neural model file ({gb:F2} GB) to free up disk space?\n\nFile:\n{path}\n\n(You can re-download it anytime in 1 click, or use online Google Translate mode without the model).";
        string confirmTitle = ru ? "Удаление модели" :
                              zh ? "删除模型" :
                                   "Delete Model";

        var res = MessageBox.Show(confirmMsg, confirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (res != DialogResult.Yes) return;

        try
        {
            if (_isServerHealthy)
            {
                StopServer();
                Thread.Sleep(1000);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            string partFile = path + ".part";
            if (File.Exists(partFile))
            {
                try { File.Delete(partFile); } catch { }
            }

            string smartPath = Path.Combine(_baseDir, "models", SmartModelFilename);
            string lightPath = Path.Combine(_baseDir, "models", LightModelFilename);

            if (path.Equals(smartPath, StringComparison.OrdinalIgnoreCase) && File.Exists(lightPath))
            {
                SaveCustomModelPath(lightPath);
            }
            else if (path.Equals(lightPath, StringComparison.OrdinalIgnoreCase) && File.Exists(smartPath))
            {
                SaveCustomModelPath(smartPath);
            }
            else
            {
                SaveCustomModelPath("");
            }

            UpdateModelStatus();

            ShowNotification(ru ? $"✅ Файл модели удален. Освобождено {gb:F2} ГБ на диске!" :
                             zh ? $"✅ 模型文件已删除。已释放 {gb:F2} GB 磁盘空间！" :
                                  $"✅ Model file deleted. Freed {gb:F2} GB of disk space!");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ru ? $"Не удалось удалить файл (возможно, он занят другим процессом): {ex.Message}" :
                            zh ? $"无法删除文件 (可能正被其他进程占用): {ex.Message}" :
                                 $"Failed to delete file (may be locked by another process): {ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseModelFile()
    {
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        string modelsDir = Path.Combine(_baseDir, "models");
        if (!Directory.Exists(modelsDir))
        {
            try { Directory.CreateDirectory(modelsDir); } catch { }
        }

        using var ofd = new OpenFileDialog
        {
            Title = ru ? "Выберите файл модели нейросети (GGUF)" :
                    zh ? "选择本地大模型文件 (GGUF)" :
                         "Select AI Model File (GGUF)",
            Filter = "GGUF Models (*.gguf)|*.gguf|All Files (*.*)|*.*",
            InitialDirectory = modelsDir
        };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            SaveCustomModelPath(ofd.FileName);
            UpdateModelStatus();

            if (_isServerHealthy)
            {
                ShowNotification(ru ? "Перезапуск сервера с новой моделью..." :
                                 zh ? "正在使用新模型重启服务..." :
                                      "Restarting server with new model...");
                StopServer();
                Thread.Sleep(800);
                StartServer();
            }
            else
            {
                ShowNotification(ru ? "Модель выбрана! Нажмите «Запустить сервер»." :
                                 zh ? "模型已选定！点击“启动服务器”。" :
                                      "Model selected! Click 'Start Server'.");
            }
        }
    }

    private void LoadConfig()
    {
        string configPath = Path.Combine(_baseDir, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("lang", out var l))
                {
                    string val = l.GetString() ?? "";
                    if (val == "en" || val == "zh" || val == "ru")
                        _currentLang = val;
                }
            }
            catch { }
        }
        else
        {
            string sysLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
            if (sysLang == "zh") _currentLang = "zh";
            else if (sysLang == "ru") _currentLang = "ru";
            else _currentLang = "ru";
        }
    }

    private void SaveConfig()
    {
        try
        {
            string configPath = Path.Combine(_baseDir, "config.json");
            string modelPath = GetCurrentModelPath();
            var cfg = new { model_path = modelPath, lang = _currentLang };
            File.WriteAllText(configPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void SaveCustomModelPath(string path)
    {
        try
        {
            string configPath = Path.Combine(_baseDir, "config.json");
            var cfg = new { model_path = path, lang = _currentLang };
            File.WriteAllText(configPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void StartDownload(bool isLight = false)
    {
        string modelsDir = Path.Combine(_baseDir, "models");
        Directory.CreateDirectory(modelsDir);

        string targetFilename = isLight ? LightModelFilename : SmartModelFilename;
        string targetUrl = isLight ? LightModelUrl : SmartModelUrl;
        long targetBytes = isLight ? LightModelBytes : SmartModelBytes;

        string destFile = Path.Combine(modelsDir, targetFilename);
        string partFile = destFile + ".part";

        _btnDownloadModel.Visible = false;
        _btnDownloadLight.Visible = false;
        _btnQuickSwitch.Visible = false;
        _btnBrowseModel.Visible = false;
        _btnOpenFolder.Visible = false;
        _btnDeleteModel.Visible = false;
        _pbDownload.Visible = true;
        _lblDownloadProgress.Visible = true;
        _btnCancelDownload.Visible = true;

        _downloadCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = $"-L -C - --fail --retry 5 --retry-delay 2 -o \"{partFile}\" \"{targetUrl}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                _curlDownloadProcess = Process.Start(psi);
                long lastBytes = File.Exists(partFile) ? new FileInfo(partFile).Length : 0;
                var lastTime = DateTime.UtcNow;

                while (_curlDownloadProcess != null && !_curlDownloadProcess.HasExited)
                {
                    await Task.Delay(500);

                    if (_downloadCts.Token.IsCancellationRequested)
                    {
                        try { _curlDownloadProcess?.Kill(); } catch { }
                        break;
                    }

                    if (File.Exists(partFile))
                    {
                        var info = new FileInfo(partFile);
                        long currentBytes = info.Length;
                        var now = DateTime.UtcNow;
                        double elapsed = (now - lastTime).TotalSeconds;

                        if (elapsed >= 0.5)
                        {
                            double speedBps = (currentBytes - lastBytes) / elapsed;
                            lastBytes = currentBytes;
                            lastTime = now;

                            int percent = (int)Math.Clamp((currentBytes * 100) / targetBytes, 0, 100);
                            double mbDown = currentBytes / (1024.0 * 1024.0);
                            double mbTotal = targetBytes / (1024.0 * 1024.0);
                            double mbSpeed = speedBps / (1024.0 * 1024.0);

                            BeginInvoke(() =>
                            {
                                _pbDownload.Value = percent;
                                string tag = isLight ? "Index 2B" : "Qwen 7B";
                                _lblDownloadProgress.Text = $"Загрузка ({tag}): {mbDown:F1} / {mbTotal:F1} МБ ({percent}%) — {mbSpeed:F1} МБ/с";
                            });
                        }
                    }
                }

                if (_curlDownloadProcess?.ExitCode == 0 && File.Exists(partFile))
                {
                    var finalInfo = new FileInfo(partFile);
                    long minAcceptable = isLight ? 1900000000L : 4500000000L;
                    if (finalInfo.Length >= minAcceptable)
                    {
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(partFile, destFile);

                        BeginInvoke(() =>
                        {
                            _pbDownload.Visible = false;
                            _lblDownloadProgress.Visible = false;
                            _btnCancelDownload.Visible = false;
                            SaveCustomModelPath(destFile);
                            UpdateModelStatus();
                            bool ru = _currentLang == "ru";
                            bool zh = _currentLang == "zh";
                            ShowNotification(ru ? "Модель успешно загружена и готова к работе!" :
                                             zh ? "模型下载成功，已就绪！" :
                                                  "Model successfully downloaded and ready!");
                        });
                        return;
                    }
                }

                BeginInvoke(() =>
                {
                    _pbDownload.Visible = false;
                    _lblDownloadProgress.Visible = false;
                    _btnCancelDownload.Visible = false;
                    UpdateModelStatus();
                    bool ru = _currentLang == "ru";
                    bool zh = _currentLang == "zh";
                    ShowNotification(ru ? "Загрузка приостановлена." : zh ? "下载已暂停。" : "Download paused.");
                });
            }
            catch (Exception ex)
            {
                BeginInvoke(() =>
                {
                    _pbDownload.Visible = false;
                    _lblDownloadProgress.Visible = false;
                    _btnCancelDownload.Visible = false;
                    UpdateModelStatus();
                    ShowNotification($"Ошибка загрузки: {ex.Message}");
                });
            }
        });
    }

    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    private static bool IsAutostartEnabled()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string shortcutPath = Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\Startup\AI_Translator.lnk");
        return File.Exists(shortcutPath);
    }

    private static void SetAutostart(bool enable)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string shortcutPath = Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\Startup\AI_Translator.lnk");

            if (enable)
            {
                string exePath = Application.ExecutablePath;
                string script = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{exePath}'; $s.Arguments = '-minimized'; $s.Save()";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            else
            {
                if (File.Exists(shortcutPath))
                    File.Delete(shortcutPath);
            }
        }
        catch { }
    }

    private async void CheckForUpdates(bool isManualClick = true)
    {
        if (_isUpdatingApp) return;

        try
        {
            if (isManualClick)
            {
                _btnCheckUpdate.Text = _currentLang == "zh" ? "⏳ 检查中..." : _currentLang == "en" ? "⏳ Checking..." : "⏳ Проверка...";
                _btnCheckUpdate.Cursor = Cursors.WaitCursor;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Translator-App");
            client.Timeout = TimeSpan.FromSeconds(10);

            string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            var resp = await client.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
            {
                if (isManualClick)
                {
                    ShowNotification(_currentLang == "zh" ? "未能连接到 GitHub Releases" : _currentLang == "en" ? "Failed to check GitHub releases" : "Релизы на GitHub пока не найдены или нет связи");
                }
                return;
            }

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string releaseName = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? tagName : tagName;
            string body = root.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? "" : "";

            // Find download URL for AI_Translator.exe
            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string assetName = asset.GetProperty("name").GetString() ?? "";
                    if (assetName.Equals("AI_Translator.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            bool isNewer = IsVersionNewer(tagName, AppVersion);

            if (isNewer && !string.IsNullOrEmpty(downloadUrl))
            {
                _btnCheckUpdate.Text = $"⚡ {tagName}!";
                _btnCheckUpdate.BackColor = Color.FromArgb(249, 115, 22); // Orange alert

                string msg = _currentLang == "zh" ?
                    $"发现新版本 {tagName}!\n\n更新内容:\n{body}\n\n立即自动下载并更新程序？" :
                    _currentLang == "en" ?
                    $"New version {tagName} available!\n\nChangelog:\n{body}\n\nDownload and update now?" :
                    $"Доступна новая версия {tagName}!\n\nЧто нового:\n{body}\n\nСкачать и обновить программу сейчас?";

                var dialogRes = MessageBox.Show(msg, "AI Translator Update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (dialogRes == DialogResult.Yes)
                {
                    await PerformSelfUpdateAsync(downloadUrl, tagName);
                }
            }
            else
            {
                _btnCheckUpdate.Text = _currentLang == "ru" ? $"⚡ Обновить ({AppVersion})" :
                                       _currentLang == "zh" ? $"⚡ 检查更新 ({AppVersion})" :
                                            $"⚡ Update ({AppVersion})";
                _btnCheckUpdate.BackColor = Color.FromArgb(55, 60, 68);

                if (isManualClick)
                {
                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        string msg = _currentLang == "zh" ?
                            $"您已在使用最新版本 ({AppVersion})。\n是否重新下载最新 Release 覆盖更新？" :
                            _currentLang == "en" ?
                            $"You have the latest version ({AppVersion}).\nDo you want to re-download and reinstall the latest Release?" :
                            $"У вас уже установлена последняя версия ({AppVersion}).\nХотите принудительно перекачать свежую сборку с GitHub?";

                        var res = MessageBox.Show(msg, "AI Translator", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (res == DialogResult.Yes)
                        {
                            await PerformSelfUpdateAsync(downloadUrl, tagName);
                        }
                    }
                    else
                    {
                        ShowNotification(_currentLang == "zh" ? $"已是最新版本 ({AppVersion})" : _currentLang == "en" ? $"Already up to date ({AppVersion})" : $"Установлена актуальная версия ({AppVersion})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (isManualClick)
            {
                ShowNotification("Ошибка проверки: " + ex.Message);
            }
        }
        finally
        {
            if (!_isUpdatingApp)
            {
                _btnCheckUpdate.Cursor = Cursors.Hand;
            }
        }
    }

    private async Task PerformSelfUpdateAsync(string downloadUrl, string newVersion)
    {
        if (_isUpdatingApp) return;

        string exePath = Environment.ProcessPath ?? Path.Combine(_baseDir, "AI_Translator.exe");
        string tempNewExe = Path.Combine(Path.GetTempPath(), $"AI_Translator_update_{Guid.NewGuid():N}.exe");
        string updaterScript = Path.Combine(Path.GetTempPath(), "ai_translator_updater.bat");

        _isUpdatingApp = true;
        _updateCts = new CancellationTokenSource();

        try
        {
            _btnCheckUpdate.Cursor = Cursors.WaitCursor;
            _btnCheckUpdate.BackColor = Color.FromArgb(37, 99, 235); // Vivid Blue Accent
            _btnCheckUpdate.ForeColor = Color.White;
            _btnCheckUpdate.Text = "⏳ 0%";
            _pbAppUpdate.Value = 0;
            _pbAppUpdate.Visible = true;

            _notificationTimer.Stop();
            _lblNotification.Text = _currentLang == "zh" ? "正在连接 GitHub..." : _currentLang == "en" ? "Connecting to GitHub..." : "Подключение к GitHub...";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Translator-App");
            using var resp = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, _updateCts.Token);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? -1L;
            double totalMb = totalBytes > 0 ? totalBytes / (1024.0 * 1024.0) : 0;

            using (var contentStream = await resp.Content.ReadAsStreamAsync(_updateCts.Token))
            using (var fs = new FileStream(tempNewExe, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long lastUpdateTick = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _updateCts.Token)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead), _updateCts.Token);
                    totalRead += bytesRead;

                    if (sw.ElapsedMilliseconds - lastUpdateTick > 100 || (totalBytes > 0 && totalRead == totalBytes))
                    {
                        lastUpdateTick = sw.ElapsedMilliseconds;
                        double downloadedMb = totalRead / (1024.0 * 1024.0);
                        double elapsedSec = sw.Elapsed.TotalSeconds;
                        double speedMb = elapsedSec > 0.1 ? (downloadedMb / elapsedSec) : 0;

                        if (totalBytes > 0)
                        {
                            int pct = (int)Math.Clamp((totalRead * 100) / totalBytes, 0, 100);
                            _pbAppUpdate.Value = pct;
                            _btnCheckUpdate.Text = $"⏳ {pct}% ({downloadedMb:F0}/{totalMb:F0} МБ)";
                            _lblNotification.Text = _currentLang == "zh" ?
                                $"正在下载更新: {pct}% ({downloadedMb:F1}/{totalMb:F1} MB) — {speedMb:F1} MB/s" :
                                _currentLang == "en" ?
                                $"Downloading update: {pct}% ({downloadedMb:F1}/{totalMb:F1} MB) — {speedMb:F1} MB/s" :
                                $"Скачивание обновления {newVersion}: {pct}% ({downloadedMb:F1}/{totalMb:F1} МБ) — {speedMb:F1} МБ/с";
                        }
                        else
                        {
                            _btnCheckUpdate.Text = $"⏳ {downloadedMb:F1} МБ";
                            _lblNotification.Text = $"Скачивание: {downloadedMb:F1} МБ — {speedMb:F1} МБ/с";
                        }
                    }
                }
            }

            if (new FileInfo(tempNewExe).Length < 1024 * 1024)
            {
                throw new Exception("Скачанный файл повреждён или слишком мал.");
            }

            _pbAppUpdate.Value = 100;
            _btnCheckUpdate.BackColor = Color.FromArgb(22, 163, 74); // Success green
            _btnCheckUpdate.Text = "✅ Перезапуск...";
            _lblNotification.Text = _currentLang == "zh" ? "更新下载完成，正在重启..." : _currentLang == "en" ? "Update ready, restarting..." : "Обновление скачано! Перезапуск программы...";

            // Write batch updater
            int pid = Process.GetCurrentProcess().Id;
            string batContent = $@"@echo off
chcp 65001 >nul
timeout /t 1 /nobreak >nul
taskkill /F /PID {pid} >nul 2>&1
timeout /t 1 /nobreak >nul
copy /y ""{tempNewExe}"" ""{exePath}"" >nul
start """" ""{exePath}""
del ""{tempNewExe}"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
";
            File.WriteAllText(updaterScript, batContent, System.Text.Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterScript,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            _isExiting = true;
            Application.Exit();
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(tempNewExe)) File.Delete(tempNewExe); } catch { }
            _pbAppUpdate.Visible = false;
            _btnCheckUpdate.Text = _currentLang == "ru" ? $"⚡ Обновить ({AppVersion})" :
                                   _currentLang == "zh" ? $"⚡ 检查更新 ({AppVersion})" :
                                        $"⚡ Update ({AppVersion})";
            _btnCheckUpdate.BackColor = Color.FromArgb(55, 60, 68);
            _btnCheckUpdate.Cursor = Cursors.Hand;
            ShowNotification("Скачивание обновления отменено.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempNewExe)) File.Delete(tempNewExe); } catch { }
            _pbAppUpdate.Visible = false;
            _btnCheckUpdate.Text = _currentLang == "ru" ? $"⚡ Обновить ({AppVersion})" :
                                   _currentLang == "zh" ? $"⚡ 检查更新 ({AppVersion})" :
                                        $"⚡ Update ({AppVersion})";
            _btnCheckUpdate.BackColor = Color.FromArgb(55, 60, 68);
            _btnCheckUpdate.Cursor = Cursors.Hand;
            MessageBox.Show("Ошибка обновления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isUpdatingApp = false;
            _updateCts?.Dispose();
            _updateCts = null;
        }
    }

    private static bool IsVersionNewer(string remoteTag, string localTag)
    {
        try
        {
            string r = remoteTag.TrimStart('v', 'V').Trim();
            string l = localTag.TrimStart('v', 'V').Trim();
            if (Version.TryParse(r, out var vr) && Version.TryParse(l, out var vl))
            {
                return vr > vl;
            }
            return !string.Equals(remoteTag, localTag, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ==========================================
    // Quick Translate Side Panel Implementation
    // ==========================================
    private void CreateSideTranslatorPanel()
    {
        _pnlSideTranslator = new Panel
        {
            Dock = DockStyle.Left,
            Width = SidePanelWidth,
            BackColor = Color.FromArgb(24, 25, 29),
            ForeColor = Color.White,
            Visible = false,
            Padding = new Padding(14)
        };

        // Right separator border
        _pnlSideTranslator.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(45, 49, 58), 1);
            e.Graphics.DrawLine(pen, _pnlSideTranslator.Width - 1, 0, _pnlSideTranslator.Width - 1, _pnlSideTranslator.Height);
        };

        // Header: Title + Close Button
        _lblSideTitle = new Label
        {
            Text = "📝 Ручной перевод",
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 12),
            Size = new Size(280, 26),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _btnCloseSide = new Button
        {
            Text = "✕",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(45, 49, 58),
            ForeColor = Color.FromArgb(200, 205, 215),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 28),
            Location = new Point(338, 10),
            Cursor = Cursors.Hand
        };
        _btnCloseSide.FlatAppearance.BorderSize = 0;
        _btnCloseSide.Click += (s, e) => CloseSidePanel();

        // Top Bar: Source Language (Auto-detect) + Paste + Clear
        _cbSourceLanguage = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(36, 39, 45),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(14, 44),
            Size = new Size(165, 28)
        };

        _btnSidePaste = new Button
        {
            Text = "📋 Вставить",
            Font = new Font("Segoe UI", 8.8f),
            BackColor = Color.FromArgb(45, 49, 58),
            ForeColor = Color.FromArgb(220, 225, 235),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(92, 28),
            Location = new Point(184, 44),
            Cursor = Cursors.Hand
        };
        _btnSidePaste.FlatAppearance.BorderSize = 0;
        _btnSidePaste.Click += (s, e) =>
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string paste = Clipboard.GetText();
                    _txtSideInput.Text = paste;
                    _txtSideInput.SelectionStart = _txtSideInput.Text.Length;
                    _txtSideInput.Focus();
                }
            }
            catch { }
        };

        _btnSideClear = new Button
        {
            Text = "✕ Очистить",
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.FromArgb(38, 41, 48),
            ForeColor = Color.FromArgb(156, 163, 175),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(86, 28),
            Location = new Point(280, 44),
            Cursor = Cursors.Hand
        };
        _btnSideClear.FlatAppearance.BorderSize = 0;
        _btnSideClear.Click += (s, e) =>
        {
            _autoTranslateDebounceTimer?.Stop();
            _autoTranslateDebounceTimer?.Dispose();
            _autoTranslateDebounceTimer = null;
            _lastTranslatedText = "";
            _txtSideInput.Text = "";
            _txtSideOutput.Text = "";
            _lblSideStatus.Text = "";
            _txtSideInput.Focus();
        };

        // Top Box: Input TextBox (Height = 195)
        _txtSideInput = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 9.5f),
            BackColor = Color.FromArgb(32, 34, 39),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(14, 76),
            Size = new Size(352, 195)
        };
        _txtSideInput.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = PerformQuickTranslationAsync();
            }
        };

        // Auto-translate listener with debounce & 1-to-1 unchanged text suppression
        _txtSideInput.TextChanged += (s, e) =>
        {
            if (!_isAutoTranslateEnabled) return;

            string current = _txtSideInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                _txtSideOutput.Text = "";
                _lblSideStatus.Text = "";
                _lastTranslatedText = "";
                return;
            }

            // If text is 1-in-1 unchanged, do not translate!
            if (current == _lastTranslatedText)
            {
                return;
            }

            _autoTranslateDebounceTimer?.Stop();
            _autoTranslateDebounceTimer?.Dispose();
            _autoTranslateDebounceTimer = new System.Windows.Forms.Timer { Interval = 450 };
            _autoTranslateDebounceTimer.Tick += (ts, te) =>
            {
                _autoTranslateDebounceTimer?.Stop();
                _autoTranslateDebounceTimer?.Dispose();
                _autoTranslateDebounceTimer = null;

                if (!_isAutoTranslateEnabled) return;
                string toTrans = _txtSideInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(toTrans) && toTrans != _lastTranslatedText)
                {
                    _ = PerformQuickTranslationAsync();
                }
            };
            _autoTranslateDebounceTimer.Start();
        };

        // Middle Row: Prominent Translate Button + Auto-translate Toggle Button
        _btnSideTranslate = new Button
        {
            Text = "⚡ Перевести (Ctrl+Enter)",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(224, 40),
            Location = new Point(14, 277),
            Cursor = Cursors.Hand
        };
        _btnSideTranslate.FlatAppearance.BorderSize = 0;
        _btnSideTranslate.Click += (s, e) => _ = PerformQuickTranslationAsync();

        _btnAutoTranslate = new Button
        {
            Text = "⚡ Авто: ВЫКЛ",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(45, 49, 58),
            ForeColor = Color.FromArgb(160, 165, 175),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(122, 40),
            Location = new Point(244, 277),
            Cursor = Cursors.Hand
        };
        _btnAutoTranslate.FlatAppearance.BorderSize = 0;
        _btnAutoTranslate.Click += (s, e) =>
        {
            _isAutoTranslateEnabled = !_isAutoTranslateEnabled;
            UpdateAutoTranslateButtonStyle();

            if (_isAutoTranslateEnabled)
            {
                string text = _txtSideInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text != _lastTranslatedText)
                {
                    _ = PerformQuickTranslationAsync();
                }
            }
            else
            {
                _autoTranslateDebounceTimer?.Stop();
                _autoTranslateDebounceTimer?.Dispose();
                _autoTranslateDebounceTimer = null;
            }
        };

        // Status Label under the button
        _lblSideStatus = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(156, 163, 175),
            Location = new Point(14, 321),
            Size = new Size(352, 18),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Bottom Bar: Target Language Selection ("На язык:") + Swap + Large Copy Button
        _lblTargetLang = new Label
        {
            Text = "На язык:",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            Location = new Point(14, 347),
            Size = new Size(52, 22),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _cbTargetLanguage = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.FromArgb(36, 39, 45),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(68, 344),
            Size = new Size(100, 28)
        };

        _btnSwapLanguages = new Button
        {
            Text = "⇄",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(45, 49, 58),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(32, 28),
            Location = new Point(172, 344),
            Cursor = Cursors.Hand
        };
        _btnSwapLanguages.FlatAppearance.BorderSize = 0;
        _btnSwapLanguages.Click += (s, e) => SwapTranslationDirection();

        // Extra wide & large copy button so it's effortless to click!
        _btnSideCopy = new Button
        {
            Text = "📋 Скопировать",
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(156, 30),
            Location = new Point(210, 342),
            Cursor = Cursors.Hand
        };
        _btnSideCopy.FlatAppearance.BorderSize = 0;
        _btnSideCopy.Click += (s, e) =>
        {
            string outText = _txtSideOutput.Text;
            if (!string.IsNullOrWhiteSpace(outText))
            {
                try
                {
                    Clipboard.SetText(outText);
                    ShowNotification(_currentLang switch { "zh" => "翻译结果已复制！", "en" => "Translation copied!", _ => "Перевод скопирован в буфер!" });
                }
                catch { }
            }
        };

        // Bottom Box: Output TextBox (Height = 290)
        _txtSideOutput = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 9.5f),
            BackColor = Color.FromArgb(18, 19, 22),
            ForeColor = Color.FromArgb(240, 243, 250),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(14, 376),
            Size = new Size(352, 290)
        };

        // Bottom Hint Label
        _lblSideHint = new Label
        {
            Text = "💡 Термины Starsector, сленг и фракции",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(107, 114, 128),
            Location = new Point(14, 672),
            Size = new Size(352, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };

        PopulateTranslateLanguages();
        UpdateAutoTranslateButtonStyle();

        _pnlSideTranslator.Controls.Add(_lblSideTitle);
        _pnlSideTranslator.Controls.Add(_btnCloseSide);
        _pnlSideTranslator.Controls.Add(_cbSourceLanguage);
        _pnlSideTranslator.Controls.Add(_btnSidePaste);
        _pnlSideTranslator.Controls.Add(_btnSideClear);
        _pnlSideTranslator.Controls.Add(_txtSideInput);
        _pnlSideTranslator.Controls.Add(_btnSideTranslate);
        _pnlSideTranslator.Controls.Add(_btnAutoTranslate);
        _pnlSideTranslator.Controls.Add(_lblSideStatus);
        _pnlSideTranslator.Controls.Add(_lblTargetLang);
        _pnlSideTranslator.Controls.Add(_cbTargetLanguage);
        _pnlSideTranslator.Controls.Add(_btnSwapLanguages);
        _pnlSideTranslator.Controls.Add(_btnSideCopy);
        _pnlSideTranslator.Controls.Add(_txtSideOutput);
        _pnlSideTranslator.Controls.Add(_lblSideHint);
    }

    private void UpdateAutoTranslateButtonStyle()
    {
        if (_btnAutoTranslate == null) return;
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        if (_isAutoTranslateEnabled)
        {
            _btnAutoTranslate.BackColor = Color.FromArgb(16, 185, 129); // Vibrant emerald green
            _btnAutoTranslate.ForeColor = Color.White;
            _btnAutoTranslate.Text = ru ? "⚡ Авто: ВКЛ" : zh ? "⚡ 自动: 开启" : "⚡ Auto: ON";
        }
        else
        {
            _btnAutoTranslate.BackColor = Color.FromArgb(45, 49, 58);
            _btnAutoTranslate.ForeColor = Color.FromArgb(160, 165, 175);
            _btnAutoTranslate.Text = ru ? "⚡ Авто: ВЫКЛ" : zh ? "⚡ 自动: 关闭" : "⚡ Auto: OFF";
        }
    }

    private void PopulateTranslateLanguages()
    {
        if (_cbSourceLanguage == null || _cbTargetLanguage == null) return;

        int prevSrcIdx = _cbSourceLanguage.SelectedIndex;
        int prevTgtIdx = _cbTargetLanguage.SelectedIndex;

        _cbSourceLanguage.Items.Clear();
        _cbTargetLanguage.Items.Clear();

        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        // Source: Auto-detect + specific languages
        _cbSourceLanguage.Items.Add(ru ? "🌐 Автоопределение" : zh ? "🌐 自动检测" : "🌐 Auto-detect");
        _cbSourceLanguage.Items.Add("🇬🇧 English");
        _cbSourceLanguage.Items.Add("🇷🇺 Русский");
        _cbSourceLanguage.Items.Add(ru ? "🇨🇳 Китайский" : zh ? "🇨🇳 中文" : "🇨🇳 Chinese");
        _cbSourceLanguage.Items.Add(ru ? "🇩🇪 Немецкий" : zh ? "🇩🇪 德语" : "🇩🇪 German");
        _cbSourceLanguage.Items.Add(ru ? "🇫🇷 Французский" : zh ? "🇫🇷 法语" : "🇫🇷 French");
        _cbSourceLanguage.Items.Add(ru ? "🇯🇵 Японский" : zh ? "🇯🇵 日语" : "🇯🇵 Japanese");

        // Target: specific languages (default Russian)
        _cbTargetLanguage.Items.Add("🇷🇺 Русский");
        _cbTargetLanguage.Items.Add("🇬🇧 English");
        _cbTargetLanguage.Items.Add(ru ? "🇨🇳 Китайский" : zh ? "🇨🇳 中文" : "🇨🇳 Chinese");
        _cbTargetLanguage.Items.Add(ru ? "🇩🇪 Немецкий" : zh ? "🇩🇪 德语" : "🇩🇪 German");
        _cbTargetLanguage.Items.Add(ru ? "🇫🇷 Французский" : zh ? "🇫🇷 法语" : "🇫🇷 French");
        _cbTargetLanguage.Items.Add(ru ? "🇯🇵 Японский" : zh ? "🇯🇵 日语" : "🇯🇵 Japanese");

        _cbSourceLanguage.SelectedIndex = (prevSrcIdx >= 0 && prevSrcIdx < _cbSourceLanguage.Items.Count) ? prevSrcIdx : 0;
        _cbTargetLanguage.SelectedIndex = (prevTgtIdx >= 0 && prevTgtIdx < _cbTargetLanguage.Items.Count) ? prevTgtIdx : 0;
    }

    private string GetSelectedSourceLangCode()
    {
        return _cbSourceLanguage?.SelectedIndex switch
        {
            1 => "en",
            2 => "ru",
            3 => "zh-CN",
            4 => "de",
            5 => "fr",
            6 => "ja",
            _ => "auto"
        };
    }

    private string GetSelectedTargetLangCode()
    {
        return _cbTargetLanguage?.SelectedIndex switch
        {
            1 => "en",
            2 => "zh-CN",
            3 => "de",
            4 => "fr",
            5 => "ja",
            _ => "ru"
        };
    }

    private void SetSourceLangCode(string code)
    {
        if (_cbSourceLanguage == null) return;
        _cbSourceLanguage.SelectedIndex = code.ToLowerInvariant() switch
        {
            "en" => 1,
            "ru" => 2,
            "zh" or "zh-cn" => 3,
            "de" => 4,
            "fr" => 5,
            "ja" => 6,
            _ => 0
        };
    }

    private void SetTargetLangCode(string code)
    {
        if (_cbTargetLanguage == null) return;
        _cbTargetLanguage.SelectedIndex = code.ToLowerInvariant() switch
        {
            "en" => 1,
            "zh" or "zh-cn" => 2,
            "de" => 3,
            "fr" => 4,
            "ja" => 5,
            _ => 0
        };
    }

    private void SwapTranslationDirection()
    {
        string src = GetSelectedSourceLangCode();
        string tgt = GetSelectedTargetLangCode();

        if (src == "auto")
        {
            SetSourceLangCode(tgt);
            SetTargetLangCode(tgt == "ru" ? "en" : "ru");
        }
        else
        {
            SetSourceLangCode(tgt);
            SetTargetLangCode(src);
        }

        string outText = _txtSideOutput.Text.Trim();
        if (!string.IsNullOrWhiteSpace(outText))
        {
            _txtSideInput.Text = outText;
            _txtSideOutput.Text = "";
            _lblSideStatus.Text = "";
            _lastTranslatedText = "";
            _txtSideInput.Focus();
        }
    }

    private void ToggleSidePanel()
    {
        if (!_isSidePanelOpen)
        {
            _savedFormLeft = this.Left;
            var screen = Screen.FromControl(this).WorkingArea;
            int newLeft = this.Left - SidePanelWidth;
            if (newLeft < screen.Left)
                newLeft = screen.Left;

            this.SuspendLayout();
            _pnlSideTranslator.Visible = true;
            this.Left = newLeft;
            this.Width = BaseFormWidth + SidePanelWidth;
            _isSidePanelOpen = true;

            UpdateSidePanelTexts();
            _btnToggleSidePanel.Text = _currentLang switch { "zh" => "▶ 隐藏面板", "en" => "▶ Hide Manual", _ => "▶ Скрыть переводчик" };
            _btnToggleSidePanel.BackColor = Color.FromArgb(79, 70, 229);

            this.ResumeLayout(true);
            _txtSideInput.Focus();
        }
        else
        {
            CloseSidePanel();
        }
    }

    private void CloseSidePanel()
    {
        if (!_isSidePanelOpen) return;

        this.SuspendLayout();
        _isSidePanelOpen = false;

        // 1. Cancel in-flight translation & debounce timers
        try
        {
            _quickTransCancelSource?.Cancel();
            _quickTransCancelSource?.Dispose();
        }
        catch { }
        _quickTransCancelSource = null;

        _autoTranslateDebounceTimer?.Stop();
        _autoTranslateDebounceTimer?.Dispose();
        _autoTranslateDebounceTimer = null;

        // 2. Clear all text, cached buffers & reset auto-translate state as requested by user
        _txtSideInput.Text = "";
        _txtSideOutput.Text = "";
        _lblSideStatus.Text = "";
        _lastTranslatedText = "";
        _isAutoTranslateEnabled = false;
        UpdateAutoTranslateButtonStyle();

        // 3. Hide side panel and restore exact form size and screen position
        _pnlSideTranslator.Visible = false;
        this.Width = BaseFormWidth;

        var screen = Screen.FromControl(this).WorkingArea;
        int targetLeft = _savedFormLeft >= 0 ? _savedFormLeft : (this.Left + SidePanelWidth);
        if (targetLeft + BaseFormWidth > screen.Right)
            targetLeft = screen.Right - BaseFormWidth;
        if (targetLeft < screen.Left)
            targetLeft = screen.Left;
        this.Left = targetLeft;

        _btnToggleSidePanel.Text = _currentLang switch { "zh" => "◀ 手动翻译", "en" => "◀ Manual Translate", _ => "◀ Ручной перевод" };
        _btnToggleSidePanel.BackColor = Color.FromArgb(55, 60, 68);

        this.ResumeLayout(true);

        // 4. Clean up memory and working set
        try
        {
            GC.Collect(1, GCCollectionMode.Forced, false);
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

    private void UpdateSidePanelTexts()
    {
        if (_pnlSideTranslator == null) return;
        bool ru = _currentLang == "ru";
        bool zh = _currentLang == "zh";

        _btnToggleSidePanel.Text = _isSidePanelOpen ?
            (ru ? "▶ Скрыть переводчик" : zh ? "▶ 隐藏面板" : "▶ Hide Manual") :
            (ru ? "◀ Ручной перевод" : zh ? "◀ 手动翻译" : "◀ Manual Translate");

        _lblSideTitle.Text = ru ? "📝 Ручной перевод" : zh ? "📝 手动翻译" : "📝 Manual Translate";
        _btnSidePaste.Text = ru ? "📋 Вставить" : zh ? "📋 粘贴" : "📋 Paste";
        _btnSideClear.Text = ru ? "✕ Очистить" : zh ? "✕ 清空" : "✕ Clear";
        _btnSideTranslate.Text = ru ? "⚡ Перевести (Ctrl+Enter)" : zh ? "⚡ 翻译 (Ctrl+Enter)" : "⚡ Translate (Ctrl+Enter)";
        _lblTargetLang.Text = ru ? "На язык:" : zh ? "目标语言:" : "Target:";
        _btnSideCopy.Text = ru ? "📋 Скопировать" : zh ? "📋 复制" : "📋 Copy";
        _lblSideHint.Text = ru ? "💡 Термины Starsector, сленг и фракции" : zh ? "💡 支持远行星号游戏术语、俚语和派系设定" : "💡 Starsector lore terms, slang & factions";

        PopulateTranslateLanguages();
        UpdateAutoTranslateButtonStyle();
    }

    private async Task PerformQuickTranslationAsync()
    {
        string input = _txtSideInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            _txtSideOutput.Text = "";
            _lblSideStatus.Text = _currentLang == "zh" ? "⚠️ 请先输入要翻译的文本！" : _currentLang == "en" ? "⚠️ Please enter text to translate first!" : "⚠️ Сначала введите текст для перевода!";
            _lblSideStatus.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        try
        {
            _quickTransCancelSource?.Cancel();
            _quickTransCancelSource?.Dispose();
        }
        catch { }
        _quickTransCancelSource = new CancellationTokenSource();
        var ct = _quickTransCancelSource.Token;

        _btnSideTranslate.Enabled = false;
        _lblSideStatus.Text = _currentLang == "zh" ? "⏳ 正在翻译..." : _currentLang == "en" ? "⏳ Translating..." : "⏳ Перевод...";
        _lblSideStatus.ForeColor = Color.FromArgb(96, 165, 250);

        string src = GetSelectedSourceLangCode();
        string tgt = GetSelectedTargetLangCode();
        string direction = $"{src}->{tgt}";

        try
        {
            string result = "";
            bool usedAi = false;

            // 1. Try local AI if healthy
            if (_isServerHealthy)
            {
                try
                {
                    result = await TranslateViaLocalAiAsync(input, direction, ct);
                    usedAi = !string.IsNullOrWhiteSpace(result);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Program.Log($"QuickTranslate AI exception: {ex.Message}");
                }
            }

            // 2. Fallback to Google Translate if local AI was offline or returned empty
            if (string.IsNullOrWhiteSpace(result))
            {
                result = await TranslateViaGoogleAsync(input, direction, ct);
            }

            if (ct.IsCancellationRequested) return;

            if (!string.IsNullOrWhiteSpace(result))
            {
                _txtSideOutput.Text = result;
                _lastTranslatedText = input; // Mark this text as translated to avoid duplicate re-translations!

                string currentModel = Path.GetFileName(GetCurrentModelPath());
                bool isLight = currentModel.Equals(LightModelFilename, StringComparison.OrdinalIgnoreCase);
                string modelTag = usedAi ? (isLight ? "Index 2B" : "Qwen 7B") : "Google";
                _lblSideStatus.Text = $"✓ {_currentLang switch { "zh" => "已完成", "en" => "Done", _ => "Готово" }} ({modelTag})";
                _lblSideStatus.ForeColor = Color.FromArgb(34, 197, 94);
            }
            else
            {
                _lblSideStatus.Text = _currentLang == "zh" ? "⚠️ 翻译失败" : _currentLang == "en" ? "⚠️ Translation failed" : "⚠️ Ошибка перевода";
                _lblSideStatus.ForeColor = Color.FromArgb(239, 68, 68);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _lblSideStatus.Text = $"⚠️ {ex.Message}";
                _lblSideStatus.ForeColor = Color.FromArgb(239, 68, 68);
            }
        }
        finally
        {
            _btnSideTranslate.Enabled = true;
        }
    }

    private static async Task<string> TranslateViaLocalAiAsync(string text, string direction, CancellationToken ct)
    {
        string[] parts = direction.Split("->");
        string src = parts[0];
        string tgt = parts[1].ToLowerInvariant();
        bool toRu = tgt.StartsWith("ru");
        bool toZh = tgt.StartsWith("zh");

        string systemPrompt;
        if (toRu)
        {
            systemPrompt = "Translate the following message into natural, clear Russian chat speech.\nSTRICT RULES:\n1. Output ONLY the direct Russian translation. Never explain, never converse, never add quotes, notes, or <think> tags.\n2. Output strictly in Russian Cyrillic. Keep words separated by normal spaces.\n3. RARE & ARCHAIC TITLES: Translate English scholarly, administrative, and factional titles accurately (provost -> ректор/прево/декан, lector -> преподаватель/лектор, magister -> магистр, legate -> легат, archon -> архонт, preceptor -> наставник).\n4. MIXED CHAT & SLANG: If message mixes Russian slang with English words (e.g. 'Тип provost' -> 'Типа ректор' or 'Вроде ректора'), translate smoothly into natural Russian.\n5. Starsector terms: d-mod -> д-моды, s-mod -> с-моды, hullmod -> халлмоды, cruiser -> крейсер, flux -> флакс, vent -> сброс, core worlds -> основные миры, domain -> домен.\n6. NO LITERAL IDIOM TRANSLATIONS: Translate actual direct meaning.\n7. Translate simple words simply (Yes -> Да, No -> Нет, Yeah -> Да/Ага).\n\nText to translate:\n" + text;
        }
        else if (toZh)
        {
            systemPrompt = "请将以下文本翻译为流畅自然的简体中文。\n规则：\n1. 直接输出翻译结果，绝不要包含任何解释、说明、对话、引用或 <think> 思考过程。\n2. 准确传达原文核心意思，避免生硬直译外语俚语或比喻。\n3. 保持游戏用语和 Discord 聊天口语自然流畅。\n\n需要翻译的文本：\n" + text;
        }
        else
        {
            systemPrompt = "You are a bilingual translator.\nTranslate the following Russian text into natural, clear, conversational English.\nSTRICT RULES:\n1. SENSE OVER WORDS: Translate the communicative meaning and intent, never literal word-by-word syntax.\n2. CLEAR INTERNATIONAL ENGLISH: Use natural, straightforward conversational English that is easily understood by everyone, including people using machine translators. Avoid obscure idioms, complex metaphors, or cryptic hyper-slang.\n3. RUSSIAN IMPERSONAL & CONVERSATIONAL FORMS:\n   - 'не страшно / это не страшно' -> 'no worries / that\\'s fine / no problem' (NEVER 'scary')\n   - 'если что' -> 'by the way / FYI / just so you know' (NEVER 'if needed')\n   - 'пишет [где-то / кто-то]' -> 'it says / it shows [who/where]' (NEVER imperative 'write')\n   - 'может быть / это может быть' -> 'maybe / could be / that\\'s possible' (NEVER unfinished 'this may be')\n   - 'я благодарен / спасибо' -> 'thank you for / thanks for' (NEVER stiff 'I thank you')\n   - 'катка' -> 'game / match'\n4. Output ONLY the direct English translation. Never explain, never converse, never add quotes, notes, or <think> tags.\n5. Keep gaming and Starsector terms natural (d-mods, s-mods, hullmods, flux, vent, provost, lector).\n\nText to translate:\n" + text;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var payload = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = systemPrompt
                }
            },
            ["temperature"] = 0.1,
            ["frequency_penalty"] = 0.2,
            ["max_tokens"] = Math.Min(2048, Math.Max(512, text.Length * 4))
        };

        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("http://127.0.0.1:11434/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        string jsonStr = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(jsonStr);
        string? raw = doc?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string cleaned = raw.Trim();
        if (cleaned.Contains("</think>"))
            cleaned = cleaned.Substring(cleaned.LastIndexOf("</think>") + 8).Trim();
        else if (cleaned.Contains("<think>"))
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"<think>[\s\S]*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^(?:Translation|Russian|English|Chinese|Simplified Chinese|Result|Output|Перевод|Русский перевод|中文翻译|翻译结果)\s*[:：]\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        cleaned = cleaned.Trim('"', '\'', '`', '«', '»', '“', '”').Trim();

        return cleaned;
    }

    private static async Task<string> TranslateViaGoogleAsync(string text, string direction, CancellationToken ct)
    {
        string src = direction.Split("->")[0];
        string tgt = direction.Split("->")[1];
        if (tgt == "en-us" || tgt == "en-gb") tgt = "en";

        string url = "https://translate-pa.googleapis.com/v1/translate?params.client=gtx&dataTypes=TRANSLATION&key=AIzaSyDLEeFI5OtFBwYBIoK_jj5m32rZK5CkCXA&query.sourceLanguage="
            + Uri.EscapeDataString(src) + "&query.targetLanguage=" + Uri.EscapeDataString(tgt) + "&query.text=" + Uri.EscapeDataString(text);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        string jsonStr = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(jsonStr);
        string? translation = doc?["translation"]?.GetValue<string>();
        return translation ?? "";
    }

}
