namespace UnityPlayMcp.Tests.Fixtures.NoInput
{
    /// <summary>
    /// An assembly that references UnityPlayMcp.Runtime in its asmdef but calls no `Input` method.
    /// </summary>
    /// <remarks>
    /// Pins the rule that the weaver adds the `UnityPlayMcp.Runtime` reference only when it rewrites
    /// a call (#47); otherwise every assembly picks up an unused reference.
    ///
    /// The asmdef reference is required: `WillProcess` only processes an assembly whose compiler
    /// references include the runtime dll. Without it this test proves nothing.
    /// </remarks>
    public sealed class NoInputFixture
    {
        public int ReadNothing()
        {
            return 0;
        }
    }
}
