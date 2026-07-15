# Jira Work Item Workflow

## Why

A Jira work item defines the problem and acceptance boundary. It should not prescribe implementation before investigation.

Use Jira for development tracking. Do not create GitHub Issues for branch or PR bookkeeping. Missing Jira work must not block implementation unless project governance explicitly requires a ticket.

## Ready Criteria

A work item is ready when it has:
- clear problem statement
- user or system impact
- acceptance criteria
- known constraints
- explicit non-goals when scope could expand
- dependencies or blockers

## Work Item Template

```markdown
## Problem

## Impact

## Acceptance Criteria
- [ ]

## Constraints

## Non-goals

## Validation Notes
```

## Lifecycle

1. Create or refine the Jira work item when tracking is required.
2. Confirm dependencies and priority.
3. Mark in progress only when active work starts.
4. Link plan and PR from Jira when a work item exists.
5. Update scope changes in Jira before implementing them.
6. Close only after acceptance criteria and required validation pass.

## Sizing

Split the Jira work item when it contains multiple independently releasable outcomes or requires unrelated ownership areas.

Do not split tightly coupled steps that cannot provide value or validation independently.
