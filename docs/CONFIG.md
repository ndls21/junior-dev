# Configuration Guide

This document describes the configuration system for Junior Dev, including how to set up authentication, adapters, and other settings.

## Configuration Architecture

Junior Dev uses a layered configuration approach with Microsoft.Extensions.Configuration:

1. **appsettings.json** - Base configuration (checked into repo, no secrets)
2. **appsettings.{Environment}.json** - Environment-specific overrides (checked into repo, no secrets)
3. **Environment Variables** - Secrets and environment-specific values
4. **User Secrets** - Development secrets (not checked into repo)

## Configuration Structure

```json
{
  "AppConfig": {
    "Auth": { ... },
    "Adapters": { ... },
    "SemanticKernel": { ... },
    "Ui": { ... },
    "Workspace": { ... },
    "Policy": { ... }
  }
}
```

## Authentication Configuration

### Jira Authentication

Set these environment variables or configure in `appsettings.json`:

```bash
# Required for live Jira integration
JUNIORDEV__APPCONFIG__AUTH__JIRA__BASEURL=https://yourcompany.atlassian.net
JUNIORDEV__APPCONFIG__AUTH__JIRA__USERNAME=your.email@company.com
JUNIORDEV__APPCONFIG__AUTH__JIRA__APITOKEN=your_jira_api_token
JUNIORDEV__APPCONFIG__AUTH__JIRA__PROJECTKEY=PROJ
```

Or in `appsettings.json`:

```json
{
  "AppConfig": {
    "Auth": {
      "Jira": {
        "BaseUrl": "https://yourcompany.atlassian.net",
        "Username": "your.email@company.com",
        "ApiToken": "your_jira_api_token",
        "ProjectKey": "PROJ"
      }
    }
  }
}
```

**Note:** The project key is required for live operations and used as a fallback when work item IDs don't include it (e.g., "PROJ-123" vs "123").

### GitHub Authentication

```bash
# Required for live GitHub integration
JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN=your_github_personal_access_token
JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTORG=your-organization
JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTREPO=your-repo
```

Or in `appsettings.json`:

```json
{
  "AppConfig": {
    "Auth": {
      "GitHub": {
        "Token": "your_github_token",
        "DefaultOrg": "your-org",
        "DefaultRepo": "your-repo"
      }
    }
  }
}
```

**GitHub Token Requirements:**
- Must have `repo` scope for issue operations
- Must have `read:org` scope if working with organization repositories
- Personal Access Token (classic) or fine-grained token with appropriate permissions

**Note:** DefaultOrg and DefaultRepo are required for live operations and used as fallbacks when repository information is not specified in commands.

### Git Authentication

```bash
# SSH Key (recommended for production)
JUNIORDEV__APPCONFIG__AUTH__GIT__SSHKEYPATH=/path/to/.ssh/id_rsa

# Or Personal Access Token
JUNIORDEV__APPCONFIG__AUTH__GIT__PERSONALACCESSTOKEN=your_git_token

# Optional
JUNIORDEV__APPCONFIG__AUTH__GIT__USERNAME=your.name
JUNIORDEV__APPCONFIG__AUTH__GIT__USEREMAIL=your.email@company.com
JUNIORDEV__APPCONFIG__AUTH__GIT__DEFAULTREMOTE=origin
JUNIORDEV__APPCONFIG__AUTH__GIT__BRANCHPREFIX=feature/
```

### OpenAI Authentication

```bash
JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY=your_openai_api_key
JUNIORDEV__APPCONFIG__AUTH__OPENAI__ORGANIZATIONID=your_org_id  # Optional
```

### Azure OpenAI Authentication

```bash
JUNIORDEV__APPCONFIG__AUTH__AZUREOPENAI__ENDPOINT=https://your-resource.openai.azure.com/
JUNIORDEV__APPCONFIG__AUTH__AZUREOPENAI__APIKEY=your_azure_api_key
JUNIORDEV__APPCONFIG__AUTH__AZUREOPENAI__DEPLOYMENTNAME=your-deployment-name
```

