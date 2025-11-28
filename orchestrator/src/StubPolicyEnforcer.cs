using System.Linq;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class StubPolicyEnforcer : IPolicyEnforcer
{
    public PolicyResult CheckPolicy(ICommand command, SessionConfig sessionConfig)
    {
        var policy = sessionConfig.Policy;

        // Check command whitelist/blacklist
        if (policy.CommandWhitelist != null && !policy.CommandWhitelist.Contains(command.Kind))
        {
            return new PolicyResult(false, "Command not in whitelist");
        }

        if (policy.CommandBlacklist != null && policy.CommandBlacklist.Contains(command.Kind))
        {
            return new PolicyResult(false, "Command in blacklist");
        }

        // Stub: protected branches - deny if branch is protected
        if (command is CreateBranch cb && policy.ProtectedBranches.Contains(cb.BranchName))
        {
            return new PolicyResult(false, "Protected branch");
        }

        // Stub: max files per commit - deny if too many files
        if (command is Commit commit && policy.MaxFilesPerCommit.HasValue)
        {
            if (commit.IncludePaths.Count > policy.MaxFilesPerCommit.Value)
            {
                return new PolicyResult(false, "Too many files in commit");
            }
        }

        return new PolicyResult(true);
    }
}
