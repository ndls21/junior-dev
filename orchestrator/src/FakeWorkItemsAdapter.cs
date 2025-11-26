using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class FakeWorkItemsAdapter : FakeAdapter
{
    public override bool CanHandle(ICommand command)
    {
        return command is TransitionTicket or Comment or SetAssignee;
    }
}