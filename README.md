# Unity Play MCP

Unity Play MCP is packaged for Unity through Unity Package Manager.

## Package

The Unity package lives at:

```text
Packages/dev.yunseong.unityplaymcp
```

Runtime scripts are under `Runtime/` and compiled through
`Artel.Runtime.asmdef`.

## Sample

`samples/WordVenture` is included as the sample Unity project. It references
the local package with:

```json
"dev.yunseong.unityplaymcp": "file:../../../Packages/dev.yunseong.unityplaymcp"
```

That is the reference the sample needs after the rename, and it is not the one
the submodule holds today — the sample still points at the old package id and
path. `samples/WordVenture` is a separate repository, so the change has to be
made there before the sample opens against this package.

Open `samples/WordVenture` in Unity to try package runtime components from a real
Unity project.

## Tests and CI

Neither the repository root nor `samples/WordVenture` can run the package's
tests as checked out, so both local runs and CI assemble a throwaway Unity
project first:

```bash
.github/scripts/setup-unity-test-project.sh /tmp/unity-play-mcp-test
```

`.github/workflows/unity-tests.yml` runs EditMode and PlayMode against that
project on every pull request and on every push to `develop`. It needs the Unity
licence secrets `UNITY_LICENSE` (or `UNITY_SERIAL` for Pro/Plus), `UNITY_EMAIL`,
and `UNITY_PASSWORD`; without them the workflow fails and names the missing one.

`.agents/docs/project.md` — *Running package tests* and *Continuous integration*
— has the full editor command line, where to obtain each secret, and how fork
pull requests are handled.
