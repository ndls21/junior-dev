using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using System.Xml;
using System.Linq;

#pragma warning disable CS8602 // Dereference of possibly null reference - suppressed for fields initialized in constructor

namespace Ui.Shell;

public enum SessionStatus
{
    Running,
    Paused,
    Error,
    NeedsApproval,
    Completed
}

public class SessionItem
{
    public string Name { get; set; } = "";
    public SessionStatus Status { get; set; }

    public override string ToString()
    {
        return Name; // Just return the name, status will be drawn separately
    }

    public (Color backColor, Color foreColor, string text) GetStatusChip()
    {
        return Status switch
        {
            SessionStatus.Running => (Color.Green, Color.White, "RUNNING"),
            SessionStatus.Paused => (Color.Yellow, Color.Black, "PAUSED"),
            SessionStatus.Error => (Color.Red, Color.White, "ERROR"),
            SessionStatus.NeedsApproval => (Color.Orange, Color.Black, "NEEDS APPROVAL"),
            SessionStatus.Completed => (Color.Blue, Color.White, "COMPLETED"),
            _ => (Color.Gray, Color.Black, "UNKNOWN")
        };
    }
}

public class AppSettings
{
    public string Theme { get; set; } = "Light";
    public int FontSize { get; set; } = 9;
    public bool ShowStatusChips { get; set; } = true;
    public bool AutoScrollEvents { get; set; } = true;
}

public class SettingsDialog : Form
{
    private System.Windows.Forms.ComboBox? themeCombo;
    private System.Windows.Forms.NumericUpDown? fontSizeSpinner;
    private System.Windows.Forms.CheckBox? statusChipsCheck;
    private System.Windows.Forms.CheckBox? autoScrollCheck;
    private System.Windows.Forms.Button? okButton;
    private System.Windows.Forms.Button? cancelButton;

    public AppSettings Settings { get; private set; } = new();

    public SettingsDialog()
    {
        InitializeDialog();
        LoadCurrentSettings();
    }

