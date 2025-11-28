using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.AIIntegration.WinForms.Chat;
using Microsoft.Extensions.AI;
using DevExpress.AIIntegration;
using System.Xml;
using System.Linq;
using JuniorDev.Contracts;

#pragma warning disable CS8602 // Dereference of possibly null reference - suppressed for fields initialized in constructor

namespace Ui.Shell;

public enum BlockingType
{
    NeedsApproval,
    ConflictDetected,
    Throttled
}

public class BlockingCondition
{
    public Guid SessionId { get; set; }
    public BlockingType Type { get; set; }
    public string Message { get; set; } = "";
    public string ActionText { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}

public class SessionItem
{
    public string Name { get; set; } = "";
    public JuniorDev.Contracts.SessionStatus Status { get; set; }

    public override string ToString()
    {
        return Name; // Just return the name, status will be drawn separately
    }

    public (Color backColor, Color foreColor, string text) GetStatusChip()
    {
        return Status switch
        {
            JuniorDev.Contracts.SessionStatus.Running => (Color.Green, Color.White, "RUNNING"),
            JuniorDev.Contracts.SessionStatus.Paused => (Color.Yellow, Color.Black, "PAUSED"),
            JuniorDev.Contracts.SessionStatus.Error => (Color.Red, Color.White, "ERROR"),
            JuniorDev.Contracts.SessionStatus.NeedsApproval => (Color.Orange, Color.Black, "NEEDS APPROVAL"),
            JuniorDev.Contracts.SessionStatus.Completed => (Color.Blue, Color.White, "COMPLETED"),
            _ => (Color.Gray, Color.Black, "UNKNOWN")
        };
    }
}

// Event rendering classes
public class EventRenderer
{
    private readonly MemoEdit _memoEdit;

    public EventRenderer(MemoEdit memoEdit)
    {
        _memoEdit = memoEdit;
    }

    public void RenderEvent(IEvent @event, DateTimeOffset timestamp)
    {
        var message = FormatEventAsMessage(@event, timestamp);
        _memoEdit.Text += message + Environment.NewLine;
        
        // Auto-scroll to bottom if enabled
        if (_memoEdit.Parent?.Parent is MainForm mainForm && mainForm.GetAutoScrollEventsSetting())
        {
            _memoEdit.SelectionStart = _memoEdit.Text.Length;
            _memoEdit.ScrollToCaret();
        }
    }

    private string FormatEventAsMessage(IEvent @event, DateTimeOffset timestamp)
    {
        var timeStr = timestamp.ToString("HH:mm:ss.fff");
        var sessionId = @event.Correlation.SessionId.ToString("N")[..8]; // First 8 chars
        var commandId = @event.Correlation.CommandId?.ToString("N")[..8] ?? "--------";

        var baseText = $"[{timeStr}] [{sessionId}] [{commandId}] ";

        switch (@event)
        {
            case CommandAccepted accepted:
                return $"{baseText}✅ Command {accepted.CommandId.ToString("N")[..8]} ACCEPTED";
            
            case CommandCompleted completed:
            {
                var outcome = completed.Outcome == CommandOutcome.Success ? "SUCCESS" : "FAILED";
                var message = string.IsNullOrEmpty(completed.Message) ? "" : $" - {completed.Message}";
                return $"{baseText}🏁 Command {completed.CommandId.ToString("N")[..8]} {outcome}{message}";
            }
            
            case CommandRejected rejected:
                return $"{baseText}❌ Command {rejected.CommandId.ToString("N")[..8]} REJECTED: {rejected.Reason}";
            
            case Throttled throttled:
                return $"{baseText}⏱️ THROTTLED ({throttled.Scope}) - Retry after {throttled.RetryAfter}";
            
            case ConflictDetected conflict:
                return $"{baseText}⚠️ CONFLICT: {conflict.Details}";
            
            case ArtifactAvailable artifact:
            {
                var summary = artifact.Artifact.InlineText?.Length > 100 
                    ? artifact.Artifact.InlineText[..100] + "..." 
                    : artifact.Artifact.InlineText ?? "[Binary artifact]";
                return $"{baseText}📎 ARTIFACT: {artifact.Artifact.Name} ({artifact.Artifact.Kind}) - {summary}";
            }
            
            case SessionStatusChanged status:
                return $"{baseText}🔄 Session {status.Status}{(string.IsNullOrEmpty(status.Reason) ? "" : $" - {status.Reason}")}";
            
            default:
                return $"{baseText}📝 {@event.Kind}: {System.Text.Json.JsonSerializer.Serialize(@event)}";
        }
    }
}

public class ChatStreamData
{
    public Guid SessionId { get; set; }
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; } = "Agent";
    public JuniorDev.Contracts.SessionStatus Status { get; set; } = JuniorDev.Contracts.SessionStatus.Running;
    public string CurrentTask { get; set; } = "";
    public int ProgressPercentage { get; set; } = 0;
    public bool IsExpanded { get; set; } = false;
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
    private DockPanel? eventsPanel;
    private DockPanel? artifactsPanel;

