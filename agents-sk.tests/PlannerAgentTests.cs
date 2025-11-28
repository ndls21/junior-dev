using System;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Agents.Sk;
using JuniorDev.Contracts;
using Microsoft.SemanticKernel;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Sk.Tests
{
    public class PlannerAgentTests
    {
        [Fact]
        public async Task GeneratePlanAsync_WithWorkItem_ReturnsSingleNodePlan()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            var workItem = new WorkItemRef("PROJ-123");
            var policy = new PolicyProfile
            {
                Name = "test",
                ProtectedBranches = new HashSet<string> { "main", "develop" }
            };

            var plan = await agent.GeneratePlanAsync(workItem, policy);

            Assert.Single(plan.Nodes);
            var node = plan.Nodes.Single();
            Assert.Equal(workItem, node.WorkItem);
            Assert.Equal("executor", node.AgentHint);
            Assert.Contains("task", node.Tags);
            Assert.Empty(node.DependsOn);
        }

        [Fact]
        public async Task GenerateBranchSuggestion_AvoidsProtectedBranches()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            var workItemId = "PROJ-123";
            var protectedBranches = new[] { "main", "develop", "feature/proj-123" };

            var suggestion = agent.GenerateBranchSuggestion(workItemId, protectedBranches);

            Assert.Equal("feature/proj-123-1", suggestion);
            Assert.DoesNotContain(suggestion, protectedBranches);
        }

        [Fact]
        public async Task GenerateBranchSuggestion_UsesBaseNameIfNotProtected()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            var workItemId = "PROJ-456";
            var protectedBranches = new[] { "main", "develop" };

            var suggestion = agent.GenerateBranchSuggestion(workItemId, protectedBranches);

            Assert.Equal("feature/proj-456", suggestion);
        }
    }
}
