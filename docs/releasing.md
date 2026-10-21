# Release version workflow

Run **Bump release version** from `develop`, providing the Unity and/or MCP version.
It pushes the version branch, creates or reuses a draft PR, checks that the PR points
to the pushed commit, and explicitly dispatches **Unity Tests** for that SHA.
An existing PR also gets a fresh CI request when the version branch is updated.

The workflow requires `contents: write`, `pull-requests: write`, and `actions: write`.
In addition, the repository's **Settings → Actions → General → Workflow permissions →
Allow GitHub Actions to create and approve pull requests** setting must permit PR
creation. Organization policy may restrict this setting. YAML permissions cannot
override it; this workflow does not change settings or install alternate credentials.

GitHub-token-created PR events can require approval, so release validation uses an
explicit `workflow_dispatch` instead of relying on PR events. See
[GitHub's token behavior](https://docs.github.com/en/actions/concepts/security/github_token).
The selected branch must contain the updated Unity Tests workflow, and that workflow
must first be present on the repository's default branch for manual dispatch.

## Failure and recovery

If PR creation is denied, the workflow fails and prints the existing branch's compare
URL plus `gh pr create` and `gh workflow run` recovery commands. The pushed branch is
retained. After the policy is resolved, rerun the bump workflow, or use the printed
commands with your normal GitHub account to create a draft and request tests.
When creating manually, assign the PR and apply the `chore` label.

If dispatch fails, the PR remains available and the bump workflow fails. Retry the
printed dispatch command. If `expected_sha` no longer matches the branch, inspect
the new diff and dispatch with its full current SHA; never treat an older run as
validation of the new commit. Each dispatched Unity test job verifies its checkout
before assembling the test project. Fork PR secret protections are unchanged.

**A successful bump run only means CI was requested.** Check the completed Unity
Tests run and both EditMode and PlayMode results for the exact PR HEAD before
merging. A failed, skipped, cancelled or queued run is not a pass. Review the draft,
mark it ready, merge, then create the release from the merge commit separately.
This workflow neither merges PRs nor publishes releases.

## Local helper tests

```sh
node --test .github/scripts/open-release-pr.test.mjs
```

The tests stub all GitHub calls: they do not create PRs, dispatch workflows or change
repository permissions. They cover new and existing PRs, denied creation, changed
HEAD/base/state, dispatch failures and unsafe inputs.
