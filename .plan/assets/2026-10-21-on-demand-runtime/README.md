# Overlay evidence

Unity 2022.3.34f1 rendered these images from the real package controllers in an
empty throwaway project. `default-hidden.png`, `visible.png`, and `hidden-again.png`
show the three visibility states.

This fixture uses `host.SetOverlay` and renders the canvases through a camera into
a 960×540 texture. It temporarily changes the canvases from ScreenSpaceOverlay to
ScreenSpaceCamera for offscreen capture. It demonstrates resource visibility and
layout, not a full MCP-to-player integration run or a performance benchmark.
The MCP schema/dispatch and Unity action/lifecycle tests cover those contracts separately.

To reproduce, copy `OverlayEvidenceTests.cs` into the throwaway package's
`Tests/PlayMode` folder, set `UNITY_PLAY_MCP_EVIDENCE_DIR` to the desired output
directory, and run Unity with `-batchmode -runTests -testPlatform PlayMode
-testFilter UnityPlayMcp.Tests.OverlayEvidenceTests` (without `-nographics`).
Remove the copied fixture afterward; it is not part of the package test suite.
