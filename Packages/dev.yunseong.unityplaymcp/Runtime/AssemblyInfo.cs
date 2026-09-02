using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime.Tests")]

// A second test assembly because Unity does not run Awake, OnEnable, or DontDestroyOnLoad outside
// play mode. Anything that drives a live component — the pointer dispatcher, the cursor, the action
// queue on a real manager — can only be exercised there.
[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime.PlayModeTests")]
