using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public async Task GeneratePlanAsync_WithWorkItem_ReturnsPlan()
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

            // Should return at least one node
            Assert.NotEmpty(plan.Nodes);
            
            // Check that the first node has proper structure
            var firstNode = plan.Nodes.First();
            Assert.Equal(workItem, firstNode.WorkItem);
            Assert.Equal("executor", firstNode.AgentHint);
            Assert.Contains("task", firstNode.Tags);
            Assert.Empty(firstNode.DependsOn);
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

        [Fact]
        public async Task AnalyzeWorkItemRequirements_ExtractsAcceptanceCriteria()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            // Access the private PlanningPlugin class
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("AnalyzeWorkItemRequirementsAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement user authentication", 
                "As a user, I should be able to login with email/password. Given valid credentials, when I submit the form, then I should be logged in. Must support OAuth providers.", 
                new[] { "Also need to handle password reset", "Security requirements: encrypt passwords" } 
            });

            Assert.Contains("login with email/password", result);
            Assert.Contains("Given valid credentials", result);
            Assert.Contains("password reset", result);
            Assert.Contains("Security requirements", result);
        }

        [Fact]
        public async Task AssessWorkItemComplexity_CalculatesScoreAndRisks()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("AssessWorkItemComplexityAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement database migration", 
                "Repository Structure Analysis:\nProject files (.csproj): 5\nTest directories: agents.tests/, orchestrator.Tests/"
            });

            Assert.Contains("Complexity Score", result);
            Assert.Contains("Identified Risks", result);
            Assert.Contains("Database changes", result);
            Assert.Contains("Estimated Effort", result);
        }

        [Fact]
        public async Task GenerateTaskDag_CreatesValidDependencyGraph()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("GenerateTaskDagAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Requirements: implement feature with testing and review",
                "Repository Structure Analysis:\nTest directories: agents.tests/",
                new[] { "executor", "reviewer", "tester" },
                "Dependency Analysis: No significant dependencies identified"
            });

            Assert.Contains("Task Dependency Graph", result);
            Assert.Contains("Implement core functionality", result);
            Assert.Contains("Write and run tests", result);
            Assert.Contains("Code review and validation", result);
            
            // Verify dependencies are properly ordered
            var implementationIndex = result.IndexOf("Implement core functionality");
            var testingIndex = result.IndexOf("Write and run tests");
            var reviewIndex = result.IndexOf("Code review and validation");
            
            Assert.True(implementationIndex < testingIndex, "Implementation should come before testing");
            Assert.True(testingIndex < reviewIndex, "Testing should come before review");
        }

        [Fact]
        public async Task GenerateTaskDag_NoCyclesInDependencyGraph()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("GenerateTaskDagAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Complex multi-step feature",
                "Repository Structure Analysis:\nTest directories: tests/",
                new[] { "executor", "reviewer" },
                "Dependency Analysis: No blockers identified"
            });

            // Parse the DAG to verify no cycles
            var lines = result.Split('\n');
            var tasks = new List<(string Title, string[] Dependencies)>();
            
            string currentTask = "";
            foreach (var line in lines)
            {
                if (line.StartsWith("Task: "))
                {
                    currentTask = line.Replace("Task: ", "").Trim();
                }
                else if (line.Contains("Dependencies:") && !string.IsNullOrEmpty(currentTask))
                {
                    var depsText = line.Replace("Dependencies:", "").Trim();
                    var deps = depsText == "None" ? Array.Empty<string>() : depsText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    tasks.Add((currentTask, deps));
                    currentTask = "";
                }
            }

            // Verify no task depends on itself and dependencies exist
            foreach (var (title, deps) in tasks)
            {
                Assert.DoesNotContain(title, deps);
                foreach (var dep in deps)
                {
                    Assert.True(tasks.Any(t => t.Title == dep), $"Dependency '{dep}' for task '{title}' should exist");
                }
            }
        }

        [Fact]
        public async Task IdentifyRisks_ProvidesMitigationStrategies()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("IdentifyRisksAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement payment processing",
                "Add payment gateway integration with security requirements. Handle PCI compliance.",
                new[] { "Previous payment integration failures", "Security vulnerabilities in payment systems" }
            });

            Assert.Contains("Risk Analysis", result);
            Assert.Contains("Security-related changes", result);
            Assert.Contains("Mitigation", result);
        }

        [Fact]
        public async Task ValidatePlan_ChecksCompletenessAndFeasibility()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("ValidatePlanAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Task: Implement feature\nAgent: executor\nTask: Test feature\nAgent: executor\nTask: Review feature\nAgent: reviewer",
                "Requirements: implement feature with testing and review",
                "Repository Structure Analysis:\nTest directories: tests/"
            });

            Assert.Contains("Plan Validation Results", result);
            Assert.Contains("Completeness Check", result);
            Assert.Contains("Agent Availability", result);
            Assert.Contains("✓", result); // Should show successful validations
        }

        [Fact]
        public async Task ParseWorkItem_ExtractsRequirementsAndConstraints()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("ParseWorkItemAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement user authentication feature. As a user, I should be able to login with email/password. Must support OAuth providers. Cannot use plain text passwords. Should handle password reset functionality. Deadline: end of sprint."
            });

            Assert.Contains("Requirements:", result);
            Assert.Contains("Acceptance Criteria:", result);
            Assert.Contains("Constraints:", result);
            Assert.Contains("login with email/password", result);
            Assert.Contains("OAuth providers", result);
            Assert.Contains("plain text passwords", result);
            Assert.Contains("password reset", result);
            Assert.Contains("Deadline:", result);
        }

        [Fact]
        public async Task SuggestAgentRoles_MatchesRequirementsToCapabilities()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("SuggestAgentRolesAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement secure API with database integration and comprehensive testing",
                new[] { "executor", "reviewer", "tester", "security-specialist", "frontend-developer" }
            });

            Assert.Contains("Agent Role Recommendations", result);
            Assert.Contains("Primary Agents", result);
            Assert.Contains("Secondary Agents", result);
            Assert.Contains("executor", result);
            Assert.Contains("tester", result);
            Assert.Contains("reviewer", result);
        }

        [Fact]
        public async Task EstimateEffort_CalculatesRealisticEstimates()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("EstimateEffortAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Complexity Score: 7/10\nIdentified Risks:\n  - Database changes - potential data loss\n  - API changes - breaking changes for consumers\nEstimated Effort: 1-2 days (complex feature)",
                "Task: Implement API\n  Agent: executor\n  Dependencies: None\nTask: Write tests\n  Agent: executor\n  Dependencies: Implement API\nTask: Code review\n  Agent: reviewer\n  Dependencies: Write tests",
                new[] { "Previous API work took 2 days", "Database changes often run over by 20%" }
            });

            Assert.Contains("Effort Estimation", result);
            Assert.Contains("Complexity Score", result);
            Assert.Contains("Number of Tasks", result);
            Assert.Contains("Base Effort Estimate", result);
            Assert.Contains("Confidence Level", result);
            Assert.Contains("Time Breakdown", result);
        }

        [Fact]
        public async Task AnalyzeWorkItemDependencies_ProcessesCommentsAndLinks()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("AnalyzeWorkItemDependenciesAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement user dashboard",
                "Create dashboard showing user stats and recent activity",
                new[] {
                    new WorkItemComment("alice", "This depends on the user authentication feature (#123)", DateTimeOffset.Now),
                    new WorkItemComment("bob", "Blocked by API changes in PROJ-456", DateTimeOffset.Now)
                },
                new[] {
                    new WorkItemLink("blocks", "PROJ-789", "User profile feature", "blocked-by"),
                    new WorkItemLink("relates", "API-001", "API authentication", "depends-on")
                }
            });

            Assert.Contains("Dependency Analysis", result);
            Assert.Contains("Explicit Links Found", result);
            Assert.Contains("Dependencies Mentioned in Comments", result);
            Assert.Contains("#123", result);
            Assert.Contains("PROJ-456", result);
            Assert.Contains("PROJ-789", result);
            Assert.Contains("API-001", result);
        }

        [Fact]
        public async Task GenerateTaskDag_IncorporatesDependencyAnalysis()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("GenerateTaskDagAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement dashboard with authentication dependency",
                "Repository Structure Analysis:\nTest directories: tests/\nProject files: 8",
                new[] { "executor", "reviewer", "tester" },
                "Dependency Analysis for: Implement user dashboard\nDependencies Mentioned in Comments:\n  - depends on user authentication (#123)\nPotential Blockers:\n  - Blocked by: API-001"
            });

            Assert.Contains("Dependency Analysis Context", result);
            Assert.Contains("Verify external dependencies", result);
            Assert.Contains("Implement core functionality", result);
            Assert.Contains("Validate prerequisite work items", result);
        }

        [Fact]
        public async Task ParseWorkItem_IdentifiesComplexityIndicators()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("ParseWorkItemAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement real-time chat feature with WebSocket API, database integration, security encryption, performance optimization, and comprehensive testing across multiple platforms."
            });

            Assert.Contains("Complexity Indicators", result);
            Assert.Contains("API/Integration complexity", result);
            Assert.Contains("Database changes required", result);
            Assert.Contains("Security considerations", result);
            Assert.Contains("Performance requirements", result);
            Assert.Contains("Testing complexity", result);
        }

        [Fact]
        public async Task SuggestAgentRoles_IncludesExecutionOrder()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("SuggestAgentRolesAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Implement secure payment processing with PCI compliance, database integration, and automated testing",
                new[] { "executor", "reviewer", "tester", "security-specialist", "database-admin" }
            });

            Assert.Contains("Suggested Agent Execution Order", result);
            Assert.Contains("1.", result);
            Assert.Contains("security-specialist", result);
            Assert.Contains("database-admin", result);
        }

        [Fact]
        public async Task EstimateEffort_IncludesRiskFactors()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);
            
            var planningPlugin = typeof(PlannerAgent).GetNestedType("PlanningPlugin", System.Reflection.BindingFlags.NonPublic);
            var pluginInstance = Activator.CreateInstance(planningPlugin);
            
            var method = planningPlugin.GetMethod("EstimateEffortAsync");
            var result = await (Task<string>)method.Invoke(pluginInstance, new object[] { 
                "Complexity Score: 8/10\nIdentified Risks:\n  - Security vulnerabilities\n  - Database schema changes\n  - API contract changes",
                "Task: Implement payment processing\n  Agent: executor\nTask: Security review\n  Agent: security-specialist\nTask: Database migration\n  Agent: database-admin",
                Array.Empty<string>()
            });

            Assert.Contains("Risk Factors", result);
            Assert.Contains("Security review may add", result);
            Assert.Contains("Database changes may require", result);
        }

        [Fact]
        public async Task ValidateDagProperties_DetectsCycles()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var validateMethod = typeof(PlannerAgent).GetMethod("ValidateDagProperties", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a DAG with a cycle: A -> B -> C -> A
            var dagText = @"Task: Task A
Agent: executor
Dependencies: Task C
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task A
Tags: implementation

Task: Task C
Agent: executor
Dependencies: Task B
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });

            // This should log warnings about cycles but not throw
            validateMethod.Invoke(agent, new object[] { tasks });
        }

        [Fact]
        public async Task ValidateDagProperties_ValidatesTopologicalOrder()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var validateMethod = typeof(PlannerAgent).GetMethod("ValidateDagProperties", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a valid DAG: A -> B -> C
            var dagText = @"Task: Task A
Agent: executor
Dependencies: None
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task A
Tags: implementation

Task: Task C
Agent: executor
Dependencies: Task B
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });

            // This should not log warnings about cycles or ordering
            validateMethod.Invoke(agent, new object[] { tasks });
        }

        [Fact]
        public async Task ValidateDagProperties_DetectsMissingDependencies()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var validateMethod = typeof(PlannerAgent).GetMethod("ValidateDagProperties", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a DAG with a missing dependency
            var dagText = @"Task: Task A
Agent: executor
Dependencies: None
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task X
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });

            // This should log warnings about missing dependencies
            validateMethod.Invoke(agent, new object[] { tasks });
        }

        [Fact]
        public async Task HasCycles_ReturnsTrueForCyclicDependencies()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var hasCyclesMethod = typeof(PlannerAgent).GetMethod("HasCycles", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a DAG with a cycle
            var dagText = @"Task: Task A
Agent: executor
Dependencies: Task C
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task A
Tags: implementation

Task: Task C
Agent: executor
Dependencies: Task B
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });
            var hasCycles = (bool)hasCyclesMethod.Invoke(agent, new object[] { tasks });

            Assert.True(hasCycles);
        }

        [Fact]
        public async Task HasCycles_ReturnsFalseForAcyclicDependencies()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var hasCyclesMethod = typeof(PlannerAgent).GetMethod("HasCycles", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a valid acyclic DAG
            var dagText = @"Task: Task A
Agent: executor
Dependencies: None
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task A
Tags: implementation

Task: Task C
Agent: executor
Dependencies: Task B
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });
            var hasCycles = (bool)hasCyclesMethod.Invoke(agent, new object[] { tasks });

            Assert.False(hasCycles);
        }

        [Fact]
        public async Task HasValidTopologicalOrder_ReturnsTrueForValidOrder()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var hasValidOrderMethod = typeof(PlannerAgent).GetMethod("HasValidTopologicalOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a valid DAG
            var dagText = @"Task: Task A
Agent: executor
Dependencies: None
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task A
Tags: implementation

Task: Task C
Agent: executor
Dependencies: Task B
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });
            var hasValidOrder = (bool)hasValidOrderMethod.Invoke(agent, new object[] { tasks });

            Assert.True(hasValidOrder);
        }

        [Fact]
        public async Task HasValidTopologicalOrder_ReturnsFalseForInvalidOrder()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private methods using reflection
            var hasValidOrderMethod = typeof(PlannerAgent).GetMethod("HasValidTopologicalOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            // Create a DAG with invalid ordering (C depends on B, B depends on A, but A depends on C)
            var dagText = @"Task: Task A
Agent: executor
Dependencies: Task C
Tags: implementation

Task: Task B
Agent: executor
Dependencies: Task A
Tags: implementation

Task: Task C
Agent: executor
Dependencies: Task B
Tags: implementation";

            var tasks = parseMethod.Invoke(agent, new object[] { dagText });
            var hasValidOrder = (bool)hasValidOrderMethod.Invoke(agent, new object[] { tasks });

            Assert.False(hasValidOrder);
        }

        [Fact]
        public async Task ParseTasksFromDag_ExtractsTaskInformation()
        {
            var kernel = new Kernel();
            var agent = new PlannerAgent(kernel);

            // Access private method using reflection
            var parseMethod = typeof(PlannerAgent).GetMethod("ParseTasksFromDag", BindingFlags.NonPublic | BindingFlags.Instance);

            var dagText = @"Task: Implement authentication
Agent: executor
Dependencies: None
Tags: security, implementation

Task: Write tests
Agent: tester
Dependencies: Implement authentication
Tags: testing, quality";

            var tasks = (System.Collections.IList)parseMethod.Invoke(agent, new object[] { dagText });

            Assert.Equal(2, tasks.Count);

            // Check first task
            var task1 = tasks[0];
            var title1 = (string)task1.GetType().GetProperty("Title").GetValue(task1);
            var agentHint1 = (string)task1.GetType().GetProperty("AgentHint").GetValue(task1);
            var dependencies1 = (string[])task1.GetType().GetProperty("DependsOn").GetValue(task1);
            var tags1 = (string[])task1.GetType().GetProperty("Tags").GetValue(task1);

            Assert.Equal("Implement authentication", title1);
            Assert.Equal("executor", agentHint1);
            Assert.Empty(dependencies1);
            Assert.Equal(new[] { "security", "implementation" }, tags1);

            // Check second task
            var task2 = tasks[1];
            var title2 = (string)task2.GetType().GetProperty("Title").GetValue(task2);
            var agentHint2 = (string)task2.GetType().GetProperty("AgentHint").GetValue(task2);
            var dependencies2 = (string[])task2.GetType().GetProperty("DependsOn").GetValue(task2);
            var tags2 = (string[])task2.GetType().GetProperty("Tags").GetValue(task2);

            Assert.Equal("Write tests", title2);
            Assert.Equal("tester", agentHint2);
            Assert.Equal(new[] { "Implement authentication" }, dependencies2);
            Assert.Equal(new[] { "testing", "quality" }, tags2);
        }
    }
}
