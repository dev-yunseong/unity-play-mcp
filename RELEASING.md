# Release procedure

1. Check which version is not released yet: the Unity package version lives in `Packages/dev.yunseong.unityplaymcp/package.json`, and the tag `v<version>` marks a Unity release; the MCP server version lives in `mcp/package.json`, and `npm view unity-play-mcp versions` shows what is already published.
2. In the GitHub Actions tab, run **Bump release version** (`workflow_dispatch`) with `unity_version` and/or `mcp_version`. The two inputs are independent — leave one empty to leave that side untouched. The workflow rejects a version that is already released (an existing git tag for `unity_version`, or an existing npm package version for `mcp_version`).
3. Review the diff on the draft pull request the workflow opens, mark it ready for review, and merge it into `develop`.
4. If `unity_version` was bumped, create a GitHub Release from the merge commit with tag `v<unity_version>`. The workflow does not create the release or the tag — a human still does this step.
5. `.github/workflows/publish-mcp.yml` reacts to `release: published`, validates the version files against the tag, and publishes `mcp/` to npm when it changed since the previous release.

The workflow needs the repository setting **Settings → Actions → General → "Allow GitHub Actions to create and approve pull requests"** turned on. If it is off, step 2 fails when the workflow tries to open the pull request.