## AI Tests Configuration

AI integration tests require valid AI service credentials and explicit opt-in. These tests are marked with `[Trait("Category", "AI")]` and are skipped by default.

### Running AI Tests

To enable AI integration tests:

```bash
# Option 1: Load from secrets file (recommended for development)
.\load-secrets.ps1

# Option 2: Set environment variables manually
export RUN_AI_TESTS=1
export JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY=your_openai_api_key

# Run AI tests specifically
dotnet test --filter "Category=AI"

# Or run all tests (AI tests will be skipped if not configured)
dotnet test
```

**Note:** AI tests are gated by the `RUN_AI_TESTS=1` environment variable to prevent accidental API usage and costs.

### CI/CD AI Tests

For CI pipelines, set secrets and enable AI tests conditionally:

```yaml
# .github/workflows/ci.yml
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - name: Run unit tests
        run: dotnet test --filter "Category!=AI"
      
      - name: Run AI integration tests
        if: github.event_name == 'push' && contains(github.ref, 'main')
        env:
          RUN_AI_TESTS: 1
          OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
        run: dotnet test --filter "Category=AI"
```

### AI Test Opt-in Policy

- AI tests are **disabled by default** to avoid accidental API usage
- Must set `RUN_AI_TESTS=1` environment variable to enable
- Must provide valid AI service credentials
- Tests marked with `[Trait("Category", "AI")]` and use test collection `"AI Integration Tests"`
- Dummy clients prevent crashes when AI is not configured for UI/agent testing

## Live Adapter Configuration

### Safe Defaults Policy

Junior Dev prioritizes safety by defaulting to mock/fake adapters that don't make real API calls. This prevents accidental data modification or API usage during development and testing.

**Default Behavior:**
- **Base configuration** (`appsettings.json`) ships with fake adapters to prevent accidental live operations
- Adapters default to `"fake"` when not specified or set to `"fake"`
- Live adapters (`"github"`, `"jira"`, `"git"`) require explicit configuration
- Push operations are disabled by default
- Dry-run mode is enabled by default for live operations

### Adapter Selection

Configure which adapters to use in `appsettings.json`:

```json
{
  "AppConfig": {
    "Adapters": {
      "WorkItemsAdapter": "fake",  // "fake" (default), "github", or "jira"
      "VcsAdapter": "fake",        // "fake" (default) or "git"
      "TerminalAdapter": "powershell"
    }
  }
}
```

**Adapter Options:**
- **WorkItemsAdapter**: `"fake"` (mock), `"github"` (real GitHub), `"jira"` (real Jira)
- **VcsAdapter**: `"fake"` (mock), `"git"` (real Git operations)
- **TerminalAdapter**: `"powershell"` (Windows), `"bash"` (Linux/macOS)

### Live Policy Configuration

Control live adapter behavior with the `LivePolicy` section:

```json
{
  "AppConfig": {
    "LivePolicy": {
      "PushEnabled": false,        // Default: false - require explicit opt-in for push operations
      "DryRun": true,             // Default: true - require explicit opt-in for live operations
      "RequireCredentialsValidation": true  // Whether to validate credentials before allowing live adapters
    }
  }
}
```

**LivePolicy Settings:**
- **PushEnabled**: Controls whether VCS push operations are allowed (default: `false`)
- **DryRun**: When `true`, adapters skip actual API calls and return success with dry-run artifacts (default: `true`)
- **RequireCredentialsValidation**: Whether to validate credentials at startup when using live adapters (default: `true`)

### Enabling Live Operations

To enable real API calls and push operations:

```json
{
  "AppConfig": {
    "Adapters": {
      "WorkItemsAdapter": "github",
      "VcsAdapter": "git"
    },
    "LivePolicy": {
      "PushEnabled": true,
      "DryRun": false
    }
  }
}
```

### Dry-Run Mode

