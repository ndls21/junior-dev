using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DevExpress.AIIntegration.WinForms.Chat;
using DevExpress.AIIntegration.Blazor.Chat.WebView;
using DevExpress.AIIntegration.Blazor.Chat;
using DevExpress.XtraEditors;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Ui.Shell;

/// <summary>
/// Represents a single chat stream with an AI agent
/// </summary>
public class ChatStream
{
    public Guid SessionId { get; set; }
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; } = "Agent";
    private JuniorDev.Contracts.SessionStatus _status = JuniorDev.Contracts.SessionStatus.Running;
    public JuniorDev.Contracts.SessionStatus Status 
    { 
        get 
        {
            if (AgentName.Contains("Agent-") || AgentName.StartsWith("Agent "))
            {
                Console.WriteLine($"ChatStream {AgentName}: Status getter returning {_status}");
            }
            return _status; 
        }
        set 
        { 
            if (AgentName.Contains("Agent-") || AgentName.StartsWith("Agent "))
            {
                Console.WriteLine($"ChatStream {AgentName}: Status changing from {_status} to {value}");
            }
            _status = value; 
        } 
    }
    public string CurrentTask { get; set; } = "";
    public int ProgressPercentage { get; set; } = 0;
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.Now;
    public bool IsExpanded { get; set; } = false;

    // UI Components
    public AgentPanel? Panel { get; set; }

    public ChatStream(Guid sessionId, string agentName = "Agent")
    {
        SessionId = sessionId;
        AgentName = agentName;
        LastActivity = DateTimeOffset.Now;
    }

    public string GetStatusText()
    {
        var sessionInfo = !string.IsNullOrEmpty(SessionId.ToString()) ? $" ({SessionId.ToString().Substring(0, 8)})" : "";
        return Status switch
        {
            JuniorDev.Contracts.SessionStatus.Running => $"🔄 {AgentName}{sessionInfo} - {CurrentTask}",
            JuniorDev.Contracts.SessionStatus.Paused => $"⏸️ {AgentName}{sessionInfo} - Paused",
            JuniorDev.Contracts.SessionStatus.Error => $"❌ {AgentName}{sessionInfo} - Error",
            JuniorDev.Contracts.SessionStatus.NeedsApproval => $"⚠️ {AgentName}{sessionInfo} - Needs Approval",
            JuniorDev.Contracts.SessionStatus.Completed => $"✅ {AgentName}{sessionInfo} - Completed",
            _ => $"❓ {AgentName}{sessionInfo} - Unknown"
        };
    }

    public string GetCollapsedPreview()
    {
        var status = GetStatusText();
        var progress = ProgressPercentage > 0 ? $" ({ProgressPercentage}%)" : "";
        var time = LastActivity.ToString("HH:mm");
        return $"{status}{progress} - {time}";
    }
}

/// <summary>
/// UI control that hosts an AI Chat + scoped event feed for a single agent
/// </summary>
public class AgentPanel : Panel
{
    private readonly ChatStream _chatStream;
    private readonly Control _chatControl;
    private readonly MemoEdit _eventsMemo;
    private readonly EventRenderer _eventRenderer;
    private readonly SplitContainer _splitContainer;
    
    // Transcript history display
    private readonly Panel _transcriptHistoryPanel;
    private readonly Label _transcriptHistoryLabel;
    private readonly TextBox _transcriptHistoryText;
    
    // Command publishing
    private ISessionManager? _sessionManager;
    private IServiceProvider? _serviceProvider;
    private readonly ToolStrip _commandToolbar;
    private readonly ContextMenuStrip _contextMenu;
    
    // Rich preview controls for collapsed state
    private readonly Panel _previewPanel;
    private readonly System.Windows.Forms.Label _agentNameLabel;
    private readonly System.Windows.Forms.Label _statusLabel;
    private readonly System.Windows.Forms.Label _taskLabel;
    private readonly System.Windows.Forms.Label _progressLabel;
    private readonly System.Windows.Forms.Label _timeLabel;

    // Transcript persistence
    private readonly TranscriptStorage _transcriptStorage;
    private readonly TranscriptConfig _transcriptConfig;
    private ChatTranscript? _currentTranscript;
    private System.Windows.Forms.Timer? _transcriptSaveTimer;
    private int _lastSavedMessageCount = 0;

    private readonly bool _isTestMode;

