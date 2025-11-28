# Multi-Agent Chat UI Design (Revised)

**Decision (2025-11-29): default to Accordion layout with integrated per-chat events; each chat stream maps 1:1 to a backend session. AI Chat control is the primary surface in each pane. Global artifacts remain shared, filterable by chat/session.**

## 🎯 **Key Insight: Per-Chat Event Streams**

- Each chat/agent generates its own event stream; combine only at the global artifacts layer.
- ChatStream maps to SessionId; UI routes events by SessionId (and IssuerAgentId when available).
- Global artifacts panel is shared, with filters for ChatStream/SessionId.

## 📐 **Revised Layout: Integrated Per-Agent Monitoring**

### **Option A: Split-Panel Per Agent**
```
┌─────────────────────────────────────────────────────────┐
│ Sessions │ Agent 1 Chat + Events │ Agent 2 Chat + Events│
│ (200px)  │ (400px)               │ (400px)              │
│          │ ┌───────────────────┐ │ ┌───────────────────┐ │
│          │ │ Chat Messages     │ │ │ Chat Messages     │ │
│          │ │ User: Create auth │ │ │ User: Fix tests   │ │
│          │ │ AI: Working...    │ │ │ AI: Running tests │ │
│          │ ├───────────────────┤ │ ├───────────────────┤ │
│          │ │ Events (50px)     │ │ │ Events (50px)     │ │
│          │ │ ✅ Branch created │ │ │ 🏁 Tests passed    │ │
│          │ └───────────────────┘ │ └───────────────────┘ │
│          └───────────────────────┴───────────────────────┘
│                                                         │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ Global Artifacts [Agent1] [Agent2] [All]           │ │
│ │ Build logs, test results, diffs from all agents    │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

**Pros**: Each agent has dedicated monitoring, clear separation
**Cons**: Less chat space per agent

### **Option B: Collapsible Monitoring Per Chat** (alternative)
```
┌─────────────────────────────────────────────────────────┐
│ Sessions │ [Agent 1 ▼] [Agent 2 ▼] [Agent 3 ▼] [+]     │
│ (200px)  │                                             │
│          │ ┌─────────────────────────────────────────┐ │
│          │ │ Agent 1 Chat                            │ │
│          │ │ User: Implement auth system             │ │
│          │ │ AI: I'll create JWT tokens...           │ │
│          │ └─────────────────────────────────────────┘ │
│          │ ▼ Events (expandable)                       │
│          │ [14:23:45] ✅ Command ACCEPTED              │
│          │ [14:23:46] 📁 Branch 'auth-feature' created │
│          │ [14:23:47] 📝 File modified: auth.js        │
│          │                                             │
│          └─────────────────────────────────────────────┘
│                                                         │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ Artifacts [Builds] [Tests] [Logs]                  │ │
│ │ Global artifact repository                         │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

**Pros**: Maximizes chat space, monitoring on-demand
**Cons**: Monitoring not always visible

