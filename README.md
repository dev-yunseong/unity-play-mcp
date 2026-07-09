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