When `LivePolicy.DryRun` is `true`, adapters will:
- Accept commands normally
- Skip actual API calls
- Return success events with dry-run indicators
- Create artifacts showing what would have been executed

This allows testing workflows without making real changes.

## Semantic Kernel Configuration

```json
{
  "AppConfig": {
    "SemanticKernel": {
      "Provider": "openai",  // or "azure-openai"
      "Model": "gpt-4",
      "MaxTokens": 4096,
      "Temperature": 0.7,
      "ProxyUrl": null,
      "Timeout": "00:05:00"
    }
  }
}
```

## UI Configuration

```json
{
  "AppConfig": {
    "Ui": {
      "LayoutPathOverride": null,    // Override layout file path
      "SettingsPathOverride": null,  // Override settings file path
      "Settings": {
        "Theme": "Light",            // "Light", "Dark", "Blue"
        "FontSize": 9,
        "ShowStatusChips": true,
        "AutoScrollEvents": true,
        "ShowTimestamps": true,
        "MaxEventHistory": 1000
      }
    }
  }
}
```

## Workspace Configuration

```json
{
  "AppConfig": {
    "Workspace": {
      "BasePath": "./workspaces",
      "BaselineMirrorPath": null,    // Path to baseline repo mirrors
      "AutoCreateDirectories": true,
      "KnownRepos": {
        "my-repo": {
          "Path": "./workspaces/my-repo",
          "RemoteUrl": "https://github.com/org/my-repo.git",
          "DefaultBranch": "main"
        }
      }
    }
  }
}
```

## Transcript Persistence Configuration

Configure chat transcript persistence in `appsettings.json`:

```json
{
  "AppConfig": {
    "Transcript": {
      "Enabled": true,
      "MaxMessagesPerTranscript": 1000,
      "MaxTranscriptSizeBytes": 10485760,
      "MaxTranscriptAge": "30.00:00:00",
      "TranscriptContextMessages": 10,
      "StorageDirectory": null
    }
  }
}
```

### Transcript Settings

- **Enabled**: Enable/disable transcript persistence (default: `true`)
- **MaxMessagesPerTranscript**: Maximum number of messages to keep per transcript (default: `1000`)
- **MaxTranscriptSizeBytes**: Maximum transcript file size in bytes (default: `10485760` = 10MB)
- **MaxTranscriptAge**: Maximum age of messages to keep (default: `"30.00:00:00"` = 30 days)
- **TranscriptContextMessages**: Number of recent messages to load into AI chat control for context (default: `10`)
- **StorageDirectory**: Custom directory for transcript files (default: `%APPDATA%\JuniorDev\Transcripts`)

### Transcript Storage

Transcripts are stored as JSON files in `%APPDATA%\JuniorDev\Transcripts\` by default:

- **File naming**: `{SessionId}.json`
- **Content**: Timestamped user/assistant message pairs
- **Pruning**: Automatic cleanup based on message count, size, and age limits
- **UI display**: Previous messages shown in read-only history panel above chat input

### Disabling Transcripts

To disable transcript persistence entirely:

```json
{
  "AppConfig": {
    "Transcript": {
      "Enabled": false
    }
  }
}
```

Or set environment variable:

```bash
JUNIORDEV__APPCONFIG__TRANSCRIPT__ENABLED=false
```

## Development Setup

1. Copy `appsettings.Development.json.example` to `appsettings.Development.json`
2. Modify settings for your development environment
3. Set up user secrets for authentication:

```bash
# Initialize user secrets for a project
dotnet user-secrets init

# Set secrets
dotnet user-secrets set "AppConfig:Auth:OpenAI:ApiKey" "your-key-here"
dotnet user-secrets set "AppConfig:Auth:GitHub:Token" "your-token-here"
```

### Local Secrets File (Alternative)

For easier development setup, you can create a `.env.local` file in the project root:

```bash
# Create secrets file
cp .env.local.example .env.local

# Edit with your secrets
# JUNIORDEV__Auth__OpenAI__ApiKey=your_openai_key_here
# RUN_AI_TESTS=1

