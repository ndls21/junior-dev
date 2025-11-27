using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.GitHub;

public class FakeGitHubAdapter : IAdapter
{
    public bool CanHandle(ICommand command) => command is Comment or TransitionTicket or SetAssignee;

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        var correlation = command switch
        {
            Comment c => c.Correlation,
            TransitionTicket t => t.Correlation,
            SetAssignee s => s.Correlation,
            _ => throw new NotSupportedException()
        };

        await session.AddEvent(new CommandAccepted(Guid.NewGuid(), correlation, command.Id));

        try
        {
            // Simulate processing
            await Task.Delay(10);

            // For fake adapter, always succeed unless invalid transition
            if (command is TransitionTicket t && t.State == "Invalid")
            {
                throw new Exception("Invalid transition");
            }

            var artifact = new Artifact("text", "Fake GitHub Issue Updated", InlineText: "Fake update", ContentType: "text/plain");
            await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), correlation, artifact));
            await session.AddEvent(new CommandCompleted(Guid.NewGuid(), correlation, command.Id, CommandOutcome.Success));
        }
        catch (Exception ex)
        {
            await session.AddEvent(new CommandRejected(Guid.NewGuid(), correlation, command.Id, ex.Message, "VALIDATION_ERROR"));
        }
    }
}