using Xunit;
using JuniorDev.Agents;

namespace JuniorDev.Agents.Tests;

public class AgentConfigTests
{
    [Fact]
    public void CreateDeterministic_SetsExpectedValues()
    {
        var config = AgentConfig.CreateDeterministic(123);

        Assert.True(config.RandomSeed.HasValue);
        Assert.Equal(123, config.RandomSeed.Value);
        Assert.True(config.EnableDetailedLogging);
        Assert.False(config.EnableMetrics);
    }

    [Fact]
    public void DefaultConfig_HasReasonableDefaults()
    {
        var config = new AgentConfig();

        Assert.False(config.DryRun);
        Assert.Null(config.RandomSeed);
        Assert.Equal(30, config.OperationTimeoutSeconds);
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryBaseDelayMs);
        Assert.False(config.EnableDetailedLogging);
        Assert.True(config.EnableMetrics);
    }
}