### **Option C: VS Code-Style Split Panels** (variant)
```
┌─────────────────────────────────────────────────────────┐
│ Sessions │ Agent Panels (Split View)                   │
│ (200px)  │ ┌─────────────┬─────────────┐               │
│          │ │ Agent 1     │ Agent 2     │               │
│          │ │ Chat        │ Chat        │               │
│          │ │ + Events    │ + Events    │               │
│          │ └─────────────┴─────────────┘               │
│          └─────────────────────────────────────────────┘
│                                                         │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ Artifacts & Global Events                          │ │
│ │ Cross-agent monitoring and outputs                 │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### **Option D: Accordion Layout (Enhanced Preview) - RECOMMENDED**
```
┌─ Agent 1 (Expanded) ──────────────────────────────┐
│ Chat with Agent 1...                              │
│ User: Implement JWT authentication                │
│ Agent: I'll create the auth service...            │
│ Events: [14:23:45] ✅ Command ACCEPTED            │
│         [14:23:46] 📁 Branch 'auth-feature' created│
│         [14:23:47] 📝 File modified: auth.cs       │
└───────────────────────────────────────────────────┘
┌─ Agent 2 (Collapsed) ─┬─ Agent 3 (Collapsed) ─┐
│ Agent 2 - Working     │ Agent 3 - Idle        │
│ Status: Running       │ Status: Waiting       │
│ Last: "Fix API bug"   │ Last: "Update docs"   │
│ Progress: 60%         │ Progress: 0%          │
└───────────────────────┴───────────────────────┘
```

**Features:**
- **Rich Previews**: Collapsed agents show status, current task, progress
- **Multi-Expand**: Can expand 1, 2, or all agents simultaneously
- **Space Efficient**: Only active agents take full space
- **Quick Access**: Click collapsed header to expand instantly

**Pros:** Excellent for focus + overview, rich collapsed previews
**Cons:** Less simultaneous chat visibility than split panels

## 🔄 **Layout Alternatives to Tabs**

### **1. Docked Panels (VS Code Style)**
- **Horizontal Split**: Agents side-by-side
- **Vertical Split**: Agents stacked
- **Floating Panels**: Drag agents to separate windows
- **Tabbed within splits**: Combine approaches

### **2. Column-Based Layout**
```
┌───┬─────────────┬─────────────┬─────────────┐
│ S │ Agent 1     │ Agent 2     │ Agent 3     │
│ e │ Chat+Events │ Chat+Events │ Chat+Events │
│ s │ (400px)     │ (300px)     │ (300px)     │
│ s │             │             │             │
│ i │ ┌─────────┐ │ ┌─────────┐ │ ┌─────────┐ │
│ o │ │Messages │ │ │Messages │ │ │Messages │ │
│ n │ │Events   │ │ │Events   │ │ │Events   │ │
│ s │ └─────────┘ │ └─────────┘ │ └─────────┘ │
└───┴─────────────┴─────────────┴─────────────┘
```

**Features:**
- **Equal Visibility**: All agents visible simultaneously
- **Compact Events**: Events shown in smaller area per agent
- **Scalable**: Add/remove columns dynamically
- **Desktop Friendly**: Works well on wide screens

**Pros:** Maximum simultaneous visibility, predictable layout
**Cons:** Narrow chat areas on laptop screens

### **3. Master-Detail with Agent Switcher**
```
┌─────────────────────────────────────────────────────┐
│ Sessions │ Agent Switcher [1] [2] [3] [+] │        │
│         │                                        │
│         │ ┌─────────────────────────────────────┐  │
│         │ │ Active Agent Chat + Events         │  │
│         │ │ (Full width for selected agent)    │  │
│         │ └─────────────────────────────────────┘  │
│         └───────────────────────────────────────────┘
│                                                    │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Artifacts & Global Monitoring                  │ │
│ └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

## 💡 **Best Recommendation: Accordion Layout with Rich Previews**

### **Why Accordion Layout is Recommended:**
- **Rich Previews**: Collapsed agents show status, current task, progress percentage
- **Space Efficient**: Only active agents take full screen space
- **Multi-Expand**: Can expand 1, 2, or all agents simultaneously  
- **Quick Access**: Click collapsed header to expand instantly
- **Focus + Overview**: Perfect balance for managing multiple concurrent agents

### **Why Per-Agent Events Matter:**
- **Agent 1**: "Create auth system" → Branch → Implement → Test → Commit
- **Agent 2**: "Fix bug in API" → Analyze → Modify → Test → Push
- **Global View**: Too noisy, hard to follow specific agent progress

### **Proposed Solution: Accordion with Integrated Events**
1. **Accordion Layout**: Expandable panels with rich collapsed previews
2. **Each Panel**: Chat area + Events area when expanded
3. **Global Artifacts**: Bottom panel for cross-agent outputs
4. **Agent Management**: Add/remove agents dynamically

### **Screen Real Estate (Laptop 1366x768):**
- **Sessions**: 200px
- **Agent 1**: 350px (Chat 245px + Events 105px)
- **Agent 2**: 350px (Chat 245px + Events 105px)
- **Artifacts**: 466px (full width bottom)
- **Total**: 1366px ✅ **Fits perfectly!**