    public ChatStream ChatStream => _chatStream;
    public Control ChatControl => _chatControl;
    public MemoEdit EventsMemo => _eventsMemo;

    public AgentPanel(ChatStream chatStream, ISessionManager? sessionManager = null, IServiceProvider? serviceProvider = null, IConfiguration? configuration = null, bool isTestMode = false)
    {
        _chatStream = chatStream;
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
        _isTestMode = isTestMode;
        _chatStream.Panel = this;

        // Get transcript configuration from appsettings
        TranscriptConfig transcriptConfig;
        if (configuration != null)
        {
            var appConfig = JuniorDev.Contracts.ConfigBuilder.GetAppConfig(configuration);
            var contractsConfig = appConfig.Transcript;
            transcriptConfig = new TranscriptConfig
            {
                Enabled = contractsConfig.Enabled,
                MaxMessagesPerTranscript = contractsConfig.MaxMessagesPerTranscript,
                MaxTranscriptSizeBytes = contractsConfig.MaxTranscriptSizeBytes,
                MaxTranscriptAge = contractsConfig.MaxTranscriptAge,
                TranscriptContextMessages = contractsConfig.TranscriptContextMessages,
                StorageDirectory = contractsConfig.StorageDirectory ?? string.Empty
            };
        }
        else
        {
            // Fallback to default config if no configuration provided
            transcriptConfig = new TranscriptConfig();
        }

        _transcriptConfig = transcriptConfig;
        _transcriptStorage = new TranscriptStorage(transcriptConfig);

        // Try to load existing transcript for this session
        _currentTranscript = _transcriptStorage.LoadTranscript(_chatStream.SessionId);
        if (_currentTranscript == null)
        {
            // Create new transcript if none exists
            _currentTranscript = new ChatTranscript(_chatStream.SessionId, _chatStream.AgentName);
        }

        // Initialize transcript save timer to periodically check for new AI messages
        InitializeTranscriptSaveTimer();

        if (_isTestMode)
        {
            // In test mode, create minimal UI components to avoid threading issues
            _previewPanel = new Panel { Dock = DockStyle.Top, Height = 60 };
            _agentNameLabel = new System.Windows.Forms.Label { Text = _chatStream.AgentName };
            _statusLabel = new System.Windows.Forms.Label { Text = "Test Mode" };
            _taskLabel = new System.Windows.Forms.Label { Text = "" };
            _progressLabel = new System.Windows.Forms.Label { Text = "" };
            _timeLabel = new System.Windows.Forms.Label { Text = "" };
            _commandToolbar = new ToolStrip();
            _contextMenu = new ContextMenuStrip();
            _chatControl = new Label { Text = "AI Chat Unavailable (Test Mode)", Dock = DockStyle.Fill };
            _eventsMemo = new MemoEdit { Dock = DockStyle.Fill };
            _eventRenderer = new EventRenderer(_eventsMemo);
            _splitContainer = new SplitContainer { Dock = DockStyle.Fill };
            _transcriptHistoryPanel = new Panel { Dock = DockStyle.Top, Height = 150 };
            _transcriptHistoryLabel = new Label { Text = "Transcript History" };
            _transcriptHistoryText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };

            // Set basic properties
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Padding = new Padding(2);
            UpdatePreviewDisplay();
            SetCollapsedState();
            return;
        }

        // Initialize rich preview controls
        _previewPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(240, 240, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand
        };

