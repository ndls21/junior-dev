# UI Shell

- Purpose: DevExpress chat/dockable UI consuming session event streams; provides controls (pause/resume/approve) and artifacts/diffs viewers.
- Structure: `src/` for UI app; `tests/` for component/layout tests and fixtures.
- Notes: columnar layout (sessions list left, conversation/log center, artifacts right), layout persistence/reset, blocking banners for approval/conflict/throttled states.
