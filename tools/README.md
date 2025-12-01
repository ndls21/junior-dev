# Tools

This directory contains utility tools for the Junior Dev project.

## DevExpressInspector

A diagnostic tool for inspecting the DevExpress AI Chat control API. Useful for troubleshooting integration issues with the DevExpress.AIIntegration.WinForms.Chat package.

### Usage

```bash
# Inspect message types and enums
dotnet run -- messages

# Inspect AIChatControl events
dotnet run -- events

# Inspect everything
dotnet run -- all

# Show help
dotnet run -- --help
```

### What it inspects

- **Messages**: `BlazorChatMessage` properties, constructors, and related enums (`ChatMessageRole`, `Microsoft.Extensions.AI.ChatRole`)
- **Events**: `AIChatControl.MessageSent` event handler types and properties
- **Methods**: AIChatControl methods related to messaging

This tool was created during the implementation of transcript persistence (issue #37) to understand the DevExpress control API.