# Load secrets into environment
.\load-secrets.ps1
```

The `.env.local` file is automatically ignored by `.gitignore` to prevent accidental commits.

## Quick Start

### Environment-Only Configuration

For containerized or CI/CD deployments, you can configure Junior Dev entirely through environment variables:

```bash
# Auth Configuration
export JUNIORDEV__APPCONFIG__AUTH__JIRA__BASEURL="https://yourcompany.atlassian.net"
export JUNIORDEV__APPCONFIG__AUTH__JIRA__USERNAME="your.email@company.com"
export JUNIORDEV__APPCONFIG__AUTH__JIRA__APITOKEN="your-jira-api-token"

export JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN="ghp_your-github-pat"
export JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTORG="your-org"
export JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTREPO="your-repo"

export JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY="sk-your-openai-key"

# Adapter Selection
export JUNIORDEV__APPCONFIG__ADAPTERS__WORKITEMSADAPTER="jira"
export JUNIORDEV__APPCONFIG__ADAPTERS__VCSADAPTER="git"
export JUNIORDEV__APPCONFIG__ADAPTERS__TERMINALADAPTER="powershell"

# Semantic Kernel Configuration
export JUNIORDEV__APPCONFIG__SEMANTICKERNEL__PROVIDER="openai"
export JUNIORDEV__APPCONFIG__SEMANTICKERNEL__MODEL="gpt-4"
export JUNIORDEV__APPCONFIG__SEMANTICKERNEL__MAXTOKENS="4096"
export JUNIORDEV__APPCONFIG__SEMANTICKERNEL__TEMPERATURE="0.7"

# UI Layout Override
export JUNIORDEV__APPCONFIG__UI__LAYOUTPATHOERRIDE="/path/to/custom/layout.json"

# Workspace Configuration
export JUNIORDEV__APPCONFIG__WORKSPACE__BASEPATH="/workspaces/junior-dev"
export JUNIORDEV__APPCONFIG__WORKSPACE__AUTOCREATEDIRECTORIES="true"

# Policy Configuration
export JUNIORDEV__APPCONFIG__POLICY__DEFAULTPROFILE="standard"
export JUNIORDEV__APPCONFIG__POLICY__GLOBALLIMITS__CALLSPERMINUTE="60"
export JUNIORDEV__APPCONFIG__POLICY__GLOBALLIMITS__BURST="10"
```

### UI Layout Overrides

To customize the UI layout, create a JSON file and set the `JUNIORDEV__APPCONFIG__UI__LAYOUTPATHOERRIDE` environment variable:

```json
{
  "panels": [
    {
      "name": "Sessions",
      "position": "left",
      "width": 300,
      "visible": true
    },
    {
      "name": "Conversation",
      "position": "center",
      "visible": true
    },
    {
      "name": "Artifacts",
      "position": "right",
      "width": 400,
      "visible": true
    }
  ],
  "theme": "dark",
  "fontSize": 12
}
```

## CI/CD Setup

For GitHub Actions or other CI systems, set secrets as environment variables:

```yaml
# .github/workflows/ci.yml
env:
  JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY: ${{ secrets.OPENAI_API_KEY }}
  JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN: ${{ secrets.GITHUB_TOKEN }}
  JUNIORDEV__APPCONFIG__AUTH__GIT__SSHKEYPATH: ${{ secrets.GIT_SSH_KEY }}
```

## Using Configuration in Code

```csharp
using Microsoft.Extensions.Configuration;
using JuniorDev.Contracts;

// Build configuration
IConfiguration config = ConfigBuilder.Build("Development");

// Get typed config
AppConfig appConfig = ConfigBuilder.GetAppConfig(config);

