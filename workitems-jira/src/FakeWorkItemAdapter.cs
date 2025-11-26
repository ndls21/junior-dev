using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.Jira;

public class FakeWorkItemAdapter : IAdapter
{
    private readonly ConcurrentDictionary<string, WorkItemData> _workItems = new();

    public bool CanHandle(ICommand command)
    {
        return command is TransitionTicket or Comment or SetAssignee;
    }

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        try
        {
            switch (command)
            {
                case Comment comment:
                    await HandleComment(comment, session);
                    break;
                case TransitionTicket transition:
                    await HandleTransition(transition, session);
                    break;
                case SetAssignee assign:
                    await HandleSetAssignee(assign, session);
                    break;
                default:
                    throw new NotSupportedException($"Command type {command.GetType()} not supported");
            }

            // Emit CommandCompleted on success
            var completedEvent = new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Success);

            await session.AddEvent(completedEvent);
        }
        catch (Exception ex)
        {
            // Emit CommandRejected for failures
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                ex.Message,
                "VALIDATION_ERROR");

            await session.AddEvent(rejectedEvent);
        }
    }

    private async Task HandleComment(Comment comment, SessionState session)
    {
        var workItem = GetOrCreateWorkItem(comment.Item.Id);
        workItem.Comments.Add(new WorkItemComment
        {
            Id = Guid.NewGuid().ToString(),
            Body = comment.Body,
            Author = "test-user",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Create artifact with comment details
        var artifact = new Artifact(
            "workitem-comment",
            $"Comment on {comment.Item.Id}",
            $"Comment added to {comment.Item.Id}: {comment.Body}",
            null,
            null,
            "text/plain");

        await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), comment.Correlation, artifact));
    }

    private async Task HandleTransition(TransitionTicket transition, SessionState session)
    {
        var workItem = GetOrCreateWorkItem(transition.Item.Id);

        // Validate transition (simple validation for demo)
        if (!IsValidTransition(workItem.Status, transition.State))
        {
            throw new InvalidOperationException($"Invalid transition from '{workItem.Status}' to '{transition.State}'");
        }

        workItem.Status = transition.State;
        workItem.LastTransitionAt = DateTimeOffset.UtcNow;

        // Create artifact with transition details
        var artifact = new Artifact(
            "workitem-transition",
            $"Transition {transition.Item.Id}",
            $"Work item {transition.Item.Id} transitioned to {transition.State}",
            null,
            null,
            "text/plain");

        await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), transition.Correlation, artifact));
    }

    private async Task HandleSetAssignee(SetAssignee assign, SessionState session)
    {
        var workItem = GetOrCreateWorkItem(assign.Item.Id);
        workItem.Assignee = assign.Assignee;
        workItem.LastAssignedAt = DateTimeOffset.UtcNow;

        // Create artifact with assignment details
        var artifact = new Artifact(
            "workitem-assignment",
            $"Assignment {assign.Item.Id}",
            $"Work item {assign.Item.Id} assigned to {assign.Assignee}",
            null,
            null,
            "text/plain");

        await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), assign.Correlation, artifact));
    }

    private WorkItemData GetOrCreateWorkItem(string id)
    {
        return _workItems.GetOrAdd(id, _ => new WorkItemData
        {
            Id = id,
            Status = "Open",
            Assignee = null,
            Comments = new List<WorkItemComment>(),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private bool IsValidTransition(string? fromStatus, string toStatus)
    {
        // Simple validation - in real Jira this would be much more complex
        var validTransitions = new Dictionary<string, string[]>
        {
            ["Open"] = new[] { "In Progress", "Closed" },
            ["In Progress"] = new[] { "Review", "Closed" },
            ["Review"] = new[] { "In Progress", "Closed" },
            ["Closed"] = new[] { "Open" }
        };

        if (fromStatus == null) return true; // New items can be set to any status
        return validTransitions.TryGetValue(fromStatus, out var allowed) && allowed.Contains(toStatus);
    }

    private class WorkItemData
    {
        public required string Id { get; set; }
        public required string Status { get; set; }
        public string? Assignee { get; set; }
        public required List<WorkItemComment> Comments { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastTransitionAt { get; set; }
        public DateTimeOffset? LastAssignedAt { get; set; }
    }

    private class WorkItemComment
    {
        public required string Id { get; set; }
        public required string Body { get; set; }
        public required string Author { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
    }
}
