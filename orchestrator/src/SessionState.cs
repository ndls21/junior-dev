using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class SessionState
{
    private readonly Channel<IEvent> _eventChannel = Channel.CreateUnbounded<IEvent>();
    private readonly List<IEvent> _eventLog = new();

    public SessionConfig Config { get; }

    public SessionState(SessionConfig config)
    {
        Config = config;
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
}