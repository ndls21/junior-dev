using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using System.Xml;

namespace Ui.Shell;

public partial class MainForm : Form
{
    private DockManager dockManager;

    // Panels
    private DockPanel sessionsPanel;
    private DockPanel conversationPanel;
    private DockPanel artifactsPanel;

    // Controls within panels
    private ListBoxControl sessionsListBox;
    private MemoEdit conversationMemo;
    private TreeList artifactsTree;

    private bool isTestMode = false;
    private System.Windows.Forms.Timer testTimer;
    private MenuStrip mainMenu;

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

        if (isTestMode)
        {
            SetupTestMode();
        }
        else
        {
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
        
        viewMenu.DropDownItems.Add(resetLayoutItem);
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
        Console.WriteLine($"Sessions Panel: Visible={sessionsPanel.Visible}, Width={sessionsPanel.Width}");
        Console.WriteLine($"Conversation Panel: Visible={conversationPanel.Visible}");
        Console.WriteLine($"Artifacts Panel: Visible={artifactsPanel.Visible}, Width={artifactsPanel.Width}");
        Console.WriteLine($"Sessions in list: {sessionsListBox.Items.Count}");
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

        // Create session list
        sessionsListBox = new ListBoxControl();
        sessionsListBox.Dock = DockStyle.Fill;

        // Mock session data with status
        sessionsListBox.Items.Add("🔄 Session 1 - Running");
        sessionsListBox.Items.Add("⏸️ Session 2 - Paused");
        sessionsListBox.Items.Add("❌ Session 3 - Error");
        sessionsListBox.Items.Add("🔄 Session 4 - Running");
        sessionsListBox.Items.Add("⚠️ Session 5 - NeedsApproval");
        sessionsListBox.Items.Add("✅ Session 6 - Completed");

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
        sessionsPanel.Width = 250;
        artifactsPanel.Width = 300;
    }

    private void LoadLayout()
    {
        try
        {
            string layoutFile = GetLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                dockManager.RestoreLayoutFromXml(layoutFile);
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
            string directory = Path.GetDirectoryName(layoutFile);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            dockManager.SaveLayoutToXml(layoutFile);
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

    private void ResetLayout()
    {
        try
        {
            // Delete the layout file to reset to defaults
            string layoutFile = GetLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                File.Delete(layoutFile);
            }
            
            // Reset panels to default layout
            LoadDefaultLayout();
            
            MessageBox.Show("Layout has been reset to default.", "Layout Reset", 
                          MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reset layout: {ex.Message}", "Error", 
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveLayout();
        base.OnFormClosing(e);
    }

    private void FilterSessions(string status)
    {
        sessionsListBox.Items.Clear();

        // Mock session data - in real app this would come from session manager
        var allSessions = new[]
        {
            "🔄 Session 1 - Running",
            "⏸️ Session 2 - Paused",
            "❌ Session 3 - Error",
            "🔄 Session 4 - Running",
            "⚠️ Session 5 - NeedsApproval",
            "✅ Session 6 - Completed"
        };

        foreach (var session in allSessions)
        {
            if (status == "All" ||
                (status == "Running" && session.Contains("Running")) ||
                (status == "Paused" && session.Contains("Paused")) ||
                (status == "Error" && session.Contains("Error")))
            {
                sessionsListBox.Items.Add(session);
            }
        }
    }
}