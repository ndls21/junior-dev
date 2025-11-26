# Agents - Semantic Kernel Host

- Purpose: host planner/executor/reviewer agents that emit commands and react to events.
- Structure: `src/` for SK orchestration and prompt assets; `tests/` for golden outputs and policy/throttle handling.
- Notes: maintain deterministic prompts and correlation IDs; respect policy/rate signals from orchestrator.
