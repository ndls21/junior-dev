# Junior Dev UI Panels Guide

## Panel Overview

Junior Dev uses a four-panel dockable layout designed for AI-assisted development workflows. Each panel serves a specific purpose in the development process.

## 🏗️ **Sessions Panel (Left Dock)**

**Purpose**: Manage and monitor active development sessions.

**Contents**:
- List of all active development sessions
- Status indicators with colored chips:
  - 🟢 **Running**: Session actively processing commands
  - 🟡 **Paused**: Session paused, awaiting user input or approval
  - 🔴 **Error**: Session encountered an error and needs attention
  - 🟠 **Needs Approval**: Session blocked waiting for user approval
  - 🔵 **Completed**: Session finished successfully

**Interactions**:
- **Filter buttons**: All/Running/Paused/Error to show relevant sessions
- **Click session**: Switch active session context (future feature)
- **Status chips**: Visual indicators of session health

**Example Use**: Filter to "Error" sessions to quickly identify issues needing attention.

---

## 🤖 **AI Chat Panel (Center-Top)**

**Purpose**: Interactive conversations with AI assistants for instructing development tasks.

**Contents**:
- Chat interface powered by DevExpress AI Chat Control
- Streaming responses from AI providers (OpenAI, Azure OpenAI, etc.)
- Conversation history with user and AI messages

**Interactions**:
- **Type messages**: Natural language instructions like:
  - "Create a new branch called 'feature-auth' and implement user authentication"
  - "Run tests and fix any failing ones"
  - "Review the code changes and suggest improvements"
- **File attachments**: Upload documents for AI analysis (future feature)
- **Prompt suggestions**: Quick-start prompts for common tasks (future feature)

**Example Use**: "Implement a REST API endpoint for user registration with validation and error handling."

---

## 📋 **Event Stream Panel (Center-Bottom)**

**Purpose**: Real-time monitoring of system events and command execution results.

**Contents**:
- Timestamped event log with correlation IDs
- Command lifecycle events:
  - ✅ **Command Accepted**: Command approved and queued
  - 🏁 **Command Completed**: Command finished (Success/Failed with message)
  - ❌ **Command Rejected**: Command blocked by policy
  - 📎 **Artifact Available**: Files/logs/test results generated
  - 🔄 **Session Status Changed**: Session state transitions
  - ⏱️ **Throttled**: Rate limit hit with retry timing
  - ⚠️ **Conflict Detected**: Merge conflicts or policy violations

**Interactions**:
- **Auto-scroll**: Automatically follows new events (configurable)
- **Read-only display**: Events are append-only system logs
- **Correlation tracking**: Each event shows session and command IDs

**Example Events**:
```
[14:23:45.123] [sess_abc123] [cmd_def456] ✅ Command ACCEPTED
[14:23:46.234] [sess_abc123] [cmd_def456] 🏁 Command SUCCESS - Tests passed (15/15)
[14:23:47.345] [sess_abc123] [cmd_def456] 📎 ARTIFACT: test-results (Test Results) - All 15 tests passed successfully
```

---

## 📁 **Artifacts Panel (Right Dock)**

**Purpose**: Browse and inspect generated artifacts from development activities.

**Contents**:
- Tree view of categorized artifacts:
  - **Build Results**: Compilation outputs and errors
  - **Test Results**: Unit/integration test reports
  - **Diff Output**: Code change summaries
  - **Log Files**: Detailed execution logs

**Interactions**:
- **Expand/collapse**: Navigate artifact categories
- **Double-click**: Open artifact details (future feature)
- **Context menu**: Export/save artifacts (future feature)

**Example Use**: Expand "Test Results" to review failing tests after a build.

---

## 🔄 **Panel Interactions & Workflow**

### Typical Development Workflow:

1. **Start Session**: New session appears in Sessions Panel with "Running" status
2. **Give Instructions**: Type natural language commands in AI Chat Panel
3. **Monitor Progress**: Watch Event Stream Panel for command execution and results
4. **Review Outputs**: Check Artifacts Panel for generated files, test results, and logs
5. **Handle Issues**: Use Sessions Panel filters to find sessions needing attention

### Cross-Panel Coordination:

- **Sessions ↔ Events**: Session status changes appear in both panels
- **AI Chat ↔ Events**: User instructions trigger command events in the stream
- **Events ↔ Artifacts**: Successful commands generate artifacts for review
- **All Panels**: Layout persists between sessions, customizable per user preference

---

## ⚙️ **Configuration & Settings**

### Layout Management:
- **Dockable**: Drag panels to rearrange layout
- **Floating**: Tear off panels for multi-monitor setups
- **Reset**: View → Reset Layout (Ctrl+R) restores defaults
- **Persistence**: Layout automatically saved to `%APPDATA%\JuniorDev\layout.xml`

### Settings (View → Settings):
- **Theme**: Light/Dark/Blue color schemes
- **Font Size**: 8-16pt adjustable text
- **Status Chips**: Toggle visual session indicators
- **Auto-scroll Events**: Keep event stream at bottom

---

## 🔮 **Future Enhancements**

- **Session Switching**: Click sessions to change active context
- **Artifact Viewers**: Integrated diff/log viewers in artifacts panel
- **File Attachments**: Upload documents to AI chat for analysis
- **Prompt Suggestions**: Quick-start templates for common tasks
- **Multi-Agent Chat**: Multiple concurrent AI chat panels (tabbed or columnar)
- **Combined Monitoring Panel**: Event Stream + Artifacts in unified tabbed interface