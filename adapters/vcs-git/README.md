# VCS - Git Adapter

- Purpose: implements branch/create/apply patch/diff/commit/push, with dry-run mode and conflict surfacing.
- Structure: `src/` for git CLI adapter; `tests/` for fakes and temp-repo scenario tests.
- Notes: enforce protected branches, max-files-per-commit; emit `ConflictDetected` with patch when merge fails.
