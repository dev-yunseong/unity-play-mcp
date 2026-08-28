# Issue Workflow

## Why

An issue defines the problem and acceptance boundary. It should not prescribe implementation before investigation.

## Ready Criteria

An issue is ready when it has:
- clear problem statement
- user or system impact
- acceptance criteria
- known constraints
- explicit non-goals when scope could expand
- dependencies or blockers
- an explicit assignee for the person responsible; do not leave ownership to a branch or PR author
- exactly one `type:<feat|fix|chore|docs|refactor|infra>` label
- an existing milestone selected before branch creation

## Issue Template

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

1. Create or refine issue.
2. Confirm dependencies and priority.
3. Add `status:in-progress` only when active work starts, then create the branch
   described in `workflow.md` from `origin/develop`.
4. Link branch, plan, and PR. The branch name carries the issue number, which is
   what ties commits and the PR back to this issue.
5. Update scope changes in issue before implementing them.
6. Close only after acceptance criteria and required validation pass.

## Progress Updates

Keep the issue current as the work moves, not at the end. Anyone reading the
issue should see the real state without asking.

- Change status the moment reality changes: add `status:in-progress` when work
  actually starts, replace it with `status:in-review` when the PR opens, and
  close the issue after merge and required validation. Never batch state
  changes at the end of the work.
- Comment at each milestone, with links rather than prose: plan path, branch
  name, PR URL, validation or test evidence, and the decision taken whenever the
  approach changed.
- Comment as soon as the work is blocked, naming what blocks it and what would
  unblock it. Do not leave a stalled issue carrying `status:in-progress` with no
  explanation.
- Edit the description when scope, acceptance criteria, or non-goals change, and
  do it before implementing the change. Comments record history; the description
  records the current contract.
- Never let the issue contradict git, the PR, or the deploy state. When they
  drift, fix the issue in the same pass.

## Sizing

Split issue when it contains multiple independently releasable outcomes or requires unrelated ownership areas.

Do not split tightly coupled steps that cannot provide value or validation independently.
