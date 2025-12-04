using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class FakeWorkItemsAdapter : FakeAdapter
{
    public override bool CanHandle(ICommand command)
    {
        return command is TransitionTicket or Comment or SetAssignee or QueryBacklog or QueryWorkItem;
    }

    public override async Task HandleCommand(ICommand command, SessionState session)
    {
        if (command is QueryBacklog queryBacklog)
        {
            await HandleQueryBacklog(queryBacklog, session);
        }
        else if (command is QueryWorkItem queryWorkItem)
        {
            await HandleQueryWorkItem(queryWorkItem, session);
        }
        else
        {
            // For other commands, use base implementation
            await base.HandleCommand(command, session);
        }
    }

    private async Task HandleQueryBacklog(QueryBacklog command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Fake implementation: return some sample work items
        var items = new List<WorkItemSummary>
        {
            new WorkItemSummary("PROJ-123", "Implement user authentication", "Open", "developer1"),
            new WorkItemSummary("PROJ-124", "Add database migration", "In Progress", "developer2"),
            new WorkItemSummary("PROJ-125", "Fix UI bug in dashboard", "Open", null)
        };

        // Apply filter if provided (simple string contains)
        if (!string.IsNullOrEmpty(command.Filter))
        {
            items = items.Where(i => i.Title.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
                                   i.Id.Contains(command.Filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var queriedEvent = new BacklogQueried(
            Guid.NewGuid(),
            command.Correlation,
            items);

        await session.AddEvent(queriedEvent);

        // Emit CommandCompleted
        var completedEvent = new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Success);

        await session.AddEvent(completedEvent);
    }

    private async Task HandleQueryWorkItem(QueryWorkItem command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Fake implementation: return details for the requested item
        var details = command.Item.Id switch
        {
            "PROJ-123" => new WorkItemDetails(
                "PROJ-123",
                "Implement user authentication",
                "Add JWT-based authentication system with login/logout endpoints. This depends on #456 for the user model.",
                "Open",
                "developer1",
                new[] { "backend", "security" },
                new[] 
                {
                    new WorkItemComment("developer1", "This should include password reset functionality", DateTimeOffset.UtcNow.AddHours(-2)),
                    new WorkItemComment("reviewer1", "Blocked by PROJ-456 user model implementation", DateTimeOffset.UtcNow.AddHours(-1))
                },
                new[]
                {
                    new WorkItemLink("github_issue", "#456", "User model implementation", "depends_on"),
                    new WorkItemLink("github_issue", "#789", "Security review", "blocks")
                }),
            "PROJ-124" => new WorkItemDetails(
                "PROJ-124",
                "Add database migration",
                "Create migration scripts for the new user table schema. Part of the authentication epic.",
                "In Progress",
                "developer2",
                new[] { "database", "migration" },
                new[] 
                {
                    new WorkItemComment("developer2", "Migration should handle existing data", DateTimeOffset.UtcNow.AddHours(-4))
                },
                new[]
                {
                    new WorkItemLink("github_issue", "#123", "Authentication system", "parent_of")
                }),
            "PROJ-125" => new WorkItemDetails(
                "PROJ-125",
                "Fix UI bug in dashboard",
                "The dashboard chart is not displaying data correctly on mobile devices",
                "Open",
                null,
                new[] { "frontend", "bug" },
                Array.Empty<WorkItemComment>(),
                Array.Empty<WorkItemLink>()),
            _ => new WorkItemDetails(
                command.Item.Id,
                $"Unknown item {command.Item.Id}",
                "This is a placeholder for unknown work items",
                "Unknown",
                null,
                Array.Empty<string>(),
                Array.Empty<WorkItemComment>(),
                Array.Empty<WorkItemLink>())
        };

        var queriedEvent = new WorkItemQueried(
            Guid.NewGuid(),
            command.Correlation,
            details);

        await session.AddEvent(queriedEvent);

        // Emit CommandCompleted
        var completedEvent = new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Success);

        await session.AddEvent(completedEvent);
    }
}