    // Blocking banners
    private Panel? _blockingBannerPanel;
    private System.Windows.Forms.Label? _blockingBannerLabel;
    private SimpleButton? _blockingBannerActionButton;

    // Controls within panels
    private System.Windows.Forms.ListBox? sessionsListBox;
    private MemoEdit? eventsMemoEdit;
    private TreeList? artifactsTree;

    // Chat components
    private AccordionLayoutManager? _accordionManager;
    private Panel? _chatContainerPanel;
    private System.Windows.Forms.ComboBox? _chatFilterCombo;
    private Dictionary<Guid, List<Artifact>> _chatArtifacts = new();

    // Blocking state tracking
    private List<BlockingCondition> _blockingConditions = new();

    private bool isTestMode = false;

    // Public property for testing
    public bool IsTestMode
    {
        get => isTestMode;
        set => isTestMode = value;
    }

    private EventRenderer? eventRenderer;
    private System.Windows.Forms.Timer? testTimer;
    private MenuStrip? mainMenu;
    private System.Windows.Forms.Timer? eventTimer;
    private AppSettings currentSettings = new();
    
    // Test helper fields
    private string? _testLayoutFilePath;
    private string? _testSettingsFilePath;
    private string? _testChatStreamsFilePath;

    public MainForm(bool isTestMode = false)
    {
        // Check for test mode argument or parameter
        this.isTestMode = isTestMode || Environment.GetCommandLineArgs().Contains("--test") || Environment.GetCommandLineArgs().Contains("-t");

        Console.WriteLine($"Junior Dev starting... Test mode: {this.isTestMode}");

        // Register AI client for chat functionality
        RegisterAIClient();

        InitializeComponent();
        SetupMenu();
        SetupUI();
        eventRenderer = new EventRenderer(eventsMemoEdit!);
        
        // Only load layout if not in test mode, or let tests control it
        if (!this.isTestMode)
        {
            LoadLayout();
            LoadAndApplySettings();
        }

        if (this.isTestMode)
        {
            // Add mock blocking events before test inspection
            AddMockBlockingEvents();
            SetupTestMode();
        }
        else
        {
            SetupMockEventFeed();
            Console.WriteLine("UI initialized successfully. Close window to exit.");
        }
    }

