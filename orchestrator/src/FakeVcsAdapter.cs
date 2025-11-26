using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class FakeVcsAdapter : FakeAdapter
{
    public override bool CanHandle(ICommand command)
    {
        return command is CreateBranch or ApplyPatch or RunTests or Commit or Push or GetDiff;
    }
}