# Real-Repo Enablement Guide

## Overview

This guide outlines the staged rollout process for enabling Junior Dev to interact with real GitHub repositories, Jira work items, and Git operations. The platform is designed with multiple safety layers to prevent accidental real-world actions during development and testing.

## Safety Architecture

### Core Safety Mechanisms

1. **Dry-Run Mode (Default)**
   - `LivePolicyConfig.DryRun = true` by default
   - All adapters simulate operations without external calls
   - No real API interactions or VCS changes

2. **Environment-Based Activation**
   - Live mode requires explicit `RUN_LIVE=1` environment variable
   - Credentials must be configured in `appsettings.json` or environment variables
   - Circuit breakers prevent cascading failures

3. **Command-Level Guards**
   - VCS push operations require `AllowPush = true` (disabled by default)
   - Live push requires `RUN_LIVE_PUSH=1` override
   - Work item operations respect dry-run flags

## Staged Rollout Process

### Stage 1: Development Environment (Recommended Starting Point)

**Environment Setup:**
```bash
# No special environment variables needed
# Platform runs in dry-run mode by default
```

**Validation Steps:**
1. Run unit tests: `dotnet test`
2. Run E2E fake mode: `dotnet test --filter "FakeModeSmokeTest"`
3. Verify no external calls in logs
4. Confirm all operations complete successfully

**Expected Behavior:**
- All commands execute with simulated responses
- No network traffic to external services
- Temporary workspaces created and cleaned up
- Full event logging for debugging

### Stage 2: Test Environment with Mock Credentials

**Environment Setup:**
```bash
export RUN_LIVE=1
# Configure invalid/test credentials in appsettings.Development.json
{
  "Auth": {
    "GitHub": {
      "Token": "invalid-test-token",
      "DefaultOrg": "test-org",
      "DefaultRepo": "test-repo"
    },
    "Git": {
      "PersonalAccessToken": "invalid-test-token"
    }
  }
}
```

**Validation Steps:**
1. Run integration tests: `dotnet test --filter "*Integration*"`
2. Verify API calls fail gracefully with authentication errors
3. Confirm circuit breakers activate on failures
4. Check timeout handling (30s for all HTTP calls)

**Expected Behavior:**
- Real API calls attempted but fail with auth errors
- Proper error handling and event emission
- No real data modification
- Circuit breaker prevents retry storms

### Stage 3: Staging Environment with Limited Permissions

**Environment Setup:**
```bash
export RUN_LIVE=1
# Configure read-only credentials
{
  "Auth": {
    "GitHub": {
      "Token": "read-only-token",  # Token with read-only permissions
      "DefaultOrg": "staging-org",
      "DefaultRepo": "staging-repo"
    }
  }
}
```

**Validation Steps:**
1. Run live smoke test: `dotnet test --filter "LiveModeSmokeTest"`
2. Verify read operations work (QueryWorkItem, etc.)
3. Confirm write operations fail with permission errors
4. Test circuit breaker recovery

**Expected Behavior:**
- Read operations succeed with real data
- Write operations fail safely with permission errors
- No accidental modifications
- Proper error boundaries

### Stage 4: Production Environment with Full Permissions

**Environment Setup:**
```bash
export RUN_LIVE=1
export RUN_LIVE_PUSH=1  # Only if push operations needed
# Configure production credentials with appropriate permissions
{
  "Auth": {
    "GitHub": {
      "Token": "production-token",
      "DefaultOrg": "production-org",
      "DefaultRepo": "production-repo"
    },
    "Git": {
      "PersonalAccessToken": "production-git-token"
    }
  },
  "LivePolicy": {
    "DryRun": false  # Explicitly enable live operations
  }
}
```

**Validation Steps:**
1. Start with dry-run validation
2. Gradually enable live operations
3. Monitor circuit breaker metrics
4. Test failure scenarios with invalid data

**Expected Behavior:**
- Full real-world integration
- Proper error handling and recovery
- Circuit breaker protection
- Audit logging of all operations

## Configuration Reference

### Environment Variables

| Variable | Purpose | Default | Safety |
|----------|---------|---------|--------|
| `RUN_LIVE` | Enable live adapter mode | `0` (disabled) | Prevents accidental live operations |
| `RUN_LIVE_PUSH` | Allow VCS push operations | `0` (disabled) | Extra guard for destructive operations |
| `RUN_AI_TESTS` | Enable AI integration tests | `0` (disabled) | Prevents AI API costs in CI |

### AppSettings Configuration

```json
{
  "Auth": {
    "GitHub": {
      "Token": "your-github-token",
      "DefaultOrg": "your-org",
      "DefaultRepo": "your-repo"
    },
    "Jira": {
      "BaseUrl": "https://your-org.atlassian.net",
      "Username": "your-email",
      "ApiToken": "your-jira-token",
      "ProjectKey": "PROJ"
    },
    "Git": {
      "PersonalAccessToken": "your-git-token"
    }
  },
  "LivePolicy": {
    "DryRun": false
  }
}
```

## Monitoring and Observability

### Key Metrics to Monitor

- Circuit breaker activation count
- API call success/failure rates
- Command execution times
- Artifact generation counts

### Logging Levels

- **Development:** Debug logging for all operations
- **Staging:** Info level with error details
- **Production:** Warn level with structured logs

## Rollback Procedures

### Immediate Rollback
```bash
unset RUN_LIVE
unset RUN_LIVE_PUSH
# Restart application - reverts to dry-run mode
```

### Gradual Rollback
1. Set `LivePolicy.DryRun = true` in config
2. Monitor for any in-flight operations
3. Restart with dry-run confirmation

## Best Practices

1. **Always start with dry-run validation** before enabling live operations
2. **Use read-only credentials** in staging environments
3. **Monitor circuit breaker metrics** for early failure detection
4. **Test failure scenarios** regularly
5. **Keep detailed audit logs** of all operations
6. **Have rollback procedures** documented and tested

## Troubleshooting

### Common Issues

**"Specified method is not supported"**
- Real adapters don't support all commands (e.g., QueryBacklog)
- Solution: Skip unsupported commands in live mode

**Circuit breaker open**
- Too many API failures
- Solution: Check credentials and network connectivity

**Timeout errors**
- External service delays
- Solution: Increase timeout or implement retry logic

**Permission errors**
- Insufficient API permissions
- Solution: Review token scopes and repository access

This guide ensures safe, gradual enablement of real-world integrations while maintaining robust safety mechanisms throughout the rollout process.