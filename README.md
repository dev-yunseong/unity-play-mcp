# artel-sdk

Artel SDK is packaged for Unity through Unity Package Manager.

## Package

The Unity package lives at:

```text
Packages/kr.artel.sdk
```

Runtime scripts are under `Runtime/` and compiled through
`Artel.Runtime.asmdef`.

## Sample

`samples/WordVenture` is included as the sample Unity project. It references
the local SDK package with:

```json
"kr.artel.sdk": "file:../../../Packages/kr.artel.sdk"
```

Open `samples/WordVenture` in Unity to try SDK runtime components from a real
Unity project.

## Tests and CI

Neither the repository root nor `samples/WordVenture` can run the package's
tests as checked out, so both local runs and CI assemble a throwaway Unity
project first:

```bash
.github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test
```

`.github/workflows/unity-tests.yml` runs EditMode and PlayMode against that
project on every pull request and on every push to `develop`. It needs the Unity
licence secrets `UNITY_LICENSE` (or `UNITY_SERIAL` for Pro/Plus), `UNITY_EMAIL`,
and `UNITY_PASSWORD`; without them the workflow fails and names the missing one.

`.agents/docs/project.md` — *Running package tests* and *Continuous integration*
— has the full editor command line, where to obtain each secret, and how fork
pull requests are handled.
