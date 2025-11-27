using System;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public abstract class FakeAdapter : IAdapter
{
    public abstract bool CanHandle(ICommand command);

    public virtual async Task HandleCommand(ICommand command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Simulate processing and emit CommandCompleted
        var completedEvent = new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Success);

        await session.AddEvent(completedEvent);
    }
}