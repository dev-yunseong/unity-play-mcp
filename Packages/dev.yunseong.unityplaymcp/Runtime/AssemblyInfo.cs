using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime.Tests")]

// Awake, OnEnable, DontDestroyOnLoad run only in play mode, so live components are tested there.
[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime.PlayModeTests")]