// Access specific sections
var auth = appConfig.Auth;
var adapters = appConfig.Adapters;
var policy = appConfig.Policy.Profiles[appConfig.Policy.DefaultProfile];
```

## Environment Variable Reference

| Section | Key | Environment Variable | Description |
|---------|-----|---------------------|-------------|
| **Adapters** | WorkItemsAdapter | `JUNIORDEV__APPCONFIG__ADAPTERS__WORKITEMSADAPTER` | "jira" or "github" |
| | VcsAdapter | `JUNIORDEV__APPCONFIG__ADAPTERS__VCSADAPTER` | "git" (only git supported) |
| | TerminalAdapter | `JUNIORDEV__APPCONFIG__ADAPTERS__TERMINALADAPTER` | "powershell" or "bash" |
| **Auth - Jira** | BaseUrl | `JUNIORDEV__APPCONFIG__AUTH__JIRA__BASEURL` | Jira instance URL |
| | Username | `JUNIORDEV__APPCONFIG__AUTH__JIRA__USERNAME` | Jira username/email |
| | ApiToken | `JUNIORDEV__APPCONFIG__AUTH__JIRA__APITOKEN` | Jira API token |
| | ProjectKey | `JUNIORDEV__APPCONFIG__AUTH__JIRA__PROJECTKEY` | Default Jira project key |
| **Auth - GitHub** | Token | `JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN` | GitHub PAT |
| | DefaultOrg | `JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTORG` | Default organization |
| | DefaultRepo | `JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTREPO` | Default repository |
| **Auth - Git** | SshKeyPath | `JUNIORDEV__APPCONFIG__AUTH__GIT__SSHKEYPATH` | Path to SSH key |
| | PersonalAccessToken | `JUNIORDEV__APPCONFIG__AUTH__GIT__PERSONALACCESSTOKEN` | Git PAT |
| | UserName | `JUNIORDEV__APPCONFIG__AUTH__GIT__USERNAME` | Git user name |
| | UserEmail | `JUNIORDEV__APPCONFIG__AUTH__GIT__USEREMAIL` | Git user email |
| | DefaultRemote | `JUNIORDEV__APPCONFIG__AUTH__GIT__DEFAULTREMOTE` | Default remote name |
| | BranchPrefix | `JUNIORDEV__APPCONFIG__AUTH__GIT__BRANCHPREFIX` | Prefix for created branches |
| **Auth - OpenAI** | ApiKey | `JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY` | OpenAI API key |
| | OrganizationId | `JUNIORDEV__APPCONFIG__AUTH__OPENAI__ORGANIZATIONID` | OpenAI org ID |
| **Auth - Azure OpenAI** | Endpoint | `JUNIORDEV__APPCONFIG__AUTH__AZUREOPENAI__ENDPOINT` | Azure endpoint URL |
| | ApiKey | `JUNIORDEV__APPCONFIG__AUTH__AZUREOPENAI__APIKEY` | Azure API key |
| | DeploymentName | `JUNIORDEV__APPCONFIG__AUTH__AZUREOPENAI__DEPLOYMENTNAME` | Deployment name |
| **Semantic Kernel** | Provider | `JUNIORDEV__APPCONFIG__SEMANTICKERNEL__PROVIDER` | "openai" or "azure-openai" |
| | Model | `JUNIORDEV__APPCONFIG__SEMANTICKERNEL__MODEL` | Model name |
| | MaxTokens | `JUNIORDEV__APPCONFIG__SEMANTICKERNEL__MAXTOKENS` | Max tokens |
| | Temperature | `JUNIORDEV__APPCONFIG__SEMANTICKERNEL__TEMPERATURE` | Temperature setting |
| | ProxyUrl | `JUNIORDEV__APPCONFIG__SEMANTICKERNEL__PROXYURL` | Proxy URL |
| | Timeout | `JUNIORDEV__APPCONFIG__SEMANTICKERNEL__TIMEOUT` | Request timeout |
| **UI** | LayoutPathOverride | `JUNIORDEV__APPCONFIG__UI__LAYOUTPATHOERRIDE` | Custom layout file path |
| | SettingsPathOverride | `JUNIORDEV__APPCONFIG__UI__SETTINGSPATHOERRIDE` | Custom settings file path |
| | Settings.Theme | `JUNIORDEV__APPCONFIG__UI__SETTINGS__THEME` | UI theme |
| | Settings.FontSize | `JUNIORDEV__APPCONFIG__UI__SETTINGS__FONTSIZE` | Font size |
| | Settings.ShowStatusChips | `JUNIORDEV__APPCONFIG__UI__SETTINGS__SHOWSTATUSCHIPS` | Show status chips |
| | Settings.AutoScrollEvents | `JUNIORDEV__APPCONFIG__UI__SETTINGS__AUTOSCROLLEVENTS` | Auto-scroll events |
| | Settings.ShowTimestamps | `JUNIORDEV__APPCONFIG__UI__SETTINGS__SHOWTIMESTAMPS` | Show timestamps |
| | Settings.MaxEventHistory | `JUNIORDEV__APPCONFIG__UI__SETTINGS__MAXEVENTHISTORY` | Max event history |
| **Workspace** | BasePath | `JUNIORDEV__APPCONFIG__WORKSPACE__BASEPATH` | Base workspace path |
| | BaselineMirrorPath | `JUNIORDEV__APPCONFIG__WORKSPACE__BASELINEMIRRORPATH` | Baseline mirror path |
| | AutoCreateDirectories | `JUNIORDEV__APPCONFIG__WORKSPACE__AUTOCREATEDIRECTORIES` | Auto-create directories |
| **Policy** | DefaultProfile | `JUNIORDEV__APPCONFIG__POLICY__DEFAULTPROFILE` | Default policy profile |
| | GlobalLimits.CallsPerMinute | `JUNIORDEV__APPCONFIG__POLICY__GLOBALLIMITS__CALLSPERMINUTE` | Global rate limit |
| | GlobalLimits.Burst | `JUNIORDEV__APPCONFIG__POLICY__GLOBALLIMITS__BURST` | Global burst limit |
| **Transcript** | Enabled | `JUNIORDEV__APPCONFIG__TRANSCRIPT__ENABLED` | Enable transcript persistence |
| | MaxMessagesPerTranscript | `JUNIORDEV__APPCONFIG__TRANSCRIPT__MAXMESSAGESPERTRANSCRIPT` | Max messages per transcript |
| | MaxTranscriptSizeBytes | `JUNIORDEV__APPCONFIG__TRANSCRIPT__MAXTRANSCRIPTSIZEBYTES` | Max transcript file size |
| | MaxTranscriptAge | `JUNIORDEV__APPCONFIG__TRANSCRIPT__MAXTRANSCRIPTAGE` | Max message age to keep |
| | TranscriptContextMessages | `JUNIORDEV__APPCONFIG__TRANSCRIPT__TRANSCRIPTCONTEXTMESSAGES` | Number of recent messages for AI context |
| | StorageDirectory | `JUNIORDEV__APPCONFIG__TRANSCRIPT__STORAGEDIRECTORY` | Custom storage directory |

## Observability & Monitoring

Junior Dev includes comprehensive observability features for monitoring adapter performance, rate limits, errors, and command execution. All metrics are exposed via .NET System.Diagnostics.Metrics and can be collected by monitoring systems like Application Insights, Prometheus, or OpenTelemetry.

### Enabling Metrics

Metrics are enabled by default in development but can be controlled via configuration:

```json
{
  "AgentConfig": {
    "EnableMetrics": true,
    "EnableDetailedLogging": true
  }
}
```

Or via environment variables:
```bash
JUNIORDEV__AGENTCONFIG__ENABLEMETRICS=true
JUNIORDEV__AGENTCONFIG__ENABLEDETAILEDLOGGING=true
```

### Available Metrics

#### Agent Metrics (JuniorDev.Agents)
- `command_latency_ms` (Histogram): Time taken to execute commands
- `commands_issued` (Counter): Number of commands issued by agents
- `commands_succeeded` (Counter): Number of commands that succeeded
- `commands_failed` (Counter): Number of commands that failed
- `events_processed` (Counter): Number of events processed by agents

#### GitHub Adapter Metrics (JuniorDev.WorkItems.GitHub)
- `commands_processed` (Counter): Number of commands processed
- `commands_succeeded` (Counter): Number of commands that succeeded
- `commands_failed` (Counter): Number of commands that failed
- `api_calls` (Counter): Number of API calls made to GitHub
- `api_errors` (Counter): Number of API errors encountered
- `circuit_breaker_trips` (Counter): Number of times circuit breaker opened

#### VCS Git Adapter Metrics (JuniorDev.VcsGit)
- `commands_processed` (Counter): Number of commands processed
- `commands_succeeded` (Counter): Number of commands that succeeded
- `commands_failed` (Counter): Number of commands that failed
- `git_operations` (Counter): Number of git operations performed
- `git_errors` (Counter): Number of git operation errors

#### Build Adapter Metrics (JuniorDev.Build.Dotnet)
- `builds_started` (Counter): Number of builds started
- `builds_succeeded` (Counter): Number of builds that succeeded
- `builds_failed` (Counter): Number of builds that failed
- `build_duration_ms` (Histogram): Time taken to complete builds

#### Jira Adapter Metrics (JuniorDev.WorkItems.Jira)
- `jira_commands_processed` (Counter): Number of Jira commands processed
- `jira_commands_succeeded` (Counter): Number of Jira commands that succeeded
- `jira_commands_failed` (Counter): Number of Jira commands that failed
- `jira_api_calls` (Counter): Number of API calls made to Jira
- `jira_api_errors` (Counter): Number of API errors from Jira

#### Rate Limiter Metrics (JuniorDev.Orchestrator.TokenBucketRateLimiter)
- `rate_limit_throttles` (Counter): Number of requests throttled by rate limiter

### Logging

Junior Dev uses structured logging with Microsoft.Extensions.Logging. Log levels can be controlled per category:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "JuniorDev": "Debug",
      "JuniorDev.WorkItems.GitHub": "Warning",
      "JuniorDev.VcsGit": "Warning",
      "JuniorDev.Build.Dotnet": "Warning"
    }
  }
}
```

