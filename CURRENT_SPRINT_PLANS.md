# Current Sprint Plans

## Phase 1: Live Readiness
Complete remaining items to enable safe real-repo operations.

### #38: Live credentials & secrets hardening for Jira/GitHub
**Summary**: Harden live config/secrets for Jira/GitHub so real runs are safe and repeatable.

**Scope**:
- Config shape: document required keys (PAT/API token, base URL, repo/owner, project key, branch prefix) in CONFIG.md; align with appsettings.* and env vars.
- Validation: add a startup check (or CLI helper) that validates presence/scope of creds and repo/project identifiers; produce friendly errors.
- Defaults: dry-run and push-off enabled by default for live profile.
- Secrets handling: support env vars and user-secrets; ensure nothing sensitive is logged; update .env.local.example.
- Tests: gated live integration sanity (auth-only) and failure-mode tests (missing creds) that skip gracefully.

**Dependencies**: Depends on config unification (#28) and UI live hookup (#33) to surface errors nicely.

### #37: Persist & restore chat transcripts per SessionId
**Summary**: Add local transcript persistence so each AI chat stream (SessionId) can be restored after app restart or re-attach.

**Background**: Attach flow currently reuses a SessionId but doesn't reload prior messages. We need durable chat history to resume conversations and give agents context.

**Scope**:
- Storage: default to platform app data (e.g., %APPDATA%/JuniorDev/Chats/{sessionId}.json); JSON array of messages { role, content, timestamp, messageId? }. Keep secrets/keys out of transcripts.
- Load on create/attach: if a transcript exists, hydrate the AIChatControl with history before showing the panel.
- Save on update: append new messages/events and flush to disk; handle corruption by falling back to empty and rewriting.
- Pruning: cap by message count/size/age; drop oldest when limits exceeded.
- Config: setting to enable/disable persistence and tune caps; default on with safe caps.
- Tests: round-trip save/load ordering; corrupt file fallback; pruning boundaries; multi-session isolation; disabled mode (no file writes).
- Docs: update ARCHITECTURE.md / MULTI_AGENT_CHAT_DESIGN.md / CONFIG.md to describe location, caps, opt-out.

**Dependencies**: Blocked by #34 (UI↔orchestrator bridge). Should land before any future chat-history UX polish.

## Phase 2: Advanced SK Features
Enhance Semantic Kernel agents with repository-wide intelligence.

### #44: Reviewer SK Phase 2: repository-wide analysis
**Summary**: Phase 2 for Reviewer SK: add optional repository-wide analysis (structure, quality, security, performance, dependencies) beyond the current diff/log/test review in #8.

**Scope**:
- Structure/quality (touched paths-first): analyze architecture/layers, complexity, duplication, naming, best practices on changed files/dirs with configurable depth.
- Security/perf/deps (opt-in): lightweight scans for common vulns, perf anti-patterns, dependency currency; capped by file count/size and cost limits.
- Caching: cache results per commit/path to avoid re-scanning unchanged code.
- Config: settings for depth, max files, focus areas (security/perf/quality), token/cost caps; conservative defaults.
- SK functions: extend ReviewAnalysisPlugin with modular functions (e.g., analyze_structure, analyze_quality, security_scan, perf_scan, dep_audit) that operate on provided paths/content.
- VCS helper: expose read/list helpers; keep heavy scanning out of adapters.
- Tests: unit tests for function selection/pruning; integration against small sample repos; rate-limit/backoff coverage.

**Dependencies**: Depends on #8 (baseline reviewer SK for diffs/logs/tests). Needs stable routing/history (#34, #37) to give context and results visibility.

**Notes**: Start with touched-files/touched-dirs only; full-repo scanning should remain opt-in and bounded.

### #45: Planning SK Phase 2: intelligent work-item analysis & DAG plans
**Summary**: Phase 2 for planning: intelligent work-item planning with content analysis, DAG task plans, risk/resource assessment, and repo-aware context. Builds on #9.

**Scope**:
- Work item parsing: extract requirements/acceptance criteria/constraints/stakeholders from descriptions/comments/attachments via SK functions.
- DAG plans: generate multi-step task graphs (dependencies/ordering) instead of single-node plans; break down complex work.
- Context-aware: factor repository structure/patterns into plans; consult adapters for tree/info.
- Risk/testing: flag high-risk changes; suggest testing/rollback strategies; estimate effort/complexity.
- Resource hints: suggest agent roles/skills and rough time by task type.
- Plan validation/refinement: loop to refine/validate plans against constraints.

**Implementation**:
- Extend PlanningPlugin with parsing/analysis SK functions; integrate with work-item adapters for content.
- Add repo-context helper (read tree/paths) to inform planning; keep bounded.
- Config for depth/cost caps; conservative defaults.
- Tests: unit tests for parsing/plan-shaping; integration on sample work items; ensure DAG validity and ordering.

**Dependencies**: Depends on #9 (baseline plan generation) and #34 (routing). Benefits from #37 (history) for context.

## Advocate Epic: Multi-Agent Advocate System
Implement collaborative AI development with mediator agents.

### #49: Epic: Multi-Agent Advocate System
**Description**: Implement a coordinator agent that mediates between specialized agents (coder + reviewer) to facilitate collaborative development workflows.

**Acceptance Criteria**:
- Advocate agent can facilitate coder-reviewer conversations
- UI shows agent interactions and allows human intervention
- System maintains conversation context across agent exchanges
- Performance doesn't degrade with multiple active agents

**Dependencies**: #38 (secrets), #37 (UI improvements), Phase 1 completion

**Linked Issues**:
- #50: Agent Communication Protocol
- #51: Advocate Agent Implementation  
- #52: Multi-Agent Session Management
- #53: Agent Conversation UI
- #54: Advocate System Integration Testing

### #50: Agent Communication Protocol (Orchestrator)
**Description**: Add infrastructure for agents to communicate directly within sessions.

**Technical Details**:
- New events: AgentMessage, AgentResponse, AgentConversationStarted
- Message routing between agents in same session
- Conversation threading and context sharing
- Rate limiting for agent-to-agent messages

**Acceptance Criteria**:
- Agents can send/receive messages via orchestrator
- Messages include correlation IDs and conversation threads
- Events are properly serialized and logged
- Unit tests for message routing

**Dependencies**: #38, #46 (observability)

**Part of Epic**: #49 (Multi-Agent Advocate System)

### #51: Advocate Agent Implementation (Agents-SK)
**Description**: Create the advocate agent that mediates between coder and reviewer.

**Technical Details**:
- New AdvocateAgent class extending AgentBase
- Specialized prompts for diplomatic communication
- State tracking for ongoing conversations
- Integration with Semantic Kernel for mediation logic

**Acceptance Criteria**:
- Advocate can receive coder output and reviewer feedback
- Generates diplomatic summaries and recommendations
- Maintains conversation history
- Can escalate to human when agents disagree

**Dependencies**: #50

**Part of Epic**: #49 (Multi-Agent Advocate System)

### #52: Multi-Agent Session Management (Orchestrator)
**Description**: Extend session management to support multiple coordinated agents.

**Technical Details**:
- Session config supports agent combinations
- Agent lifecycle management (start/stop individual agents)
- Shared context across agents in session
- Approval gates for multi-agent decisions

**Acceptance Criteria**:
- Sessions can register multiple agents
- Agents share session context
- Individual agent pause/resume works
- Session status reflects multi-agent state

**Dependencies**: #50, #42 (kill switches)

**Part of Epic**: #49 (Multi-Agent Advocate System)

### #53: Agent Conversation UI (UI-Shell)
**Description**: Add UI components to display and manage agent conversations.

**Technical Details**:
- New panel for agent-to-agent dialogues
- Conversation threading visualization
- Human intervention controls (approve/reject agent decisions)
- Status indicators for active agent negotiations

**Acceptance Criteria**:
- Users can view agent conversations in real-time
- Intervention buttons dispatch appropriate commands
- Conversation history is searchable/filterable
- UI remains responsive with multiple active agents

**Dependencies**: #52, #37 (UI improvements), #41 (UI validation)

**Part of Epic**: #49 (Multi-Agent Advocate System)

### #54: Advocate System Integration Testing
**Description**: End-to-end testing for the complete advocate workflow.

**Technical Details**:
- Integration tests with fake agents
- Gauntlet scenarios for coder-reviewer-advocate flows
- Performance testing with multiple agents
- Error handling and recovery scenarios

**Acceptance Criteria**:
- Full advocate workflow executes successfully
- Performance benchmarks meet requirements
- Error cases handled gracefully
- Documentation updated with advocate usage

**Dependencies**: #51, #53, #43 (build validation)

**Part of Epic**: #49 (Multi-Agent Advocate System)