    private void InitializeDialog()
    {
        this.Text = "Settings";
        this.Size = new Size(300, 200);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        // Theme
        var themeLabel = new System.Windows.Forms.Label { Text = "Theme:", Location = new Point(10, 10), AutoSize = true };
        themeCombo = new System.Windows.Forms.ComboBox
        {
            Location = new Point(100, 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        themeCombo.Items.AddRange(new[] { "Light", "Dark", "Blue" });

        // Font Size
        var fontLabel = new System.Windows.Forms.Label { Text = "Font Size:", Location = new Point(10, 40), AutoSize = true };
        fontSizeSpinner = new System.Windows.Forms.NumericUpDown
        {
            Location = new Point(100, 40),
            Minimum = 8,
            Maximum = 16,
            Value = 9
        };

        // Checkboxes
        statusChipsCheck = new System.Windows.Forms.CheckBox { Text = "Show status chips", Location = new Point(10, 70), AutoSize = true };
        autoScrollCheck = new System.Windows.Forms.CheckBox { Text = "Auto-scroll events", Location = new Point(10, 95), AutoSize = true, Checked = true };

        // Buttons
        okButton = new System.Windows.Forms.Button { Text = "OK", Location = new Point(120, 130), DialogResult = DialogResult.OK };
        cancelButton = new System.Windows.Forms.Button { Text = "Cancel", Location = new Point(200, 130), DialogResult = DialogResult.Cancel };

        okButton.Click += OkButton_Click;

        this.Controls.AddRange(new System.Windows.Forms.Control[] {
            themeLabel, themeCombo, fontLabel, fontSizeSpinner,
            statusChipsCheck, autoScrollCheck, okButton, cancelButton
        });

        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;
    }

    private void LoadCurrentSettings()
    {
        // Load from saved settings - in a real app this would be passed in
        Settings = new AppSettings();
        themeCombo!.SelectedItem = Settings.Theme;
        fontSizeSpinner!.Value = Settings.FontSize;
        statusChipsCheck!.Checked = Settings.ShowStatusChips;
        autoScrollCheck!.Checked = Settings.AutoScrollEvents;
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        Settings = new AppSettings
        {
            Theme = themeCombo!.SelectedItem?.ToString() ?? "Light",
            FontSize = (int)fontSizeSpinner!.Value,
            ShowStatusChips = statusChipsCheck!.Checked,
            AutoScrollEvents = autoScrollCheck!.Checked
        };
    }
}

public partial class MainForm : Form
{
    private DockManager? dockManager;

    // Panels
    private DockPanel? sessionsPanel;
    private DockPanel? conversationPanel;
    private DockPanel? artifactsPanel;

    // Controls within panels
    private System.Windows.Forms.ListBox? sessionsListBox;
    private MemoEdit? conversationMemo;
    private TreeList? artifactsTree;

    internal bool isTestMode = false;

    // Public property for testing
    public bool IsTestMode
    {
        get => isTestMode;
        set => isTestMode = value;
    }
    private System.Windows.Forms.Timer? testTimer;
    private MenuStrip? mainMenu;
    private System.Windows.Forms.Timer? eventTimer;
    private AppSettings currentSettings = new();
    
    // Test helper fields
    private string? _testLayoutFilePath;
    private string? _testSettingsFilePath;

    public MainForm()
    {
        // Check for test mode argument
        string[] args = Environment.GetCommandLineArgs();
        isTestMode = args.Contains("--test") || args.Contains("-t");

        Console.WriteLine($"Junior Dev starting... Test mode: {isTestMode}");

        InitializeComponent();
        SetupMenu();
        SetupUI();
        LoadLayout();
        LoadAndApplySettings();

        if (isTestMode)
        {
            SetupTestMode();
        }
        else
        {
            SetupMockEventFeed();
            Console.WriteLine("UI initialized successfully. Close window to exit.");
        }
    }

    private void InitializeComponent()
    {
        this.Text = "Junior Dev - AI-Assisted Development Platform";
        this.Size = new Size(1200, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
        
        // Setup main menu
        mainMenu = new MenuStrip();
        this.MainMenuStrip = mainMenu;
        this.Controls.Add(mainMenu);
    }

    private void SetupMenu()
    {
        // View menu
        var viewMenu = new ToolStripMenuItem("View");
        
        // Reset Layout menu item
        var resetLayoutItem = new ToolStripMenuItem("Reset Layout");
        resetLayoutItem.Click += (s, e) => ResetLayout();
        resetLayoutItem.ShortcutKeys = Keys.Control | Keys.R;
        resetLayoutItem.ShowShortcutKeys = true;

        // Settings menu item
        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (s, e) => ShowSettingsDialog();

        viewMenu.DropDownItems.Add(resetLayoutItem);
        viewMenu.DropDownItems.Add(settingsItem);
        mainMenu.Items.Add(viewMenu);
    }

    private void SetupTestMode()
    {
        testTimer = new System.Windows.Forms.Timer();
        testTimer.Interval = 2000; // 2 seconds
        testTimer.Tick += (s, e) => 
        {
            testTimer.Stop();
            Application.Exit();
        };
        testTimer.Start();
        
        // Update title to indicate test mode
        this.Text = "Junior Dev - TEST MODE (Auto-exit in 2s)";
        
        // Log UI state for inspection
        Console.WriteLine("=== UI TEST MODE INSPECTION ===");
        Console.WriteLine($"Window Size: {this.Size.Width}x{this.Size.Height}");
        Console.WriteLine($"Sessions Panel: Visible={sessionsPanel!.Visible}, Width={sessionsPanel!.Width}");
        Console.WriteLine($"Conversation Panel: Visible={conversationPanel!.Visible}");
        Console.WriteLine($"Artifacts Panel: Visible={artifactsPanel!.Visible}, Width={artifactsPanel!.Width}");
        Console.WriteLine($"Sessions in list: {sessionsListBox!.Items.Count}");
        Console.WriteLine("Mock data loaded successfully");
        Console.WriteLine("Auto-exit in 2 seconds...");
        Console.WriteLine("===============================");
    }

    private void SetupUI()
    {
        // Initialize DevExpress DockManager
        dockManager = new DockManager();
        dockManager.Form = this;

        // Create panels
        CreateSessionsPanel();
        CreateConversationPanel();
        CreateArtifactsPanel();

        // Setup layout
        SetupLayout();
    }

    private void CreateSessionsPanel()
    {
        sessionsPanel = dockManager.AddPanel(DockingStyle.Left);
        sessionsPanel.Text = "Sessions";
        sessionsPanel.FloatSize = new Size(300, 600);
        sessionsPanel.FloatLocation = new Point(10, 10);

        // Create a panel with filters and session list
        var panel = new Panel();
        panel.Dock = DockStyle.Fill;

        // Add filter buttons at the top
        var filterPanel = new FlowLayoutPanel();
        filterPanel.Dock = DockStyle.Top;
        filterPanel.Height = 40;
        filterPanel.FlowDirection = FlowDirection.LeftToRight;

        var allButton = new SimpleButton();
        allButton.Text = "All";
        allButton.Width = 50;
        allButton.Click += (s, e) => FilterSessions("All");

        var runningButton = new SimpleButton();
        runningButton.Text = "Running";
        runningButton.Width = 70;
        runningButton.Click += (s, e) => FilterSessions("Running");

        var pausedButton = new SimpleButton();
        pausedButton.Text = "Paused";
        pausedButton.Width = 60;
        pausedButton.Click += (s, e) => FilterSessions("Paused");

        var errorButton = new SimpleButton();
        errorButton.Text = "Error";
        errorButton.Width = 50;
        errorButton.Click += (s, e) => FilterSessions("Error");

        filterPanel.Controls.AddRange(new Control[] { allButton, runningButton, pausedButton, errorButton });

        // Create session list with status indicators
        sessionsListBox = new System.Windows.Forms.ListBox();
        sessionsListBox.Dock = DockStyle.Fill;
        sessionsListBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
        sessionsListBox.ItemHeight = 30;
        sessionsListBox.DrawItem += SessionsListBox_DrawItem;

        // Mock session data with status objects
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 1", Status = SessionStatus.Running });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 2", Status = SessionStatus.Paused });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 3", Status = SessionStatus.Error });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 4", Status = SessionStatus.Running });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 5", Status = SessionStatus.NeedsApproval });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 6", Status = SessionStatus.Completed });

        panel.Controls.Add(sessionsListBox);
        panel.Controls.Add(filterPanel);

        sessionsPanel.Controls.Add(panel);
    }

    private void CreateConversationPanel()
    {
        conversationPanel = dockManager.AddPanel(DockingStyle.Fill);
        conversationPanel.Text = "Conversation & Events";

        conversationMemo = new MemoEdit();
        conversationMemo.Dock = DockStyle.Fill;
        conversationMemo.Text = "Event log and conversation will appear here...\r\n\r\n[Mock Event] Session started\r\n[Mock Event] Agent initialized\r\n[Mock Event] Command executed";

        conversationPanel.Controls.Add(conversationMemo);
    }

    private void CreateArtifactsPanel()
    {
        artifactsPanel = dockManager.AddPanel(DockingStyle.Right);
        artifactsPanel.Text = "Artifacts & Tests";
        artifactsPanel.FloatSize = new Size(300, 600);
        artifactsPanel.FloatLocation = new Point(900, 10);

        artifactsTree = new TreeList();
        artifactsTree.Dock = DockStyle.Fill;

        // Mock some artifact data
        artifactsTree.Columns.Add();
        artifactsTree.Columns[0].Caption = "Artifacts";
        artifactsTree.Columns[0].VisibleIndex = 0;

        var rootNode = artifactsTree.AppendNode(new object[] { "Build Results" }, null);
        artifactsTree.AppendNode(new object[] { "Test Results" }, rootNode);
        artifactsTree.AppendNode(new object[] { "Diff Output" }, rootNode);
        artifactsTree.AppendNode(new object[] { "Log Files" }, rootNode);

        artifactsPanel.Controls.Add(artifactsTree);
    }

    private void SetupLayout()
    {
        // Panels are already docked via DockingStyle in AddPanel
        // Configure panel sizes
        sessionsPanel!.Width = 250;
        artifactsPanel!.Width = 300;
    }

    private void LoadDefaultLayout()
    {
        // Default layout is already set up in SetupLayout()
        // Panels are docked via DockingStyle in AddPanel
    }

    private void SaveLayout()
    {
        try
        {
            string layoutFile = GetLayoutFilePath();
            string directory = Path.GetDirectoryName(layoutFile)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            dockManager!.SaveLayoutToXml(layoutFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save layout: {ex.Message}");
        }
    }

    private string GetLayoutFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "JuniorDev");
        return Path.Combine(appFolder, "layout.xml");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveLayout();
        base.OnFormClosing(e);
    }

    private void SetupMockEventFeed()
    {
        eventTimer = new System.Windows.Forms.Timer();
        eventTimer.Interval = 3000; // Add event every 3 seconds
        eventTimer.Tick += (s, e) => AddMockEvent();
        eventTimer.Start();
    }

    private void AddMockEvent()
    {
        if (conversationMemo is null) return;

        var mockEvents = new[]
        {
            "[INFO] Agent initialized successfully",
            "[COMMAND] Running git status",
            "[RESULT] Repository is clean",
            "[INFO] Starting code analysis",
            "[WARNING] Found 2 style violations",
            "[COMMAND] Executing tests",
            "[SUCCESS] All tests passed (15/15)",
            "[INFO] Build completed successfully",
            "[EVENT] Session state updated",
            "[INFO] Waiting for user input"
        };

        var randomEvent = mockEvents[new Random().Next(mockEvents.Length)];
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var eventLine = $"[{timestamp}] {randomEvent}\r\n";

        conversationMemo.Text += eventLine;

        // Auto-scroll to bottom only if enabled in settings
        if (currentSettings.AutoScrollEvents)
        {
            conversationMemo.SelectionStart = conversationMemo.Text.Length;
            conversationMemo.ScrollToCaret();
        }
    }

    private void ShowSettingsDialog()
    {
        using var dialog = new SettingsDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            // Apply settings
            ApplySettings(dialog.Settings);
            SaveSettings(dialog.Settings);
        }
    }

    private void LoadAndApplySettings()
    {
        var settings = LoadSettings();
        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        // Apply theme to form and child controls
        Color backColor, foreColor;
        switch (settings.Theme)
        {
            case "Light":
                backColor = Color.White;
                foreColor = Color.Black;
                break;
            case "Dark":
                backColor = Color.FromArgb(30, 30, 30);
                foreColor = Color.White;
                break;
            case "Blue":
                backColor = Color.LightBlue;
                foreColor = Color.Black;
                break;
            default:
                backColor = Color.White;
                foreColor = Color.Black;
                break;
        }

        this.BackColor = backColor;
        this.ForeColor = foreColor;

        // Apply to child controls
        if (sessionsPanel != null) sessionsPanel.Appearance.BackColor = backColor;
        if (conversationPanel != null) conversationPanel.Appearance.BackColor = backColor;
        if (artifactsPanel != null) artifactsPanel.Appearance.BackColor = backColor;

        // Apply font size to form and propagate to controls
        var newFont = new Font(this.Font.FontFamily, settings.FontSize);
        this.Font = newFont;

        // Store settings for behavior
        currentSettings = settings;

        // Apply ShowStatusChips behavior
        if (sessionsListBox != null)
        {
            sessionsListBox.DrawMode = settings.ShowStatusChips ? System.Windows.Forms.DrawMode.OwnerDrawFixed : System.Windows.Forms.DrawMode.Normal;
            sessionsListBox.Refresh(); // Redraw to apply changes
        }

        // Apply AutoScrollEvents behavior (stored for use in AddMockEvent)
        // This will be checked in AddMockEvent method
    }

    private void SaveSettings(AppSettings settings)
    {
        try
        {
            string settingsFile = GetSettingsFilePath();
            string directory = Path.GetDirectoryName(settingsFile)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            File.WriteAllText(settingsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private void SessionsListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= sessionsListBox!.Items.Count) return;

        var item = sessionsListBox!.Items[e.Index] as SessionItem;
        if (item == null) return;

        // Clear background
        e.DrawBackground();

        // Get status chip info
        var (chipBackColor, chipForeColor, chipText) = item.GetStatusChip();

        // Draw session name
        using (var brush = new SolidBrush(e.ForeColor))
        {
            e.Graphics.DrawString(item.Name, e.Font!, brush, e.Bounds.X + 80, e.Bounds.Y + 5);
        }

        // Draw status chip (badge)
        var chipRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 3, 70, 20);
        using (var chipBrush = new SolidBrush(chipBackColor))
        using (var chipTextBrush = new SolidBrush(chipForeColor))
        using (var chipFont = new Font(e.Font.FontFamily, 7f, FontStyle.Bold))
        {
            e.Graphics.FillRectangle(chipBrush, chipRect);
            e.Graphics.DrawRectangle(Pens.Black, chipRect);
            var textSize = e.Graphics.MeasureString(chipText, chipFont);
            var textX = chipRect.X + (chipRect.Width - textSize.Width) / 2;
            var textY = chipRect.Y + (chipRect.Height - textSize.Height) / 2;
            e.Graphics.DrawString(chipText, chipFont, chipTextBrush, textX, textY);
        }

        e.DrawFocusRectangle();
    }

    private void FilterSessions(string status)
    {
        sessionsListBox!.Items.Clear();

        // Mock session data - in real app this would come from session manager
        var allSessions = new[]
        {
            new SessionItem { Name = "Session 1", Status = SessionStatus.Running },
            new SessionItem { Name = "Session 2", Status = SessionStatus.Paused },
            new SessionItem { Name = "Session 3", Status = SessionStatus.Error },
            new SessionItem { Name = "Session 4", Status = SessionStatus.Running },
            new SessionItem { Name = "Session 5", Status = SessionStatus.NeedsApproval },
            new SessionItem { Name = "Session 6", Status = SessionStatus.Completed }
        };

        foreach (var session in allSessions)
        {
            bool shouldShow = status == "All" ||
                (status == "Running" && session.Status == SessionStatus.Running) ||
                (status == "Paused" && session.Status == SessionStatus.Paused) ||
                (status == "Error" && session.Status == SessionStatus.Error) ||
                (status == "NeedsApproval" && session.Status == SessionStatus.NeedsApproval) ||
                (status == "Completed" && session.Status == SessionStatus.Completed);

            if (shouldShow)
            {
                sessionsListBox!.Items.Add(session);
            }
        }
    }

    // Test helper methods
    public System.Windows.Forms.DrawMode GetSessionsListBoxDrawMode()
    {
        return sessionsListBox?.DrawMode ?? System.Windows.Forms.DrawMode.Normal;
    }

    public bool GetAutoScrollEventsSetting()
    {
        return currentSettings.AutoScrollEvents;
    }

    public void SetLayoutFilePath(string path)
    {
        // For testing only
        _testLayoutFilePath = path;
    }

    public void SetSettingsFilePath(string path)
    {
        // For testing only
        _testSettingsFilePath = path;
    }

    private string GetSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "JuniorDev");
        return Path.Combine(appFolder, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        try
        {
            string settingsFile = _testSettingsFilePath ?? GetSettingsFilePath();
            if (File.Exists(settingsFile))
            {
                var json = File.ReadAllText(settingsFile);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public void LoadLayout()
    {
        try
        {
            string layoutFile = _testLayoutFilePath ?? GetLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                dockManager!.RestoreLayoutFromXml(layoutFile);
            }
            else
            {
                LoadDefaultLayout();
            }
        }
        catch (Exception ex)
        {
            // If layout is corrupted, fall back to default
            Console.WriteLine($"Failed to load layout: {ex.Message}");
            LoadDefaultLayout();
        }
    }

    public void ResetLayout()
    {
        try
        {
            // Delete the layout file to reset to defaults
            string layoutFile = _testLayoutFilePath ?? GetLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                File.Delete(layoutFile);
            }
            
            // Explicitly reset panels to default layout
            sessionsPanel!.Dock = DockingStyle.Left;
            sessionsPanel!.Width = 250;
            artifactsPanel!.Dock = DockingStyle.Right;
            artifactsPanel!.Width = 300;
            conversationPanel!.Dock = DockingStyle.Fill;
            
            // Save the reset layout immediately
            SaveLayout();
            
            // Show message box only in normal mode (skip in test mode for automation)
            if (!isTestMode)
            {
                MessageBox.Show("Layout has been reset to default.", "Layout Reset", 
                              MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (!isTestMode)
            {
                MessageBox.Show($"Failed to reset layout: {ex.Message}", "Error", 
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                Console.WriteLine($"Failed to reset layout: {ex.Message}");
            }
        }
    }

    private void ShowAutoClosingMessageBox(string message, string title, int seconds)
    {
        var form = new Form
        {
            Text = title,
            Size = new Size(300, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true,
            Owner = this // Set owner for proper parenting
        };

        var label = new System.Windows.Forms.Label
        {
            Text = message,
            AutoSize = true,
            Location = new Point(20, 20)
        };

        form.Controls.Add(label);

        var timer = new System.Windows.Forms.Timer
        {
            Interval = seconds * 1000
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (!form.IsDisposed)
            {
                form.Invoke(new Action(() => form.Close()));
            }
        };

        form.FormClosed += (s, e) => timer.Stop();

        timer.Start();
        form.Show();
    }
}