### Viewing Metrics

#### Console Output
Metrics are automatically written to console in development. Look for lines like:
```
[Metrics] JuniorDev.WorkItems.GitHub.commands_processed{command_type="Comment"} 5
[Metrics] JuniorDev.Agents.command_latency_ms{command_type="BuildProject"} 1250.5
```

#### Application Insights
Configure Application Insights in `appsettings.json`:
```json
{
  "ApplicationInsights": {
    "ConnectionString": "your-connection-string"
  }
}
```

#### Prometheus
Use the `prometheus-net` package and expose metrics on a `/metrics` endpoint.

## Operations & Rollback Procedures

### Live Readiness Checklist

Before enabling live adapters, verify:

1. **Credentials Configured**: Run `ConfigBuilder.ValidateLiveAdapters()` to ensure all required credentials are present
2. **Dry-Run Testing**: Test all adapters with `DryRun = true` to verify functionality
3. **Rate Limits**: Configure appropriate rate limits for your environment
4. **Monitoring**: Enable metrics and logging to monitor adapter performance
5. **Backup**: Ensure you have backups of any repositories that will be modified

### Enabling Live Features

#### Safe Rollout Process

1. **Internal Testing** (Development Environment)
   ```json
   {
     "LivePolicy": {
       "PushEnabled": false,
       "DryRun": true,
       "RequireCredentialsValidation": true
     },
     "Adapters": {
       "WorkItemsAdapter": "fake",
       "VcsAdapter": "fake"
     }
   }
   ```

