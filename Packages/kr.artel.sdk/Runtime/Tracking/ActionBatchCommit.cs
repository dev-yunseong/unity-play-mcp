namespace Artel.Tracking
{
    internal sealed class ActionBatchCommit
    {
        private readonly ActionInvocationBuffer buffer;
        private readonly long watermark;

        public ActionBatchCommit(ActionInvocationBuffer buffer, long watermark)
        {
            this.buffer = buffer;
            this.watermark = watermark;
        }

        public void Commit()
        {
            buffer.Commit(watermark);
        }
    }
}
