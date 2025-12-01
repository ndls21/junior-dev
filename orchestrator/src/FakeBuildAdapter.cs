using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class FakeBuildAdapter : FakeAdapter
{
    public override bool CanHandle(ICommand command)
    {
        return command is BuildProject;
    }

    public override async Task HandleCommand(ICommand command, SessionState session)
    {
        if (command is BuildProject buildCommand)
        {
            await HandleBuildProject(buildCommand, session);
        }
        else
        {
            // For other commands, use base implementation
            await base.HandleCommand(command, session);
        }
    }

    private async Task HandleBuildProject(BuildProject command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Fake implementation: simulate a successful build
        // In a real implementation, this would execute dotnet build or similar

        // Emit CommandCompleted with success
        var completedEvent = new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Success,
            $"Fake build completed successfully for project '{command.ProjectPath}'");

        await session.AddEvent(completedEvent);
    }
}