2. **Beta Testing** (Staging Environment)
   ```json
   {
     "LivePolicy": {
       "PushEnabled": false,
       "DryRun": true,
       "RequireCredentialsValidation": true
     },
     "Adapters": {
       "WorkItemsAdapter": "github",
       "VcsAdapter": "git"
     }
   }
   ```

3. **Limited Production** (Production with Restrictions)
   ```json
   {
     "LivePolicy": {
       "PushEnabled": false,
       "DryRun": false,
       "RequireCredentialsValidation": true
     },
     "Adapters": {
       "WorkItemsAdapter": "github",
       "VcsAdapter": "git"
     }
   }
   ```

4. **Full Production** (Complete Live Operation)
   ```json
   {
     "LivePolicy": {
       "PushEnabled": true,
       "DryRun": false,
       "RequireCredentialsValidation": true
     }
   }
   ```

### Emergency Rollback Procedures

#### Immediate Shutdown (Circuit Breaker Pattern)
If adapters are misbehaving, the circuit breaker will automatically open after consecutive failures. Monitor the `circuit_breaker_trips` metric.

#### Manual Adapter Disabling
To immediately disable live adapters:

1. **Disable Push Operations**:
   ```json
   {
     "LivePolicy": {
       "PushEnabled": false
     }
   }
   ```

