using System;
using System.Collections.Generic;
using DevExpress.AIIntegration.WinForms.Chat;
using DevExpress.XtraEditors;
using JuniorDev.Contracts;

namespace Ui.Shell;

/// <summary>
/// Represents a single chat stream with an AI agent
/// </summary>
public class ChatStream
{
    public Guid SessionId { get; set; }
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; } = "Agent";
    public SessionStatus Status { get; set; } = SessionStatus.Running;
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
        return Status switch
        {
            SessionStatus.Running => $"🔄 {AgentName} - {CurrentTask}",
            SessionStatus.Paused => $"⏸️ {AgentName} - Paused",
            SessionStatus.Error => $"❌ {AgentName} - Error",
            SessionStatus.NeedsApproval => $"⚠️ {AgentName} - Needs Approval",
            SessionStatus.Completed => $"✅ {AgentName} - Completed",
            _ => $"❓ {AgentName} - Unknown"
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
    private readonly AIChatControl _chatControl;
    private readonly MemoEdit _eventsMemo;
    private readonly EventRenderer _eventRenderer;
    private readonly SplitContainer _splitContainer;
    
    // Rich preview controls for collapsed state
    private readonly Panel _previewPanel;
    private readonly System.Windows.Forms.Label _agentNameLabel;
    private readonly System.Windows.Forms.Label _statusLabel;
    private readonly System.Windows.Forms.Label _taskLabel;
    private readonly System.Windows.Forms.Label _progressLabel;
    private readonly System.Windows.Forms.Label _timeLabel;

    public ChatStream ChatStream => _chatStream;
    public AIChatControl ChatControl => _chatControl;
    public MemoEdit EventsMemo => _eventsMemo;

    public AgentPanel(ChatStream chatStream)
    {
        _chatStream = chatStream;
        _chatStream.Panel = this;

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

        // Initialize components
        _chatControl = new AIChatControl
        {
            Dock = DockStyle.Fill,
            UseStreaming = DevExpress.Utils.DefaultBoolean.True
        };

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

        _splitContainer.Panel1.Controls.Add(_chatControl);
        _splitContainer.Panel2.Controls.Add(_eventsMemo);

        this.Controls.Add(_splitContainer);
        this.Controls.Add(_previewPanel); // Add preview panel on top
        this.BorderStyle = BorderStyle.FixedSingle;
        this.Padding = new Padding(2);

        // Initially show preview (collapsed state)
        UpdatePreviewDisplay();
        SetCollapsedState();
    }

    private void OnPreviewPanelClicked()
    {
        // Notify parent to expand this panel
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ExpandRequested;

    public void RenderEvent(IEvent @event, DateTimeOffset timestamp)
    {
        // Only render events for this chat stream's session
        if (@event.Correlation.SessionId == _chatStream.SessionId)
        {
            _eventRenderer.RenderEvent(@event, timestamp);
            _chatStream.LastActivity = timestamp;
            UpdatePreviewDisplay();
        }
    }

    public void UpdateStatus(SessionStatus status, string? currentTask = null, int? progress = null)
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
            SessionStatus.Running => $"🔄 {_chatStream.CurrentTask}",
            SessionStatus.Paused => "⏸️ Paused",
            SessionStatus.Error => "❌ Error",
            SessionStatus.NeedsApproval => "⚠️ Needs Approval",
            SessionStatus.Completed => "✅ Completed",
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
            SessionStatus.Running => Color.Green,
            SessionStatus.Paused => Color.Orange,
            SessionStatus.Error => Color.Red,
            SessionStatus.NeedsApproval => Color.DarkOrange,
            SessionStatus.Completed => Color.Blue,
            _ => Color.Gray
        };
        _statusLabel.ForeColor = statusColor;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _chatControl?.Dispose();
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

    public IReadOnlyList<ChatStream> ChatStreams => _chatStreams.AsReadOnly();
    public event EventHandler<ChatStream>? StreamExpanded;
    public event EventHandler<ChatStream>? StreamCollapsed;

    public AccordionLayoutManager(Panel container)
    {
        _container = container;
        _container.AutoScroll = true;
    }

    public void AddChatStream(ChatStream chatStream)
    {
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