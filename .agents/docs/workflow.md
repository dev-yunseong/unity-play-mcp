# Development Workflow

## Why

Small, explicit steps reduce regressions and make review, rollback, and handoff predictable.

## Work Classification

Trivial work:
- documentation typo
- isolated formatting fix
- deterministic one-line configuration change

Non-trivial work:
- behavior change
- bug fix requiring investigation
- dependency or schema change
- cross-module refactor
- user-facing workflow change

Non-trivial work requires a concise plan. Use the `writing-plan` skill when it
is installed.

## End-to-End Flow

1. Confirm goal, scope, acceptance criteria, and non-goals.
2. Read project context, relevant code, tests, and recent changes.
3. Create the GitHub Issue, the branch, and — once the work is ready — the PR, following `## Issue-Driven Development Flow`. These are part of doing the work; do not wait for an explicit request to create them. Reuse issue and branch context already provided by the user or environment rather than creating a duplicate.
4. Write a concise implementation plan; use `writing-plan` when installed.
5. Identify architecture impact, tradeoffs, risks, and rollback.
6. Implement the smallest coherent change.
7. Follow `testing.md`; use an installed testing skill when available.
8. Review the complete diff for scope, correctness, and accidental churn.
9. Commit coherent units using the commit convention.
10. Open a draft PR with evidence and explicit remaining risk. Follow
    `pull-request.md` for draft ownership and user-notification rules.
11. Address review without hiding unresolved concerns.

## Issue-Driven Development Flow

Use this pipeline when the work item is tracked in a GitHub Issue and the user
asks for end-to-end development.

1. **Create the issue.** Use `gh issue create` in this repository and follow
   `issue.md` for the body. Set the identifying metadata explicitly; a title
   alone leaves the issue unassigned and unclassified, and it will not show up
   in the right filters:
   - assignee: the person who will do the work. Set it with
     `gh issue edit --add-assignee <login>`; never leave it empty or infer
     ownership from the branch or PR author.
   - milestone: select the existing milestone that owns this outcome before
     branch creation.
   - type label: exactly one of `type:feat`, `type:fix`, `type:chore`,
     `type:docs`, `type:refactor`, or `type:infra`.
   - labels: add another only when the work belongs to a theme the type label
     does not already express. Reuse an existing label instead of inventing a
     near-duplicate.
   - dates: record the start and completion dates in the issue body or a
     comment as the work moves — see `## Issue Dates`.

   Use `gh issue list --label ...` to find existing work before creating a
   duplicate.

2. **Move to in progress and create the branch.** Add the
   `status:in-progress` label the moment active work starts. Then create this
   branch explicitly from `origin/develop`:

   ```bash
   gh issue edit <issue number> --add-label status:in-progress
   git fetch origin develop
   git switch -c <branch name> origin/develop
   ```

   ```text
   <type>/<issue title with spaces replaced by hyphens>-<issue number>
   ```

   For example, `chore/configure-github-issue-workflow-69`. Korean characters
   stay as they appear in the title.

   The issue number in the branch name is what ties branch, commits, and PR back
   to the issue, so never create the branch before the issue exists. Keep one
   issue per branch, never force-push a shared branch without coordination,
   and delete the branch after merge unless follow-up work depends on it.

3. **Plan.** Use the `writing-plan` skill. Plans land in `.plan/general/`.

4. **Review the plan.** Use the `plan-review` skill.

5. **Loop on the plan.** Fold each finding back into the plan and review again.
   Leave the loop only when no remaining finding requires a plan change. Do not
   start implementing to settle a planning disagreement.

6. **Implement.** Follow the implementation, testing, diff-review, and commit
   steps of `## End-to-End Flow`.

7. **Pair review.** Use the `pair-review` skill, which drives the
   `pair-review-critic` subagent against the implementation. Resolve or
   explicitly accept every finding before opening the PR.

8. **Open the draft PR.** Do this as soon as the work is ready, without waiting to be
   asked. Follow `pull-request.md`, targeting `develop`. Set the assignee and
   the type label, fill in `Code Walkthrough` with one entry per changed unit,
   and end the body with a `Closes #<issue number>` trailer so the issue links
   back and closes when the PR merges.

Replace `status:in-progress` with `status:in-review` when the PR opens:

```bash
gh issue edit <issue number> --remove-label status:in-progress --add-label status:in-review
```

Close the issue only after merge and required validation pass.

## Issue Dates

Every issue records a start date and completion date in its body or a comment.
Both record what actually happened in Git rather than an estimate, so the two
dates read as the work's real span:

- **Start date** — the date of the first commit carrying `Refs: #<issue number>`,
  or the PR open date when that is earlier. Record it when the issue gains
  `status:in-progress`; if
  the branch has no commit yet, use the date the branch was created.
- **Completion date** — the date the PR merged. Record it when the issue closes.
  When several PRs carry the issue number, use the last merge.

Write both as `YYYY-MM-DD` in `Asia/Seoul`, so a late-night commit lands on the
day it was made locally. Do not overwrite a date that is already recorded
unless the Git history contradicts it.

## Change Rules

- Preserve existing architecture unless the task requires changing it.
- Keep unrelated cleanup out of the change.
- Add abstractions only when they remove demonstrated complexity or match an established pattern.
- Keep migrations backward-compatible when practical.
- Prefer reversible rollout for high-risk behavior.

## Stop Conditions

Pause and surface the problem when:
- requirements conflict
- destructive action lacks approval
- required credentials or external access are unavailable
- validation reveals an unrelated pre-existing failure that blocks confidence
- scope expands beyond the agreed issue or plan

Do not silently guess through high-impact ambiguity.