## 🚀 **Implementation Plan**

### **Phase 1: Core Multi-Agent Chat UI** - [Issue #24](https://github.com/ndls21/junior-dev/issues/24)
Implement configurable multi-agent chat interface with **accordion layout as default**.

### **Phase 2: Skills Implementation**
- Package Management Skills - [Issue #19](https://github.com/ndls21/junior-dev/issues/19)
- Script Execution Skills - [Issue #20](https://github.com/ndls21/junior-dev/issues/20)  
- File System Skills - [Issue #21](https://github.com/ndls21/junior-dev/issues/21)
- Terminal Adapter - [Issue #22](https://github.com/ndls21/junior-dev/issues/22)
- Policy Integration - [Issue #23](https://github.com/ndls21/junior-dev/issues/23)

Would you like me to implement the **accordion layout with rich previews**?

## 🎨 **Implementation Strategy: Configurable Layouts**

### **Phase 1: Core Accordion Implementation**
Start with accordion layout approach, but make layout **configurable** so users can switch between styles:

```csharp
public enum MultiAgentLayout
{
    Accordion,      // Expandable panels with rich previews (recommended default)
    SplitPanel,     // Side-by-side panels
    Column,         // Equal-width columns
    MasterDetail    // One active agent, others minimized
}

public class MultiAgentChatControl : UserControl
{
    public MultiAgentLayout Layout { get; set; } = MultiAgentLayout.Accordion;
    
    // Layout-specific implementations
    private void ApplyAccordionLayout() { /* ... */ }
    private void ApplySplitPanelLayout() { /* ... */ }
    private void ApplyColumnLayout() { /* ... */ }
}
```

### **Phase 2: Layout Comparison & Optimization**
Implement alternative layouts with user preference persistence (SplitPanel/Column/MasterDetail), gather feedback, and keep Accordion as default unless user overrides.

## 🔧 **Agent Capabilities & Terminal Access**

### **Current Agent Operations (Typed Commands)**
Agents currently work through Semantic Kernel functions that emit structured commands:
- **VCS**: create_branch, commit, push, get_diff, apply_patch, run_tests
- **Work Items**: claim_item, comment, transition, list_backlog, get_item  
- **General**: upload_artifact, request_approval

### **Future Terminal Operations (Skills Approach)**
For operations not covered by current commands, we'll extend the system with new typed commands rather than direct terminal access:

**Package Management:**
- `install_package("dotnet", "Microsoft.Extensions.AI", "8.0.0")`
- `install_package("npm", "axios", "^1.6.0")`

**Script Execution:**
- `run_script("./build.ps1", ["--clean", "--release"])`  
- `run_script("deploy.sh", ["--environment", "staging"])`

**File Operations:**
- `create_directory("src/components")`
- `copy_files("*.config", "backup/")`

### **Security & Auditability**
- All operations go through policy enforcement and rate limiting
- Commands are logged with correlation IDs for traceability
- Parameters are validated (safe paths, approved commands)
- No direct shell access - everything is structured and auditable

### **GitHub Issues for Skills Implementation**

#### **Issue: Package Management Skills** 
**Title:** "Implement Package Management Commands and SK Functions"  
**[Issue #19](https://github.com/ndls21/junior-dev/issues/19)**

#### **Issue: Script Execution Skills**
**Title:** "Implement Script Execution Commands and SK Functions"  
**[Issue #20](https://github.com/ndls21/junior-dev/issues/20)**

#### **Issue: File System Skills**
**Title:** "Implement File System Operation Commands and SK Functions"  
**[Issue #21](https://github.com/ndls21/junior-dev/issues/21)**

#### **Issue: Terminal Adapter Implementation**
**Title:** "Create TerminalAdapter for Safe Command Execution"  
**[Issue #22](https://github.com/ndls21/junior-dev/issues/22)**

#### **Issue: Policy Integration for Terminal Commands**
**Title:** "Extend PolicyProfile for Terminal Command Restrictions"  
**[Issue #23](https://github.com/ndls21/junior-dev/issues/23)**
