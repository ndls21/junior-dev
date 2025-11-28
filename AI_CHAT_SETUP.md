# Junior Dev AI Chat Setup

## AI Chat Configuration

Junior Dev now includes an interactive AI chat interface powered by DevExpress AI Chat Control. This allows you to have conversations with AI assistants to instruct agents for various tasks.

### Panel Overview

For detailed information about each UI panel and how they work together, see [`UI_PANELS_GUIDE.md`](UI_PANELS_GUIDE.md).

**Quick Panel Reference:**
- **AI Chat Panel**: Interactive conversations with AI assistants
- **Event Stream Panel**: Real-time system events and command results
- **Sessions Panel**: Active development session management
- **Artifacts Panel**: Generated files, test results, and logs

### Example Conversations

You can now instruct agents through natural language:

```
User: Create a new branch called "feature-user-auth" and implement basic user authentication

Assistant: I'll help you create that branch and implement authentication...

[Events show: CommandAccepted, BranchCreated, FilesModified, etc.]
```

### Supported AI Providers

Currently configured for OpenAI GPT-4o-mini. The system is extensible to support:
- Azure OpenAI
- Semantic Kernel
- Ollama (self-hosted models)

### Security Notes

- API keys are read from environment variables only
- Never commit API keys to version control
- The chat control supports streaming responses for real-time conversations