    private void RegisterAIClient()
    {
        try
        {
            // Register OpenAI client for AI chat functionality
            // In production, you would get the API key from environment variables or secure storage
            var openAIKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "your-openai-api-key-here";
            
            if (!string.IsNullOrEmpty(openAIKey) && openAIKey != "your-openai-api-key-here")
            {
                var openAIClient = new OpenAI.OpenAIClient(openAIKey).GetChatClient("gpt-4o-mini").AsIChatClient();
                AIExtensionsContainerDesktop.Default.RegisterChatClient(openAIClient);
                Console.WriteLine("AI client registered successfully.");
            }
            else
            {
                Console.WriteLine("Warning: OpenAI API key not configured. AI chat features will be limited.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to register AI client: {ex.Message}");
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

        // Chat menu
        var chatMenu = new ToolStripMenuItem("Chat");
        
        var addChatItem = new ToolStripMenuItem("Add Chat Stream");
        addChatItem.Click += (s, e) => AddNewChatStream();
        addChatItem.ShortcutKeys = Keys.Control | Keys.N;
        addChatItem.ShowShortcutKeys = true;

        var removeChatItem = new ToolStripMenuItem("Remove Active Chat");
        removeChatItem.Click += (s, e) => RemoveActiveChatStream();
        removeChatItem.ShortcutKeys = Keys.Control | Keys.W;
        removeChatItem.ShowShortcutKeys = true;

        chatMenu.DropDownItems.Add(addChatItem);
        chatMenu.DropDownItems.Add(removeChatItem);
        mainMenu.Items.Add(chatMenu);
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
        Console.WriteLine($"Chat Streams: {_accordionManager?.ChatStreams.Count ?? 0}");
        if (_accordionManager != null)
        {
            foreach (var stream in _accordionManager.ChatStreams)
            {
                var preview = stream.GetCollapsedPreview();
                Console.WriteLine($"  - {stream.AgentName}: {stream.Status}, Expanded={stream.IsExpanded}");
                Console.WriteLine($"    Preview: {preview}");
            }
        }
        Console.WriteLine($"Events Panel: Visible={eventsPanel!.Visible}, Height={eventsPanel!.Height}");
        Console.WriteLine($"Artifacts Panel: Visible={artifactsPanel!.Visible}, Width={artifactsPanel!.Width}");
        Console.WriteLine($"Chat Filter: {_chatFilterCombo?.SelectedItem?.ToString() ?? "None"}");
        Console.WriteLine($"Total Artifacts: {_chatArtifacts.Sum(kvp => kvp.Value.Count)}");
        foreach (var kvp in _chatArtifacts)
        {
            var chatName = _accordionManager?.ChatStreams.FirstOrDefault(s => s.SessionId == kvp.Key)?.AgentName ?? "Unknown";
            Console.WriteLine($"  - {chatName}: {kvp.Value.Count} artifacts");
        }
        Console.WriteLine($"Blocking Banners: {_blockingConditions.Count} active");
        foreach (var condition in _blockingConditions)
        {
            Console.WriteLine($"  - {condition.Type}: {condition.Message}");
        }
        Console.WriteLine($"Sessions in list: {sessionsListBox!.Items.Count}");
        Console.WriteLine("Mock data loaded successfully");
        Console.WriteLine("Auto-exit in 2 seconds...");
        Console.WriteLine("===============================");
    }

    private void AddMockBlockingEvents()
    {
        // Add some mock blocking conditions for demonstration
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        Console.WriteLine("Adding mock blocking conditions...");
        
        AddBlockingCondition(new BlockingCondition
        {
            SessionId = sessionId1,
            Type = BlockingType.Throttled,
            Message = $"⚠️ Session throttled: git-operations. Retry after 14:37:05",
            ActionText = "Wait"
        });
        
        Console.WriteLine($"After adding throttled: {_blockingConditions.Count} conditions");

        AddBlockingCondition(new BlockingCondition
        {
            SessionId = sessionId2,
            Type = BlockingType.ConflictDetected,
            Message = $"⚠️ Merge conflict detected: Merge conflict in main.cs",
            ActionText = "Resolve"
        });
        
        Console.WriteLine($"After adding conflict: {_blockingConditions.Count} conditions");
        Console.WriteLine("Mock blocking events added for demonstration");
    }

    private void SetupUI()
    {
        // Initialize DevExpress DockManager
        dockManager = new DockManager();
        dockManager.Form = this;

        // Create blocking banner (initially hidden)
        CreateBlockingBanner();

        // Create panels
        CreateSessionsPanel();
        CreateConversationPanel();
        CreateEventsPanel();
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
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 1", Status = JuniorDev.Contracts.SessionStatus.Running });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 2", Status = JuniorDev.Contracts.SessionStatus.Paused });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 3", Status = JuniorDev.Contracts.SessionStatus.Error });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 4", Status = JuniorDev.Contracts.SessionStatus.Running });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 5", Status = JuniorDev.Contracts.SessionStatus.NeedsApproval });
        sessionsListBox.Items.Add(new SessionItem { Name = "Session 6", Status = JuniorDev.Contracts.SessionStatus.Completed });

        panel.Controls.Add(sessionsListBox);
        panel.Controls.Add(filterPanel);

        sessionsPanel.Controls.Add(panel);
    }

    private void CreateConversationPanel()
    {
        conversationPanel = dockManager.AddPanel(DockingStyle.Fill);
        conversationPanel.Text = "AI Chat";

        // Create container panel for accordion layout
        _chatContainerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        // Initialize accordion layout manager
        _accordionManager = new AccordionLayoutManager(_chatContainerPanel);

        // Wire up events
        _accordionManager.StreamsChanged += (s, e) => UpdateChatFilterOptions();

        // Create initial chat stream
        var initialChatStream = new ChatStream(Guid.NewGuid(), "Agent 1");
        _accordionManager.AddChatStream(initialChatStream);
        
        // Add a few more streams for testing rich previews
        if (isTestMode)
        {
            AddTestChatStreams();
        }

        conversationPanel.Controls.Add(_chatContainerPanel);
    }

    private void CreateEventsPanel()
    {
        eventsPanel = dockManager.AddPanel(DockingStyle.Bottom);
        eventsPanel.Text = "Event Stream";

        eventsMemoEdit = new MemoEdit();
        eventsMemoEdit.Dock = DockStyle.Fill;
        eventsMemoEdit.Properties.ReadOnly = true;
        eventsMemoEdit.Properties.ScrollBars = ScrollBars.Vertical;

        eventsPanel.Controls.Add(eventsMemoEdit);
    }

    private void CreateArtifactsPanel()
    {
        artifactsPanel = dockManager.AddPanel(DockingStyle.Right);
        artifactsPanel.Text = "Artifacts & Tests";
        artifactsPanel.FloatSize = new Size(300, 600);
        artifactsPanel.FloatLocation = new Point(900, 10);

        // Create container panel for filter + tree
        var containerPanel = new Panel();
        containerPanel.Dock = DockStyle.Fill;

        // Add filter dropdown at the top
        var filterPanel = new FlowLayoutPanel();
        filterPanel.Dock = DockStyle.Top;
        filterPanel.Height = 35;
        filterPanel.FlowDirection = FlowDirection.LeftToRight;

        var filterLabel = new System.Windows.Forms.Label();
        filterLabel.Text = "Filter by Chat:";
        filterLabel.AutoSize = true;
        filterLabel.TextAlign = ContentAlignment.MiddleLeft;
        filterLabel.Padding = new Padding(5, 8, 0, 0);

        var chatFilterCombo = new System.Windows.Forms.ComboBox();
        chatFilterCombo.Width = 180;
        chatFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        chatFilterCombo.Items.Add("All Chats");
        chatFilterCombo.SelectedIndex = 0; // Default to "All Chats"
        chatFilterCombo.SelectedIndexChanged += (s, e) => FilterArtifactsByChat(chatFilterCombo.SelectedItem?.ToString() ?? "All Chats");

        filterPanel.Controls.Add(filterLabel);
        filterPanel.Controls.Add(chatFilterCombo);

        // Create artifacts tree
        artifactsTree = new TreeList();
        artifactsTree.Dock = DockStyle.Fill;

        // Setup columns
        artifactsTree.Columns.Add();
        artifactsTree.Columns[0].Caption = "Artifacts";
        artifactsTree.Columns[0].VisibleIndex = 0;

        // Add double-click handler for artifact linking
        artifactsTree.DoubleClick += ArtifactsTree_DoubleClick;

        // Store filter combo for later updates
        _chatFilterCombo = chatFilterCombo;

        containerPanel.Controls.Add(artifactsTree);
        containerPanel.Controls.Add(filterPanel);

        artifactsPanel.Controls.Add(containerPanel);

        // Initialize with mock data
        RefreshArtifactsTree();
    }

    private void CreateBlockingBanner()
    {
        _blockingBannerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(255, 255, 200), // Light yellow
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };

        _blockingBannerLabel = new System.Windows.Forms.Label
        {
            Location = new Point(10, 10),
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold),
            ForeColor = Color.DarkRed
        };

        _blockingBannerActionButton = new SimpleButton
        {
            Location = new Point(300, 8),
            Size = new Size(100, 30),
            Text = "Action",
            Visible = false
        };
        _blockingBannerActionButton.Click += BlockingBannerActionButton_Click;

        _blockingBannerPanel.Controls.Add(_blockingBannerLabel);
        _blockingBannerPanel.Controls.Add(_blockingBannerActionButton);

        this.Controls.Add(_blockingBannerPanel);
        // Ensure banner appears above menu but below panels
        _blockingBannerPanel.BringToFront();
    }

    private void SetupLayout()
    {
        // Panels are already docked via DockingStyle in AddPanel
        // Configure panel sizes
        sessionsPanel!.Width = 250;
        eventsPanel!.Height = 200;
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
            
            // Save DevExpress dock layout
            dockManager!.SaveLayoutToXml(layoutFile);
            
            // Save chat streams data
            SaveChatStreams();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save layout: {ex.Message}");
        }
    }

    public void SaveChatStreams(ChatStream[]? streamsToSave = null)
    {
        try
        {
            var streams = streamsToSave ?? (_accordionManager?.ChatStreams ?? Array.Empty<ChatStream>());
            
            var chatStreamsData = streams.Select(stream => new ChatStreamData
            {
                SessionId = stream.SessionId,
                AgentId = stream.AgentId,
                AgentName = stream.AgentName,
                Status = stream.Status,
                CurrentTask = stream.CurrentTask,
                ProgressPercentage = stream.ProgressPercentage,
                IsExpanded = stream.IsExpanded
            }).ToList();
            
            string chatStreamsFile = GetChatStreamsFilePath();
            string json = System.Text.Json.JsonSerializer.Serialize(chatStreamsData);
            File.WriteAllText(chatStreamsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save chat streams: {ex.Message}");
        }
    }

    public ChatStream[] LoadChatStreams()
    {
        try
        {
            string chatStreamsFile = GetChatStreamsFilePath();
            if (!File.Exists(chatStreamsFile)) return LoadDefaultChatStreams();
            
            string json = File.ReadAllText(chatStreamsFile);
            var chatStreamsData = System.Text.Json.JsonSerializer.Deserialize<List<ChatStreamData>>(json);
            
            if (chatStreamsData != null && _accordionManager != null)
            {
                // Clear existing streams
                var streamsToRemove = _accordionManager.ChatStreams.ToList();
                foreach (var stream in streamsToRemove)
                {
                    _accordionManager.RemoveChatStream(stream);
                }
                
                // Load saved streams
                foreach (var data in chatStreamsData)
                {
                    var chatStream = new ChatStream(data.SessionId, data.AgentName)
                    {
                        AgentId = data.AgentId,
                        Status = data.Status,
                        CurrentTask = data.CurrentTask,
                        ProgressPercentage = data.ProgressPercentage,
                        IsExpanded = data.IsExpanded
                    };
                    
                    _accordionManager.AddChatStream(chatStream);
                }
                
                return _accordionManager.ChatStreams.ToArray();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load chat streams: {ex.Message}");
            // Fall back to default chat streams
            return LoadDefaultChatStreams();
        }
        
        return Array.Empty<ChatStream>();
    }

    public ChatStream[] LoadDefaultChatStreams()
    {
        if (_accordionManager == null) return Array.Empty<ChatStream>();
        
        // Clear existing streams
        var streamsToRemove = _accordionManager.ChatStreams.ToList();
        foreach (var stream in streamsToRemove)
        {
            _accordionManager.RemoveChatStream(stream);
        }
        
        // Add default stream
        var defaultStream = new ChatStream(Guid.NewGuid(), "Agent 1");
        _accordionManager.AddChatStream(defaultStream);
        
        return _accordionManager.ChatStreams.ToArray();
    }

    private string GetChatStreamsFilePath()
    {
        if (_testChatStreamsFilePath != null)
        {
            return _testChatStreamsFilePath;
        }
        
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "JuniorDev");
        return Path.Combine(appFolder, "chat-streams.json");
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
        // Generate mock events with proper correlation
        var mockEvents = GenerateMockEventSequence();
        var randomEvent = mockEvents[new Random().Next(mockEvents.Length)];
        
        // Route event to appropriate chat streams
        _accordionManager?.RouteEventToStreams(randomEvent, DateTimeOffset.Now);
        
        // Handle artifacts
        if (randomEvent is ArtifactAvailable artifactEvent)
        {
            AddArtifactToChat(artifactEvent.Correlation.SessionId, artifactEvent.Artifact);
        }

        // Check for blocking conditions
        CheckForBlockingConditions(randomEvent);
        
        // Also render to global events panel for backward compatibility
        eventRenderer?.RenderEvent(randomEvent, DateTimeOffset.Now);
    }

    private void CheckForBlockingConditions(IEvent @event)
    {
        switch (@event)
        {
            case Throttled throttled:
                AddBlockingCondition(new BlockingCondition
                {
                    SessionId = throttled.Correlation.SessionId,
                    Type = BlockingType.Throttled,
                    Message = $"⚠️ Session throttled: {throttled.Scope}. Retry after {throttled.RetryAfter:HH:mm:ss}",
                    ActionText = "Wait"
                });
                break;

            case ConflictDetected conflict:
                AddBlockingCondition(new BlockingCondition
                {
                    SessionId = conflict.Correlation.SessionId,
                    Type = BlockingType.ConflictDetected,
                    Message = $"⚠️ Merge conflict detected: {conflict.Details}",
                    ActionText = "Resolve"
                });
                break;

            case SessionStatusChanged statusChanged when statusChanged.Status == JuniorDev.Contracts.SessionStatus.NeedsApproval:
                AddBlockingCondition(new BlockingCondition
                {
                    SessionId = statusChanged.Correlation.SessionId,
                    Type = BlockingType.NeedsApproval,
                    Message = $"🔒 Session requires approval: {statusChanged.Reason}",
                    ActionText = "Approve"
                });
                break;

            case SessionStatusChanged statusChanged when statusChanged.Status != JuniorDev.Contracts.SessionStatus.NeedsApproval:
                // Remove approval blocking when status changes away from NeedsApproval
                RemoveBlockingCondition(statusChanged.Correlation.SessionId, BlockingType.NeedsApproval);
                break;
        }
    }

    public IEvent[] GenerateMockEventSequence()
    {
        // Use existing chat stream SessionIds for events
        var availableSessionIds = _accordionManager?.ChatStreams.Select(s => s.SessionId).ToArray() ?? new[] { Guid.NewGuid() };
        var sessionId = availableSessionIds[new Random().Next(availableSessionIds.Length)];
        var commandId = Guid.NewGuid();
        var correlation = new Correlation(sessionId, commandId);

        return new IEvent[]
        {
            new CommandAccepted(Guid.NewGuid(), correlation, commandId),
            new CommandCompleted(Guid.NewGuid(), correlation, commandId, CommandOutcome.Success, "Tests passed (15/15)"),
            new ArtifactAvailable(Guid.NewGuid(), correlation, new Artifact("test-results", "Test Results", "All 15 tests passed successfully")),
            new SessionStatusChanged(Guid.NewGuid(), correlation, JuniorDev.Contracts.SessionStatus.Running, "Processing next command"),
            new CommandAccepted(Guid.NewGuid(), new Correlation(sessionId, Guid.NewGuid()), Guid.NewGuid()),
            new Throttled(Guid.NewGuid(), correlation, "git-operations", DateTimeOffset.Now.AddSeconds(30)),
            new CommandRejected(Guid.NewGuid(), correlation, commandId, "Rate limit exceeded", "throttle-policy"),
            new ConflictDetected(Guid.NewGuid(), correlation, new RepoRef("test-repo", "/path/to/repo"), "Merge conflict in main.cs"),
            new CommandCompleted(Guid.NewGuid(), correlation, commandId, CommandOutcome.Failure, "Build failed", "COMPILATION_ERROR")
        };
    }

    public void RouteEventToChatStreams(IEvent @event, DateTimeOffset timestamp)
    {
        _accordionManager?.RouteEventToStreams(@event, timestamp);
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
        if (eventsPanel != null) eventsPanel.Appearance.BackColor = backColor;
        if (artifactsPanel != null) artifactsPanel.Appearance.BackColor = backColor;
        if (eventsMemoEdit != null) eventsMemoEdit.BackColor = backColor;

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
            new SessionItem { Name = "Session 1", Status = JuniorDev.Contracts.SessionStatus.Running },
            new SessionItem { Name = "Session 2", Status = JuniorDev.Contracts.SessionStatus.Paused },
            new SessionItem { Name = "Session 3", Status = JuniorDev.Contracts.SessionStatus.Error },
            new SessionItem { Name = "Session 4", Status = JuniorDev.Contracts.SessionStatus.Running },
            new SessionItem { Name = "Session 5", Status = JuniorDev.Contracts.SessionStatus.NeedsApproval },
            new SessionItem { Name = "Session 6", Status = JuniorDev.Contracts.SessionStatus.Completed }
        };

        foreach (var session in allSessions)
        {
            bool shouldShow = status == "All" ||
                (status == "Running" && session.Status == JuniorDev.Contracts.SessionStatus.Running) ||
                (status == "Paused" && session.Status == JuniorDev.Contracts.SessionStatus.Paused) ||
                (status == "Error" && session.Status == JuniorDev.Contracts.SessionStatus.Error) ||
                (status == "NeedsApproval" && session.Status == JuniorDev.Contracts.SessionStatus.NeedsApproval) ||
                (status == "Completed" && session.Status == JuniorDev.Contracts.SessionStatus.Completed);

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

    public void SetChatStreamsFilePath(string path)
    {
        // For testing only
        _testChatStreamsFilePath = path;
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

            // Load chat streams after dock layout
            LoadChatStreams();
        }
        catch (Exception ex)
        {
            // If layout is corrupted, fall back to default
            Console.WriteLine($"Failed to load layout: {ex.Message}");
            LoadDefaultLayout();
            LoadDefaultChatStreams();
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
            
            // Delete the chat streams file to reset to defaults
            string chatStreamsFile = GetChatStreamsFilePath();
            if (File.Exists(chatStreamsFile))
            {
                File.Delete(chatStreamsFile);
            }
            
            // Explicitly reset panels to default layout
            sessionsPanel!.Dock = DockingStyle.Left;
            sessionsPanel!.Width = 250;
            artifactsPanel!.Dock = DockingStyle.Right;
            artifactsPanel!.Width = 300;
            conversationPanel!.Dock = DockingStyle.Fill;
            eventsPanel!.Dock = DockingStyle.Bottom;
            eventsPanel!.Height = 200;
            
            // Reset chat streams to default
            LoadDefaultChatStreams();
            
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

    private void AddNewChatStream()
    {
        if (_accordionManager == null) return;

        // Generate a unique agent name
        var agentNumber = _accordionManager.ChatStreams.Count + 1;
        var agentName = $"Agent {agentNumber}";
        
        // Create new chat stream with mock status
        var chatStream = new ChatStream(Guid.NewGuid(), agentName);
        
        // Add some variety to the mock data
        var mockTasks = new[] { "Analyzing code", "Running tests", "Fixing bugs", "Implementing feature", "Reviewing changes" };
        var mockStatuses = new[] { SessionStatus.Running, SessionStatus.Paused, SessionStatus.Error, SessionStatus.NeedsApproval };
        
        var random = new Random();
        chatStream.Status = mockStatuses[random.Next(mockStatuses.Length)];
        chatStream.CurrentTask = mockTasks[random.Next(mockTasks.Length)];
        chatStream.ProgressPercentage = random.Next(0, 101);
        
        _accordionManager.AddChatStream(chatStream);
        
        Console.WriteLine($"Added new chat stream: {agentName} ({chatStream.Status}) - {chatStream.CurrentTask}");
    }

    private void AddTestChatStreams()
    {
        if (_accordionManager == null) return;

        // Add test streams with different statuses
        var testStreams = new[]
        {
            ("Agent 2", SessionStatus.Running, "Analyzing code", 75),
            ("Agent 3", SessionStatus.Paused, "Running tests", 45),
            ("Agent 4", SessionStatus.Error, "Fixing bugs", 0),
            ("Agent 5", SessionStatus.Completed, "Feature complete", 100)
        };

        foreach (var (name, status, task, progress) in testStreams)
        {
            var stream = new ChatStream(Guid.NewGuid(), name);
            stream.Status = status;
            stream.CurrentTask = task;
            stream.ProgressPercentage = progress;
            _accordionManager.AddChatStream(stream);

            // Add some mock artifacts for this stream
            AddMockArtifactsForStream(stream.SessionId, name);
        }
    }

    private void AddMockArtifactsForStream(Guid sessionId, string agentName)
    {
        var mockArtifacts = new[]
        {
            new Artifact("test-results", "Unit Test Results", $"Test results for {agentName} - 15/15 tests passed"),
            new Artifact("build-log", "Build Log", $"Build log for {agentName} - compilation successful"),
            new Artifact("code-coverage", "Code Coverage Report", $"Coverage report for {agentName} - 85% coverage achieved"),
            new Artifact("diff-output", "Code Changes", $"Git diff showing changes made by {agentName}")
        };

        foreach (var artifact in mockArtifacts)
        {
            AddArtifactToChat(sessionId, artifact);
        }
    }

    private void RemoveActiveChatStream()
    {
        if (_accordionManager == null || _accordionManager.ChatStreams.Count <= 1) return;

        // Find the currently expanded stream
        var expandedStream = _accordionManager.ChatStreams.FirstOrDefault(s => s.IsExpanded);
        if (expandedStream != null)
        {
            _accordionManager.RemoveChatStream(expandedStream);
            Console.WriteLine($"Removed chat stream: {expandedStream.AgentName}");
        }
    }

    private void FilterArtifactsByChat(string selectedChat)
    {
        RefreshArtifactsTree(selectedChat);
    }

    private void RefreshArtifactsTree(string filterByChat = "All Chats")
    {
        if (artifactsTree == null) return;

        artifactsTree.Nodes.Clear();

        if (filterByChat == "All Chats")
        {
            // Show artifacts from all chats
            foreach (var chatStream in _accordionManager?.ChatStreams ?? Enumerable.Empty<ChatStream>())
            {
                AddChatArtifactsToTree(chatStream);
            }
        }
        else
        {
            // Show artifacts from specific chat
            var chatStream = _accordionManager?.ChatStreams.FirstOrDefault(s => s.AgentName == filterByChat);
            if (chatStream != null)
            {
                AddChatArtifactsToTree(chatStream);
            }
        }

        // Expand all nodes for better visibility
        artifactsTree.ExpandAll();
    }

    private void AddChatArtifactsToTree(ChatStream chatStream)
    {
        if (!_chatArtifacts.ContainsKey(chatStream.SessionId) || !_chatArtifacts[chatStream.SessionId].Any())
        {
            // Add a placeholder node if no artifacts
            var chatNode = artifactsTree.AppendNode(new object[] { $"{chatStream.AgentName} - No artifacts yet" }, null);
            return;
        }

        // Add chat node
        var rootNode = artifactsTree.AppendNode(new object[] { chatStream.AgentName }, null);

        // Group artifacts by type
        var artifactsByType = _chatArtifacts[chatStream.SessionId].GroupBy(a => a.Kind);

        foreach (var typeGroup in artifactsByType)
        {
            var typeNode = artifactsTree.AppendNode(new object[] { typeGroup.Key }, rootNode);

            foreach (var artifact in typeGroup)
            {
                var artifactNode = artifactsTree.AppendNode(new object[] { artifact.Name }, typeNode);
                // Store artifact data for double-click handling
                artifactNode.Tag = artifact;
            }
        }
    }

    public void AddArtifactToChat(Guid sessionId, Artifact artifact)
    {
        if (!_chatArtifacts.ContainsKey(sessionId))
        {
            _chatArtifacts[sessionId] = new List<Artifact>();
        }

        _chatArtifacts[sessionId].Add(artifact);

        // Refresh the artifacts tree if currently showing this chat or all chats
        var currentFilter = _chatFilterCombo?.SelectedItem?.ToString() ?? "All Chats";
        if (currentFilter == "All Chats" || 
            _accordionManager?.ChatStreams.Any(s => s.SessionId == sessionId && s.AgentName == currentFilter) == true)
        {
            RefreshArtifactsTree(currentFilter);
        }
    }

    private void UpdateChatFilterOptions()
    {
        if (_chatFilterCombo == null || _accordionManager == null) return;

        _chatFilterCombo.Items.Clear();
        _chatFilterCombo.Items.Add("All Chats");

        foreach (var stream in _accordionManager.ChatStreams)
        {
            _chatFilterCombo.Items.Add(stream.AgentName);
        }

        // Keep current selection if it still exists, otherwise default to "All Chats"
        if (_chatFilterCombo.SelectedItem == null)
        {
            _chatFilterCombo.SelectedIndex = 0;
        }
    }

    public void AddBlockingCondition(BlockingCondition condition)
    {
        _blockingConditions.Add(condition);
        UpdateBlockingBanner();
    }

    public void RemoveBlockingCondition(Guid sessionId, BlockingType type)
    {
        _blockingConditions.RemoveAll(c => c.SessionId == sessionId && c.Type == type);
        UpdateBlockingBanner();
    }

    private void UpdateBlockingBanner()
    {
        if (_blockingConditions.Count == 0)
        {
            _blockingBannerPanel!.Visible = false;
            return;
        }

        // Show the most recent blocking condition
        var currentCondition = _blockingConditions.OrderByDescending(c => c.Timestamp).First();

        _blockingBannerLabel!.Text = currentCondition.Message;
        _blockingBannerActionButton!.Text = currentCondition.ActionText;
        _blockingBannerActionButton!.Visible = !string.IsNullOrEmpty(currentCondition.ActionText);
        _blockingBannerActionButton!.Tag = currentCondition; // Store for action handling

        _blockingBannerPanel!.Visible = true;
    }

    private void ArtifactsTree_DoubleClick(object? sender, EventArgs e)
    {
        if (artifactsTree?.Selection?.Count > 0)
        {
            var selectedNode = artifactsTree.Selection[0];
            if (selectedNode?.Tag is Artifact artifact)
            {
                // Open/select the artifact
                OpenArtifact(artifact);
            }
        }
    }

    private void OpenArtifact(Artifact artifact)
    {
        try
        {
            // For now, show artifact content in a message box or dialog
            // In a real implementation, this might open in an editor, browser, etc.
            var content = artifact.InlineText ?? "[Binary artifact - cannot display inline]";
            
            using var dialog = new Form
            {
                Text = artifact.Name,
                Size = new Size(600, 400),
                StartPosition = FormStartPosition.CenterParent
            };
            
            var textBox = new System.Windows.Forms.TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = content,
                Font = new Font("Consolas", 9)
            };
            
            dialog.Controls.Add(textBox);
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open artifact: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BlockingBannerActionButton_Click(object? sender, EventArgs e)
    {
        if (_blockingBannerActionButton?.Tag is BlockingCondition condition)
        {
            HandleBlockingAction(condition);
        }
    }

    private void HandleBlockingAction(BlockingCondition condition)
    {
        switch (condition.Type)
        {
            case BlockingType.NeedsApproval:
                // In a real app, this would show an approval dialog
                // For now, just remove the blocking condition
                RemoveBlockingCondition(condition.SessionId, condition.Type);
                Console.WriteLine($"Approved session {condition.SessionId}");
                break;

            case BlockingType.ConflictDetected:
                // In a real app, this would show conflict resolution UI
                // For now, just remove the blocking condition
                RemoveBlockingCondition(condition.SessionId, condition.Type);
                Console.WriteLine($"Resolved conflict for session {condition.SessionId}");
                break;

            case BlockingType.Throttled:
                // Throttling is time-based, just wait
                // Could show retry options
                Console.WriteLine($"Acknowledged throttling for session {condition.SessionId}");
                break;
        }
    }
}
