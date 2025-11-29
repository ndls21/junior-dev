using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class FakeVcsAdapter : FakeAdapter
{
    public override bool CanHandle(ICommand command)
    {
        return command is CreateBranch or ApplyPatch or RunTests or Commit or Push or GetDiff;
    }

    public override async Task HandleCommand(ICommand command, SessionState session)
    {
        // Handle specific commands
        if (command is RunTests runTests)
        {
            // Emit CommandAccepted
            var acceptedEvent = new CommandAccepted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id);

            await session.AddEvent(acceptedEvent);
            
            await HandleRunTests(runTests, session);
        }
        else
        {
            // For other commands, use base implementation
            await base.HandleCommand(command, session);
        }
    }

    private async Task HandleRunTests(RunTests command, SessionState session)
    {
        // Fake implementation: generate test results artifact
        var testResults = $"Fake test results for {command.Repo.Path}\nFilter: {command.Filter ?? "All"}\nTimeout: {command.Timeout?.TotalSeconds ?? 0}s\nTests passed.";
        var testArtifact = new Artifact("TestResults", "test-results.txt", InlineText: testResults);
        var artifactEvent = new ArtifactAvailable(
            Guid.NewGuid(),
            command.Correlation,
            testArtifact);
        await session.AddEvent(artifactEvent);

        // Emit CommandCompleted
        var completedEvent = new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Success);

        await session.AddEvent(completedEvent);
    }
}
