namespace JuniorDev.Agents;

/// <summary>
/// Configuration for agent behavior.
/// </summary>
public class AgentConfig
{
    /// <summary>
    /// Whether to run in dry-run mode (no actual operations performed).
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Seed for deterministic/random operations. Use null for truly random behavior.
    /// </summary>
    public int? RandomSeed { get; set; }

    /// <summary>
    /// Timeout for operations in seconds.
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retry attempts for failed operations.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay between retries in milliseconds.
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Whether to enable detailed logging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>
    /// Whether to collect and expose metrics.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// The profile/name of this agent.
    /// </summary>
    public string? AgentProfile { get; set; }

    /// <summary>
    /// Creates a deterministic config for testing.
    /// </summary>
    public static AgentConfig CreateDeterministic(int seed = 42)
    {
        return new AgentConfig
        {
            RandomSeed = seed,
            EnableDetailedLogging = true,
            EnableMetrics = false
        };
    }

    /// <summary>
    /// Creates a config from environment variables.
    /// </summary>
    public static AgentConfig FromEnvironment()
    {
        return new AgentConfig
        {
            DryRun = Environment.GetEnvironmentVariable("AGENT_DRY_RUN") == "true",
            RandomSeed = int.TryParse(Environment.GetEnvironmentVariable("AGENT_RANDOM_SEED"), out var seed) ? seed : null,
            OperationTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("AGENT_TIMEOUT_SECONDS"), out var timeout) ? timeout : 30,
            MaxRetryAttempts = int.TryParse(Environment.GetEnvironmentVariable("AGENT_MAX_RETRIES"), out var retries) ? retries : 3,
            RetryBaseDelayMs = int.TryParse(Environment.GetEnvironmentVariable("AGENT_RETRY_BASE_DELAY_MS"), out var delay) ? delay : 1000,
            AgentProfile = Environment.GetEnvironmentVariable("AGENT_PROFILE"),
            EnableDetailedLogging = Environment.GetEnvironmentVariable("AGENT_DETAILED_LOGGING") == "true",
            EnableMetrics = Environment.GetEnvironmentVariable("AGENT_DISABLE_METRICS") != "true"
        };
    }
}