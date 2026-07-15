# Branch Workflow

## Why

Predictable branch names expose intent without relying on tracker-specific numbers.

## Naming

```text
<type>/<short-description>
```

Examples:

```text
feat/add-session-timeout
fix/handle-empty-response
docs/local-test-setup
```

Allowed types:
- `feat`: user-visible capability
- `fix`: defect correction
- `refactor`: behavior-preserving structure change
- `perf`: measured performance improvement
- `test`: test-only change
- `docs`: documentation-only change
- `build`: build or dependency change
- `ci`: automation change
- `chore`: maintenance not covered above

Description rules:
- use lowercase kebab-case
- keep it short but specific enough to expose intent
- do not require a GitHub Issue number
- link a Jira work item in the PR when one exists; Jira linkage is optional

## Lifecycle

- Branch from repository default branch unless project policy says otherwise.
- Keep one primary issue per branch.
- Sync with default branch before final validation when divergence matters.
- Never force-push a shared branch without coordination.
- Delete branch after merge when no follow-up work depends on it.
