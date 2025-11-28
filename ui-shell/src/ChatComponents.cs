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

    public ChatStream ChatStream => _chatStream;
    public AIChatControl ChatControl => _chatControl;
    public MemoEdit EventsMemo => _eventsMemo;

    public AgentPanel(ChatStream chatStream)
    {
        _chatStream = chatStream;
        _chatStream.Panel = this;

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
        this.BorderStyle = BorderStyle.FixedSingle;
        this.Padding = new Padding(2);
    }

    public void RenderEvent(IEvent @event, DateTimeOffset timestamp)
    {
        // Only render events for this chat stream's session
        if (@event.Correlation.SessionId == _chatStream.SessionId)
        {
            _eventRenderer.RenderEvent(@event, timestamp);
            _chatStream.LastActivity = timestamp;
        }
    }

    public void UpdateStatus(SessionStatus status, string? currentTask = null, int? progress = null)
    {
        _chatStream.Status = status;
        if (currentTask != null) _chatStream.CurrentTask = currentTask;
        if (progress.HasValue) _chatStream.ProgressPercentage = progress.Value;
        _chatStream.LastActivity = DateTimeOffset.Now;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _chatControl?.Dispose();
            _eventsMemo?.Dispose();
            _splitContainer?.Dispose();
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
            chatStream.Panel.Height = 400; // Expanded height
            chatStream.Panel.Visible = true;
        }

        StreamExpanded?.Invoke(this, chatStream);
        RefreshLayout();
    }

    public void CollapseStream(ChatStream chatStream)
    {
        chatStream.IsExpanded = false;

        if (chatStream.Panel != null)
        {
            chatStream.Panel.Height = 60; // Collapsed height for preview
            chatStream.Panel.Visible = true;
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