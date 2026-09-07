namespace UnityPlayMcp.Tests.Fixtures.NoInput
{
    /// <summary>
    /// An assembly that references UnityPlayMcp.Runtime in its asmdef but calls no `Input` method.
    /// </summary>
    /// <remarks>
    /// This is the other half of issue #47. The weaver now adds the `UnityPlayMcp.Runtime` assembly
    /// reference itself, so something has to pin the rule that it only does so when it has a call to
    /// rewrite. Without that rule every assembly in a project picks up a reference it never uses.
    ///
    /// The asmdef reference to `UnityPlayMcp.Runtime` is load-bearing, not decoration: `WillProcess`
    /// only lets an assembly through when the runtime dll is in its compiler references, and an
    /// assembly defined by an asmdef gets only what its `references` list names. Drop it and the
    /// postprocessor never runs here, leaving a test that proves nothing.
    /// </remarks>
    public sealed class NoInputFixture
    {
        public int ReadNothing()
        {
            return 0;
        }
    }
}
