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

Set these environment variables:

```bash
# Required
JUNIORDEV__APPCONFIG__AUTH__JIRA__BASEURL=https://yourcompany.atlassian.net
JUNIORDEV__APPCONFIG__AUTH__JIRA__USERNAME=your.email@company.com
JUNIORDEV__APPCONFIG__AUTH__JIRA__APITOKEN=your_jira_api_token

# Or use user secrets (development)
dotnet user-secrets set "AppConfig:Auth:Jira:BaseUrl" "https://yourcompany.atlassian.net"
dotnet user-secrets set "AppConfig:Auth:Jira:Username" "your.email@company.com"
dotnet user-secrets set "AppConfig:Auth:Jira:ApiToken" "your_jira_api_token"
```

### GitHub Authentication

```bash
# Personal Access Token
JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN=your_github_token

# Optional defaults
JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTORG=your-org
JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTREPO=your-repo
```

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
# Set the opt-in flag
export RUN_AI_TESTS=1

# Provide OpenAI credentials
export OPENAI_API_KEY=your_openai_api_key
# Or via config
export JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY=your_openai_api_key

# Run AI tests specifically
dotnet test --filter "Category=AI"

# Or run all tests (AI tests will be skipped if not configured)
dotnet test
```

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

## Adapter Selection

Configure which adapters to use in `appsettings.json`:

```json
{
  "AppConfig": {
    "Adapters": {
      "WorkItemsAdapter": "jira",  // or "github"
      "VcsAdapter": "git",         // only "git" supported
      "TerminalAdapter": "powershell"  // or "bash" (powershell on Windows)
    }
  }
}
```

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

## Policy Configuration

```json
{
  "AppConfig": {
    "Policy": {
      "Profiles": {
        "default": {
          "Name": "Default Policy",
          "ProtectedBranches": ["master", "main", "develop"],
          "MaxFilesPerCommit": 50,
          "RequireTestsBeforePush": true,
          "RequireApprovalForPush": false,
          "Limits": {
            "CallsPerMinute": 60,
            "Burst": 10,
            "PerCommandCaps": {
              "RunTests": 5,
              "Push": 3
            }
          }
        }
      },
      "DefaultProfile": "default",
      "GlobalLimits": {
        "CallsPerMinute": 120,
        "Burst": 20
      }
    }
  }
}
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
| **Auth - GitHub** | Token | `JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN` | GitHub PAT |
| | DefaultOrg | `JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTORG` | Default organization |
| | DefaultRepo | `JUNIORDEV__APPCONFIG__AUTH__GITHUB__DEFAULTREPO` | Default repository |
| **Auth - Git** | SshKeyPath | `JUNIORDEV__APPCONFIG__AUTH__GIT__SSHKEYPATH` | Path to SSH key |
| | PersonalAccessToken | `JUNIORDEV__APPCONFIG__AUTH__GIT__PERSONALACCESSTOKEN` | Git PAT |
| | UserName | `JUNIORDEV__APPCONFIG__AUTH__GIT__USERNAME` | Git user name |
| | UserEmail | `JUNIORDEV__APPCONFIG__AUTH__GIT__USEREMAIL` | Git user email |
| | DefaultRemote | `JUNIORDEV__APPCONFIG__AUTH__GIT__DEFAULTREMOTE` | Default remote name |
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
| **Build** | Timeout | `JUNIORDEV__APPCONFIG__BUILD__TIMEOUT` | Build timeout (reserved for #32) |
| | AllowedTargets | `JUNIORDEV__APPCONFIG__BUILD__ALLOWEDTARGETS` | Allowed build targets (reserved for #32) |
| | MaxParallelJobs | `JUNIORDEV__APPCONFIG__BUILD__MAXPARALLELJOBS` | Max parallel build jobs (reserved for #32) |