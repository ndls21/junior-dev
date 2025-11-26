# WorkItems-Jira Adapter

Jira work item adapter for Junior Dev, supporting comment, transition, and assignment operations.

## Configuration

### Environment Variables

Set these environment variables to enable real Jira operations:

- `JIRA_URL`: Base URL of your Jira instance (e.g., `https://yourcompany.atlassian.net`)
- `JIRA_PROJECT`: Project key (e.g., `PROJ`)
- `JIRA_USER`: Jira username or email
- `JIRA_TOKEN`: Jira API token (create at https://id.atlassian.com/manage-profile/security/api-tokens)

### Example

```bash
export JIRA_URL=https://yourcompany.atlassian.net
export JIRA_PROJECT=MYPROJ
export JIRA_USER=your.email@company.com
export JIRA_TOKEN=your-api-token-here
```

## Usage

The adapter automatically uses the fake implementation for testing. When environment variables are configured, it switches to the real Jira adapter.

### Commands Supported

- `Comment`: Add comments to work items
- `TransitionTicket`: Change work item status
- `SetAssignee`: Assign work items to users

### Work Item References

Work items are referenced by ID. If the ID doesn't contain a project key, the configured `JIRA_PROJECT` is prepended.

Examples:
- `PROJ-123` (full reference)
- `123` (becomes `PROJ-123` with configured project)

## Testing

Unit tests use the fake adapter and don't require network access. Integration tests are opt-in via environment variables.

Run tests:
```bash
dotnet test
```

For integration tests with real Jira:
```bash
# Set environment variables above, then:
dotnet test --filter Integration
```