        _agentNameLabel = new System.Windows.Forms.Label
        {
            Location = new Point(5, 5),
            Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold),
            AutoSize = true
        };

        _statusLabel = new System.Windows.Forms.Label
        {
            Location = new Point(5, 25),
            Font = new Font(FontFamily.GenericSansSerif, 8),
            AutoSize = true
        };

        _taskLabel = new System.Windows.Forms.Label
        {
            Location = new Point(5, 40),
            Font = new Font(FontFamily.GenericSansSerif, 8),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        _progressLabel = new System.Windows.Forms.Label
        {
            Location = new Point(200, 5),
            Font = new Font(FontFamily.GenericSansSerif, 8),
            AutoSize = true,
            TextAlign = ContentAlignment.TopRight
        };

        _timeLabel = new System.Windows.Forms.Label
        {
            Location = new Point(200, 40),
            Font = new Font(FontFamily.GenericSansSerif, 7),
            AutoSize = true,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.TopRight
        };

        _previewPanel.Controls.AddRange(new Control[] { 
            _agentNameLabel, _statusLabel, _taskLabel, _progressLabel, _timeLabel 
        });

        // Handle click to expand
        _previewPanel.Click += (s, e) => OnPreviewPanelClicked();

        // Initialize command toolbar
        _commandToolbar = new ToolStrip { Dock = DockStyle.Top };
        InitializeCommandToolbar();

        // Initialize context menu
        _contextMenu = new ContextMenuStrip();
        InitializeContextMenu();

        // Check if we have valid AI credentials before creating AI chat control
        var hasValidCredentials = CheckValidAICredentials();
        
        if (hasValidCredentials)
        {
            // Try to create AI chat control only when we have valid credentials
            try
            {
                _chatControl = new AIChatControl
                {
                    Dock = DockStyle.Fill,
                    UseStreaming = DevExpress.Utils.DefaultBoolean.True
                };

                // Load existing transcript into the chat control if available
                if (_currentTranscript != null && _currentTranscript.Messages.Any())
                {
                    LoadTranscriptIntoChatControl(_currentTranscript);
                }

                // Hook into message events to save new messages
                HookIntoChatMessageEvents();

                // Global DevExpress AI configuration in Program.cs should handle service provider setup
                // No need to set per-control service providers here
                Console.WriteLine($"AI chat control created for agent '{_chatStream.AgentName}' with {_currentTranscript?.Messages.Count ?? 0} existing messages");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create AI chat control for agent '{_chatStream.AgentName}': {ex.Message}");
                // Create a placeholder control when AI is not available
                _chatControl = new Label { Text = "AI Chat Unavailable", Dock = DockStyle.Fill };
            }
        }
        else
        {
            // No valid AI credentials - use placeholder control
            Console.WriteLine($"No valid AI credentials found - using placeholder for agent '{_chatStream.AgentName}'");
            _chatControl = new Label { Text = "AI Chat Unavailable", Dock = DockStyle.Fill };
        }

        _eventsMemo = new MemoEdit
        {
            Dock = DockStyle.Fill,
            Properties =
            {
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            },
            Height = 150
        };

        _eventRenderer = new EventRenderer(_eventsMemo);

        // Setup split container for chat + events
        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 300, // Chat takes most space
            Panel1MinSize = 200,
            Panel2MinSize = 100
        };

        // Create transcript history panel (read-only)
        _transcriptHistoryPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 150,
            BorderStyle = BorderStyle.FixedSingle
        };

        _transcriptHistoryLabel = new Label
        {
            Text = "Transcript History",
            Dock = DockStyle.Top,
            Font = new Font(this.Font, FontStyle.Bold),
            Height = 20
        };

        _transcriptHistoryText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.WhiteSmoke,
            Font = new Font("Consolas", 9)
        };

        _transcriptHistoryPanel.Controls.AddRange(new Control[] { _transcriptHistoryText, _transcriptHistoryLabel });

        // Create chat panel that contains history + AI chat control
        var chatPanel = new Panel { Dock = DockStyle.Fill };
        chatPanel.Controls.AddRange(new Control[] { _transcriptHistoryPanel, _chatControl });

        _splitContainer.Panel1.Controls.Add(chatPanel);
        _splitContainer.Panel2.Controls.Add(_eventsMemo);

        // Create main container with command toolbar on top
        var mainContainer = new Panel { Dock = DockStyle.Fill };
        mainContainer.Controls.AddRange(new Control[] { _commandToolbar, _splitContainer });

        this.Controls.Add(mainContainer);
        this.Controls.Add(_previewPanel); // Add preview panel on top
        this.BorderStyle = BorderStyle.FixedSingle;
        this.Padding = new Padding(2);

        // Initially show preview (collapsed state)
        UpdatePreviewDisplay();
        SetCollapsedState();
    }

    private void InitializeCommandToolbar()
    {
        // Add command buttons
        var buildButton = new ToolStripButton("Build", null, (s, e) => PublishCommand("Build"));
        var testButton = new ToolStripButton("Test", null, (s, e) => PublishCommand("Test"));
        var commentButton = new ToolStripButton("Comment", null, (s, e) => PublishCommand("Comment"));
        var transitionButton = new ToolStripButton("Transition", null, (s, e) => PublishCommand("Transition"));

        _commandToolbar.Items.AddRange(new ToolStripItem[] { buildButton, testButton, commentButton, transitionButton });

        // Store button references for later updates
        _buildButton = buildButton;
        _testButton = testButton;
        _commentButton = commentButton;
        _transitionButton = transitionButton;

        // Initially update button states
        UpdateCommandButtonStates();
    }

    private ToolStripButton? _buildButton;
    private ToolStripButton? _testButton;
    private ToolStripButton? _commentButton;
    private ToolStripButton? _transitionButton;

    private void UpdateCommandButtonStates()
    {
        // Skip in test mode where buttons are not created
        if (_isTestMode) return;

        // Enable buttons if this chat stream is attached to an active session
        // Check if session manager is available and this stream has a valid session
        bool enableCommands = false;
        if (_sessionManager != null)
        {
            var activeSessions = _sessionManager.GetActiveSessions();
            enableCommands = activeSessions.Any(s => s.SessionId == _chatStream.SessionId);
        }

        _buildButton!.Enabled = enableCommands;
        _testButton!.Enabled = enableCommands;
        _commentButton!.Enabled = enableCommands;
        _transitionButton!.Enabled = enableCommands;

        if (!enableCommands)
        {
            _buildButton!.ToolTipText = "Attach to an active session to enable commands";
            _testButton!.ToolTipText = "Attach to an active session to enable commands";
            _commentButton!.ToolTipText = "Attach to an active session to enable commands";
            _transitionButton!.ToolTipText = "Attach to an active session to enable commands";
        }
        else
        {
            _buildButton!.ToolTipText = "Build the project";
            _testButton!.ToolTipText = "Run tests";
            _commentButton!.ToolTipText = "Add a comment to work item";
            _transitionButton!.ToolTipText = "Transition work item state";
        }
    }

    private void InitializeContextMenu()
    {
        // Add session management menu items
        var createSessionItem = new ToolStripMenuItem("Create New Session", null, (s, e) => CreateSessionForChatStream());
        var attachToSessionItem = new ToolStripMenuItem("Attach to Session...", null, (s, e) => AttachChatStreamToSession());

        _contextMenu.Items.AddRange(new ToolStripItem[] { createSessionItem, attachToSessionItem });

        // Assign context menu to the panel
        this.ContextMenuStrip = _contextMenu;
    }

    private async void PublishCommand(string commandType)
    {
        if (_sessionManager == null)
        {
            Console.WriteLine("Session manager not available - cannot publish commands");
            return;
        }

        // Get the session config for this chat stream
        var sessionConfig = _sessionManager.GetSessionConfig(_chatStream.SessionId);
        if (sessionConfig == null)
        {
            Console.WriteLine($"Session {_chatStream.SessionId} not found - cannot publish commands");
            return;
        }

        try
        {
            var commandId = Guid.NewGuid();
            var correlation = new Correlation(_chatStream.SessionId);
            
            ICommand command = commandType switch
            {
                "Build" => new BuildProject(commandId, correlation, sessionConfig.Repo, ".", "Release", "net8.0", null, TimeSpan.FromMinutes(5)),
                "Test" => new RunTests(commandId, correlation, sessionConfig.Repo, null, TimeSpan.FromMinutes(5)),
                "Comment" => sessionConfig.WorkItem != null 
                    ? new Comment(commandId, correlation, sessionConfig.WorkItem, "UI-triggered comment")
                    : throw new InvalidOperationException("No work item configured for this session"),
                "Transition" => sessionConfig.WorkItem != null
                    ? new TransitionTicket(commandId, correlation, sessionConfig.WorkItem, "In Progress")
                    : throw new InvalidOperationException("No work item configured for this session"),
                _ => throw new ArgumentException($"Unknown command type: {commandType}")
            };
            
            await _sessionManager.PublishCommand(command);
            Console.WriteLine($"Published {commandType} command for session {_chatStream.SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to publish {commandType} command: {ex.Message}");
            // In a real app, this would show an error dialog
        }
    }

    private bool CheckValidAICredentials()
    {
        try
        {
            // Check if we have a service provider and chat client factory
            if (_serviceProvider == null)
            {
                Console.WriteLine("No service provider available for credential check");
                return false;
            }

            var factory = _serviceProvider.GetService(typeof(IChatClientFactory)) as IChatClientFactory;
            if (factory == null)
            {
                Console.WriteLine("ChatClientFactory not available in service provider");
                return false;
            }

            // Try to get a client for this agent - if it returns a real client, we have valid credentials
            var agentProfile = _chatStream.AgentName.ToLower().Replace(" ", "-");
            var underlyingClient = factory.GetUnderlyingClientFor(agentProfile);
            
            // If we get a real Microsoft.Extensions.AI.IChatClient, credentials are valid
            return underlyingClient is Microsoft.Extensions.AI.IChatClient;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking AI credentials: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads an existing transcript into the UI (history panel and optionally chat control)
    /// </summary>
    private void LoadTranscriptIntoChatControl(ChatTranscript transcript)
    {
        // Display transcript history in the read-only text box
        DisplayTranscriptHistory(transcript);

        if (_chatControl is not AIChatControl aiChatControl)
            return;

        try
        {
            // For the AI chat control, load the configured number of most recent messages as context
            var contextMessageCount = _transcriptConfig.TranscriptContextMessages;
            var recentMessages = transcript.Messages
                .OrderByDescending(m => m.Timestamp)
                .Take(contextMessageCount) // Use configured count for context
                .OrderBy(m => m.Timestamp) // Re-order chronologically
                .ToList();

            // TODO: Implement summarization for messages beyond context count (issue #48)
            // If there are more messages than the context count, create a summary message
            // from the older messages and prepend it to the context messages.

            if (recentMessages.Any())
            {
                // Convert our ChatMessage objects to DevExpress BlazorChatMessage objects
                var blazorMessages = new List<DevExpress.AIIntegration.Blazor.Chat.BlazorChatMessage>();
                foreach (var message in recentMessages)
                {
                    // Convert role string to ChatMessageRole enum
                    var chatRole = message.Role.ToLower() switch
                    {
                        "user" => Microsoft.Extensions.AI.ChatRole.User,
                        "assistant" => Microsoft.Extensions.AI.ChatRole.Assistant,
                        "system" => Microsoft.Extensions.AI.ChatRole.System,
                        _ => Microsoft.Extensions.AI.ChatRole.User // Default to User for unknown roles
                    };

                    var blazorMessage = new DevExpress.AIIntegration.Blazor.Chat.BlazorChatMessage(chatRole, message.Content)
                    {
                        Typing = false // Loaded messages are not typing
                    };
                    blazorMessages.Add(blazorMessage);
                }

                // Load context messages into the chat control
                aiChatControl.LoadMessages(blazorMessages);

                Console.WriteLine($"Loaded {blazorMessages.Count} context messages into chat control for session {_chatStream.SessionId} (configured limit: {contextMessageCount})");
                
                // Update our tracking of saved messages
                _lastSavedMessageCount = blazorMessages.Count;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load recent messages into chat control: {ex.Message}");
        }
    }

    /// <summary>
    /// Displays the transcript history in the read-only text box
    /// </summary>
    private void DisplayTranscriptHistory(ChatTranscript transcript)
    {
        if (_transcriptHistoryText == null || transcript == null || !transcript.Messages.Any())
        {
            _transcriptHistoryText.Text = "No previous messages.";
            _transcriptHistoryPanel.Visible = false;
            return;
        }

        try
        {
            var historyText = new System.Text.StringBuilder();
            foreach (var message in transcript.Messages.OrderBy(m => m.Timestamp))
            {
                var timestamp = message.Timestamp.ToString("HH:mm:ss");
                var role = message.Role.ToUpper();
                historyText.AppendLine($"[{timestamp}] {role}: {message.Content}");
                historyText.AppendLine(); // Empty line between messages
            }

            _transcriptHistoryText.Text = historyText.ToString().TrimEnd();
            _transcriptHistoryPanel.Visible = true;

            // Auto-scroll to bottom
            _transcriptHistoryText.SelectionStart = _transcriptHistoryText.Text.Length;
            _transcriptHistoryText.ScrollToCaret();

            Console.WriteLine($"Displayed {transcript.Messages.Count} messages in transcript history for session {_chatStream.SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to display transcript history: {ex.Message}");
            _transcriptHistoryText.Text = "Error loading transcript history.";
        }
    }

    /// <summary>
    /// Hooks into AI chat control message events to save new messages
    /// </summary>
    private void HookIntoChatMessageEvents()
    {
        if (_chatControl is not AIChatControl aiChatControl)
            return;

        try
        {
            // Hook into the MessageSent event
            aiChatControl.MessageSent += OnMessageSent;

            Console.WriteLine("Hooked into chat message events for transcript persistence");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to hook into chat message events: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes a timer to periodically check for new AI messages in the chat control
    /// </summary>
    private void InitializeTranscriptSaveTimer()
    {
        if (!_transcriptConfig.Enabled || _chatControl is not AIChatControl)
            return;

        _transcriptSaveTimer = new System.Windows.Forms.Timer
        {
            Interval = 2000, // Check every 2 seconds
            Enabled = true
        };
        _transcriptSaveTimer.Tick += OnTranscriptSaveTimerTick;
        _transcriptSaveTimer.Start();

        Console.WriteLine("Initialized transcript save timer for periodic AI message detection");
    }

    /// <summary>
    /// Handles when a user sends a message
    /// </summary>
    private void OnMessageSent(object? sender, AIChatControlMessageSentEventArgs e)
    {
        if (_currentTranscript == null) return;

        try
        {
            // The MessageSent event gives us the content of the message that was sent
            // We assume it's from the user since this is a user-initiated send
            _currentTranscript.AddMessage("user", e.Content);
            _transcriptStorage.SaveTranscript(_currentTranscript);
            
            // Update the transcript history display
            DisplayTranscriptHistory(_currentTranscript);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save user message to transcript: {ex.Message}");
        }
    }

    /// <summary>
    /// Timer tick handler that checks for new AI messages in the chat control
    /// </summary>
    private void OnTranscriptSaveTimerTick(object? sender, EventArgs e)
    {
        if (_currentTranscript == null || _chatControl is not AIChatControl aiChatControl)
            return;

        try
        {
            // Get current messages from the chat control
            var currentMessages = aiChatControl.SaveMessages()?.ToList();
            if (currentMessages == null || !currentMessages.Any())
                return;

            // Check if we have new messages since last save
            if (currentMessages.Count <= _lastSavedMessageCount)
                return;

            // Process new messages (skip the ones we've already saved)
            for (int i = _lastSavedMessageCount; i < currentMessages.Count; i++)
            {
                var message = currentMessages[i];
                
                // Convert DevExpress message to our format
                var role = message.Role == DevExpress.AIIntegration.Blazor.Chat.ChatMessageRole.Assistant ? "assistant" : "user";
                var content = message.Content ?? string.Empty;

                // Only save assistant messages (AI responses) - user messages are already saved via MessageSent event
                if (role == "assistant" && !string.IsNullOrWhiteSpace(content))
                {
                    _currentTranscript.AddMessage(role, content);
                    Console.WriteLine($"Saved new AI response to transcript for session {_chatStream.SessionId}");
                }
            }

            // Save the transcript
            _transcriptStorage.SaveTranscript(_currentTranscript);
            
            // Update the transcript history display
            DisplayTranscriptHistory(_currentTranscript);
            
            // Update our tracking
            _lastSavedMessageCount = currentMessages.Count;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to check for new AI messages: {ex.Message}");
        }
    }

    /// <summary>
    /// Public method to add AI responses to the transcript (called when responses are received through other channels)
    /// </summary>
    public void AddAIResponseToTranscript(string content)
    {
        if (_currentTranscript == null) return;

        try
        {
            _currentTranscript.AddMessage("assistant", content);
            _transcriptStorage.SaveTranscript(_currentTranscript);
            
            // Update the transcript history display
            DisplayTranscriptHistory(_currentTranscript);
            
            Console.WriteLine($"Added AI response to transcript for session {_chatStream.SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to add AI response to transcript: {ex.Message}");
        }
    }

    private void OnPreviewPanelClicked()
    {
        // Notify parent to expand this panel
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ExpandRequested;
    public event EventHandler? CreateSessionRequested;
    public event EventHandler? AttachToSessionRequested;

    public void RenderEvent(IEvent @event, DateTimeOffset timestamp)
    {
        // Only render events for this chat stream's session
        if (@event.Correlation.SessionId == _chatStream.SessionId)
        {
            // Handle status changes
            if (@event is SessionStatusChanged statusChanged)
            {
                _chatStream.Status = statusChanged.Status;
                if (!string.IsNullOrEmpty(statusChanged.Reason))
                {
                    _chatStream.CurrentTask = statusChanged.Reason;
                }
                _chatStream.LastActivity = timestamp;
                
                // Debug logging in test mode
                if (_chatStream.AgentName.Contains("Agent-") || _chatStream.AgentName.StartsWith("Agent "))
                {
                    Console.WriteLine($"RenderEvent: Updated chat stream {_chatStream.AgentName} status to {statusChanged.Status} (reason: {statusChanged.Reason}) - Status is now: {_chatStream.Status}");
                }
            }
            else
            {
                _chatStream.LastActivity = timestamp;
            }

            _eventRenderer.RenderEvent(@event, timestamp);
            UpdatePreviewDisplay();
        }
    }

    public void UpdateStatus(JuniorDev.Contracts.SessionStatus status, string? currentTask = null, int? progress = null)
    {
        _chatStream.Status = status;
        if (currentTask != null) _chatStream.CurrentTask = currentTask;
        if (progress.HasValue) _chatStream.ProgressPercentage = progress.Value;
        _chatStream.LastActivity = DateTimeOffset.Now;
        UpdatePreviewDisplay();
    }

    public void SetExpandedState()
    {
        _chatStream.IsExpanded = true;
        _previewPanel.Visible = false;
        _splitContainer.Visible = true;
        this.Height = 400; // Expanded height
        this.BackColor = Color.White;
    }

    public void SetCollapsedState()
    {
        _chatStream.IsExpanded = false;
        _previewPanel.Visible = true;
        _splitContainer.Visible = false;
        this.Height = 60; // Collapsed height
        this.BackColor = Color.FromArgb(245, 245, 245);
        UpdatePreviewDisplay();
    }

    private void UpdatePreviewDisplay()
    {
        _agentNameLabel.Text = _chatStream.AgentName;
        
        // Status with emoji
        var statusText = _chatStream.Status switch
        {
            JuniorDev.Contracts.SessionStatus.Running => $"🔄 {_chatStream.CurrentTask}",
            JuniorDev.Contracts.SessionStatus.Paused => "⏸️ Paused",
            JuniorDev.Contracts.SessionStatus.Error => "❌ Error",
            JuniorDev.Contracts.SessionStatus.NeedsApproval => "⚠️ Needs Approval",
            JuniorDev.Contracts.SessionStatus.Completed => "✅ Completed",
            _ => "❓ Unknown"
        };
        _statusLabel.Text = statusText;

        // Current task (truncated if too long)
        var taskText = string.IsNullOrEmpty(_chatStream.CurrentTask) 
            ? "Idle" 
            : _chatStream.CurrentTask.Length > 25 
                ? _chatStream.CurrentTask[..22] + "..." 
                : _chatStream.CurrentTask;
        _taskLabel.Text = taskText;

        // Progress percentage
        _progressLabel.Text = _chatStream.ProgressPercentage > 0 
            ? $"{_chatStream.ProgressPercentage}%" 
            : "";

        // Last activity time
        _timeLabel.Text = _chatStream.LastActivity.ToString("HH:mm");

        // Color coding based on status
        var statusColor = _chatStream.Status switch
        {
            JuniorDev.Contracts.SessionStatus.Running => Color.Green,
            JuniorDev.Contracts.SessionStatus.Paused => Color.Orange,
            JuniorDev.Contracts.SessionStatus.Error => Color.Red,
            JuniorDev.Contracts.SessionStatus.NeedsApproval => Color.DarkOrange,
            JuniorDev.Contracts.SessionStatus.Completed => Color.Blue,
            _ => Color.Gray
        };
        _statusLabel.ForeColor = statusColor;
    }

    private void CreateSessionForChatStream()
    {
        CreateSessionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AttachChatStreamToSession()
    {
        AttachToSessionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSession(Guid newSessionId)
    {
        _chatStream.SessionId = newSessionId;
        UpdateCommandButtonStates();
    }

    public void SetDependencies(ISessionManager? sessionManager, IServiceProvider? serviceProvider)
    {
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
        UpdateCommandButtonStates();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Stop and dispose transcript save timer
            if (_transcriptSaveTimer != null)
            {
                _transcriptSaveTimer.Stop();
                _transcriptSaveTimer.Dispose();
                _transcriptSaveTimer = null;
            }

            // Save transcript before disposing
            if (_currentTranscript != null)
            {
                try
                {
                    _transcriptStorage.SaveTranscript(_currentTranscript);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save transcript during disposal: {ex.Message}");
                }
            }

            if (_chatControl is AIChatControl aiChatControl)
            {
                aiChatControl.Dispose();
            }
            else
            {
                _chatControl?.Dispose();
            }
            _eventsMemo?.Dispose();
            _splitContainer?.Dispose();
            _previewPanel?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Manages multiple chat streams in an accordion layout
/// </summary>
public class AccordionLayoutManager
{
    private readonly Panel _container;
    private readonly List<ChatStream> _chatStreams = new();
    private ChatStream? _expandedStream;
    private readonly bool _isTestMode;

    public IReadOnlyList<ChatStream> ChatStreams => _chatStreams.AsReadOnly();
    public event EventHandler<ChatStream>? StreamExpanded;
    public event EventHandler<ChatStream>? StreamCollapsed;
    public event EventHandler? StreamsChanged;
    public event EventHandler<AgentPanel>? CreateSessionRequested;
    public event EventHandler<AgentPanel>? AttachToSessionRequested;

    public AccordionLayoutManager(Panel container, bool isTestMode = false)
    {
        _container = container;
        _container.AutoScroll = true;
        _isTestMode = isTestMode;
    }

    public void AddChatStream(ChatStream chatStream, ISessionManager? sessionManager = null, IServiceProvider? serviceProvider = null, IConfiguration? configuration = null)
    {
        // Create the AgentPanel for this chat stream
        if (chatStream.Panel == null)
        {
            chatStream.Panel = new AgentPanel(chatStream, sessionManager, serviceProvider, configuration, _isTestMode);
            
            // Wire up session management events
            if (chatStream.Panel is AgentPanel agentPanel)
            {
                agentPanel.CreateSessionRequested += (s, e) => CreateSessionRequested?.Invoke(this, agentPanel);
                agentPanel.AttachToSessionRequested += (s, e) => AttachToSessionRequested?.Invoke(this, agentPanel);
            }
        }
        
        _chatStreams.Add(chatStream);
        if (_chatStreams.Count == 1)
        {
            // First stream is expanded by default
            ExpandStream(chatStream);
        }
        else
        {
            // Additional streams start collapsed
            CollapseStream(chatStream);
        }
        RefreshLayout();
        StreamsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveChatStream(ChatStream chatStream)
    {
        if (_expandedStream == chatStream)
        {
            _expandedStream = null;
        }
        _chatStreams.Remove(chatStream);
        chatStream.Panel?.Dispose();
        RefreshLayout();
        StreamsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExpandStream(ChatStream chatStream)
    {
        if (_expandedStream != null && _expandedStream != chatStream)
        {
            CollapseStream(_expandedStream);
        }

        _expandedStream = chatStream;
        chatStream.IsExpanded = true;

        if (chatStream.Panel != null)
        {
            chatStream.Panel.SetExpandedState();
            // Wire up the expand requested event
            chatStream.Panel.ExpandRequested += (s, e) => ExpandStream(chatStream);
        }

        StreamExpanded?.Invoke(this, chatStream);
        RefreshLayout();
    }

    public void CollapseStream(ChatStream chatStream)
    {
        chatStream.IsExpanded = false;

        if (chatStream.Panel != null)
        {
            chatStream.Panel.SetCollapsedState();
        }

        if (_expandedStream == chatStream)
        {
            _expandedStream = null;
        }

        StreamCollapsed?.Invoke(this, chatStream);
        RefreshLayout();
    }

    public void ToggleStream(ChatStream chatStream)
    {
        if (chatStream.IsExpanded)
        {
            CollapseStream(chatStream);
        }
        else
        {
            ExpandStream(chatStream);
        }
    }

    public void RouteEventToStreams(IEvent @event, DateTimeOffset timestamp)
    {
        foreach (var stream in _chatStreams)
        {
            stream.Panel?.RenderEvent(@event, timestamp);
        }
    }

    private void RefreshLayout()
    {
        _container.SuspendLayout();

        // Clear existing controls
        _container.Controls.Clear();

        // Add all panels in order
        foreach (var stream in _chatStreams)
        {
            if (stream.Panel != null)
            {
                _container.Controls.Add(stream.Panel);
            }
        }

        _container.ResumeLayout(true);
    }
}
