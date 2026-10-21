# Runtime lifecycle and idle performance validation

These checks run in the disposable project assembled by
`.github/scripts/setup-unity-test-project.sh`, never in a game's project.
Use the editor version pinned in `.github/unity-test-project/ProjectSettings/ProjectVersion.txt`.
They supplement the normal EditMode and PlayMode suites; neither runs automatically in CI.

## Actual domain reload

```sh
.github/scripts/setup-unity-test-project.sh /tmp/unity-play-mcp-test
python3 .github/scripts/run-runtime-lifecycle.py --editor /path/to/Unity --project /tmp/unity-play-mcp-test
python3 .github/scripts/test-run-runtime-lifecycle.py
```

The runner refuses an occupied port 17311 and never closes another Editor. It uses
argument arrays, removes stale output, and requires both a successful Editor exit
and complete evidence in `runtime-lifecycle.json`. The outer timeout defaults to
240 seconds (including import); the probe has its own 120-second deadline. Increase
`--timeout` for a cold import. Inspect `runtime-lifecycle.log` if it fails.

The probe enters Play Mode, activates readings and both overlays, requests an actual
Unity domain reload, and verifies a changed static token, a rebuilt PlayApi and open
transport. It requires collection and overlays to remain active until the 15-second
boundary (100 ms allowance between Editor and runtime updates), then requires shutdown
within five seconds. It waits for at least 15 seconds of actual inactivity, checks that readings,
metrics and visible overlays stop, then verifies status wakes metrics only and readings
wakes readings without overlays. It calls the existing activity handler via reflection:
this is lifecycle coverage, **not a WebSocket request/response integration test**.
It requests domain reload directly, not a source-file edit/recompilation. Private
field reflection is isolated to test code and may need updating after runtime refactors.

## Opt-in benchmark

Port 17311 must also be free for this test: SDK auto-spawn opens the transport before
the test starts. The benchmark's own host receives a controlled transport before Start.
Do not run it alongside a
playing game. A socket conflict is a failed run, not a usable measurement.

```sh
/path/to/Unity -batchmode -nographics -runTests -testPlatform PlayMode \
  -testFilter UnityPlayMcp.Tests.RuntimePerformanceTests.ReportsUnconnectedActiveAndIdleCosts \
  -projectPath /tmp/unity-play-mcp-test \
  -testResults /tmp/unity-play-mcp-test/performance-tests.xml \
  -logFile /tmp/unity-play-mcp-test/performance.log
```

Read `runtime-performance.json` together with the NUnit XML; a leftover JSON file
alone does not prove the latest run passed. Each state has 30 warmup frames and 120
requested sample frames. The JSON reports actual samples, CPU mean/p50/p95 in
nanoseconds, main-thread GC bytes per recorded frame, and environment metadata.
No machine-dependent timing threshold is asserted. State assertions must pass;
missing profiler data fails rather than becoming zero.

The three states are unconnected, active readings/UI, and connected-but-idle after
the 15-second boundary (the benchmark advances the idle check directly; the lifecycle
probe covers real elapsed time). A controlled transport excludes network traffic and
other clients. CPU covers **Host.Update only**, not the SDK's separate UI/reading
callbacks or transport threads. GC covers the **whole main thread**, including test
and Editor work, not SDK-only allocations. These numbers are comparison evidence,
not total SDK overhead and not a claim of zero cost. Compare repeated runs on the
same machine/editor/project; use the Unity Profiler on a representative game for
end-to-end costs before setting a performance budget.

The benchmark uses Unity's [ProfilerRecorder](https://docs.unity.cn/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.StartNew.html)
with per-frame summation and current-thread collection.
