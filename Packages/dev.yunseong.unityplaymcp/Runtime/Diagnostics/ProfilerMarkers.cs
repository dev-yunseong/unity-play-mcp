using Unity.Profiling;

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// The SDK's Profiler markers, in one place.
    /// </summary>
    /// <remarks>
    /// Separates SDK cost from game cost without Deep Profile. Markers compile away in
    /// non-development builds.
    ///
    /// Names follow <c>UnityPlayMcp.&lt;Subsystem&gt;.&lt;Operation&gt;</c>, keyed to the subsystem
    /// rather than the class, so numbers stay comparable when an implementation is replaced.
    ///
    /// One marker per component, not per field; per-field markers would flood the Profiler.
    /// </remarks>
    internal static class ProfilerMarkers
    {
        public static readonly ProfilerMarker HostUpdate = new ProfilerMarker("UnityPlayMcp.Host.Update");

        public static readonly ProfilerMarker HostPumpStreaming =
            new ProfilerMarker("UnityPlayMcp.Host.PumpStreaming");

        public static readonly ProfilerMarker HostHandleMessage =
            new ProfilerMarker("UnityPlayMcp.Host.HandleMessage");

        public static readonly ProfilerMarker HostPollSceneState =
            new ProfilerMarker("UnityPlayMcp.Host.PollSceneState");

        public static readonly ProfilerMarker HostPerformanceReport =
            new ProfilerMarker("UnityPlayMcp.Host.PerformanceReport");

        /// <summary>Walking the scene hierarchy and building the snapshot.</summary>
        public static readonly ProfilerMarker SceneScanScan = new ProfilerMarker("UnityPlayMcp.SceneScan.Scan");

        /// <summary>Lowering the snapshot to the wire DTO.</summary>
        public static readonly ProfilerMarker SceneScanMap = new ProfilerMarker("UnityPlayMcp.SceneScan.Map");

        /// <summary>Hashing the DTO to decide whether the state actually changed.</summary>
        public static readonly ProfilerMarker SceneScanHash = new ProfilerMarker("UnityPlayMcp.SceneScan.Hash");

        /// <summary>Measures member-reading work during a scene walk.</summary>
        public static readonly ProfilerMarker StateReadTagged = new ProfilerMarker("UnityPlayMcp.StateRead.Tagged");

        /// <summary>Reading and lowering the fields Unity itself would serialize.</summary>
        public static readonly ProfilerMarker StateReadSerializedFields =
            new ProfilerMarker("UnityPlayMcp.StateRead.SerializedFields");

        /// <summary>The synchronous GPU readback: <c>ReadPixels</c> plus <c>Apply</c>.</summary>
        public static readonly ProfilerMarker CaptureReadback = new ProfilerMarker("UnityPlayMcp.Capture.Readback");

        public static readonly ProfilerMarker CaptureEncode = new ProfilerMarker("UnityPlayMcp.Capture.Encode");

        /// <summary>
        /// Building the upload requests. Web request waits are excluded; spanning a yield counts idle time.
        /// </summary>
        public static readonly ProfilerMarker CaptureUpload = new ProfilerMarker("UnityPlayMcp.Capture.Upload");

        /// <summary>The per-frame screen grab that backs the video track.</summary>
        public static readonly ProfilerMarker StreamCaptureFrame =
            new ProfilerMarker("UnityPlayMcp.Stream.CaptureFrame");
    }
}