2. **Enable Dry-Run Mode**:
   ```json
   {
     "LivePolicy": {
       "DryRun": true
     }
   }
   ```

3. **Switch to Fake Adapters**:
   ```json
   {
     "Adapters": {
       "WorkItemsAdapter": "fake",
       "VcsAdapter": "fake"
     }
   }
   ```

#### Session-Level Controls
Individual sessions can be controlled via the UI or API:
- Pause session processing
- Cancel active commands
- Reset session state

#### Configuration Rollback
To revert to a safe configuration:

1. **Restore from Backup**: Keep backups of working `appsettings.json` files
2. **Environment Variables**: Override dangerous settings with safe environment variables
3. **Feature Flags**: Use environment variables to override config values:
   ```bash
   JUNIORDEV__APPCONFIG__LIVEPOLICY__PUSHENABLED=false
   JUNIORDEV__APPCONFIG__LIVEPOLICY__DRYRUN=true
   JUNIORDEV__APPCONFIG__ADAPTERS__WORKITEMSADAPTER=fake
   JUNIORDEV__APPCONFIG__ADAPTERS__VCSADAPTER=fake
   ```

### Monitoring During Rollout

#### Key Metrics to Monitor
- `commands_failed` / `commands_processed` ratio (>5% may indicate issues)
- `circuit_breaker_trips` (should be 0 in normal operation)
- `rate_limit_throttles` (monitor for API limit issues)
- `api_errors` (GitHub/Jira API failures)
- `git_errors` (Git operation failures)

#### Alert Conditions
- Circuit breaker trips > 0
- Command failure rate > 10%
- API error rate > 5%
- Rate limit throttling > 50 requests/minute

#### Log Patterns to Monitor
```
ERROR: Failed to process command Comment
WARN: Circuit breaker open for command CreateBranch
WARN: Rate limited (429), attempt 2/3
ERROR: Git command failed with exit code 128
```

### Recovery Procedures

#### After Circuit Breaker Activation
1. Check adapter logs for root cause
2. Verify external service status (GitHub/Jira API)
3. Fix configuration or credentials if needed
4. Manually reset circuit breaker or restart service

#### After Rate Limit Issues
1. Increase rate limit buffers in configuration
2. Implement exponential backoff (already built-in)
3. Consider upgrading API plans if consistently hitting limits

#### After Git Operation Failures
1. Check repository permissions
2. Verify git configuration
3. Check for concurrent modifications
4. Manually resolve conflicts if needed

### Staged Rollout Gates

Use Gauntlet E2E tests to gate progression between stages:

1. **Gate 1 (Internal)**: All unit tests pass + basic E2E with fakes
2. **Gate 2 (Beta)**: E2E tests pass with live adapters in dry-run mode
3. **Gate 3 (Limited Production)**: 24-hour soak test with live adapters, no push
4. **Gate 4 (Full Production)**: 7-day monitoring period with full live operation

Each gate requires:
- 100% test pass rate
- <1% error rate in metrics
- Manual review of generated artifacts
- Stakeholder approval