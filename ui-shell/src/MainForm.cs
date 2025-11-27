using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;

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

    public MainForm()
    {
        InitializeComponent();
        SetupUI();
        LoadDefaultLayout();
    }

    private void InitializeComponent()
    {
        this.Text = "Junior Dev - AI-Assisted Development Platform";
        this.Size = new Size(1200, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
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

    private void LoadDefaultLayout()
    {
        // For now, just ensure the layout is set up
        // TODO: Implement layout persistence (save/restore from user settings)
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // TODO: Save layout before closing
        base.OnFormClosing(e);
    }
}