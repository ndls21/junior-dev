using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class SessionState
{
    private readonly Channel<IEvent> _eventChannel = Channel.CreateUnbounded<IEvent>();
    private readonly List<IEvent> _eventLog = new();
    private readonly Queue<ICommand> _pendingApproval = new();

    public SessionConfig Config { get; }
    public string WorkspacePath { get; }
    public SessionStatus Status { get; private set; } = SessionStatus.Running;
    public bool IsApproved { get; private set; }
    public IReadOnlyList<IEvent> Events => _eventLog;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
    public string? CurrentTask { get; private set; }

    public SessionState(SessionConfig config, string workspacePath)
    {
        Config = config;
        WorkspacePath = workspacePath;
    }

    public async Task AddEvent(IEvent @event)
    {
        _eventLog.Add(@event);
        await _eventChannel.Writer.WriteAsync(@event);
    }

    public IAsyncEnumerable<IEvent> GetEvents()
    {
        return _eventChannel.Reader.ReadAllAsync();
    }

    public void Complete()
    {
        _eventChannel.Writer.Complete();
    }

    public async Task SetStatus(SessionStatus newStatus, string? reason = null)
    {
        Status = newStatus;
        var statusEvent = new SessionStatusChanged(
            Guid.NewGuid(),
            new Correlation(Config.SessionId),
            newStatus,
            reason);

        await AddEvent(statusEvent);
    }

    public void SetApproved(bool approved)
    {
        IsApproved = approved;
    }

    public void EnqueuePending(ICommand command)
    {
        _pendingApproval.Enqueue(command);
    }

    public bool TryDequeuePending(out ICommand? command)
    {
        if (_pendingApproval.Count > 0)
        {
            command = _pendingApproval.Dequeue();
            return true;
        }

        command = null;
        return false;
    }

    public void SetCurrentTask(string? task)
    {
        CurrentTask = task;
    }
}
