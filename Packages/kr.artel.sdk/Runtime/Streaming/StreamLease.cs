using System;

namespace Artel.Streaming
{
    /// <summary>
    /// The dead-man timer for one watching session.
    ///
    /// It lives in the SDK rather than being driven by the server on purpose. A clean disconnect
    /// is not the case worth designing for — a closed laptop lid, a killed browser, or the
    /// orchestration server going down all leave the game encoding video for nobody, and none of
    /// them send anything. Stopping because renewals stopped arriving is the only version of
    /// "stop when nobody is watching" that does not depend on being told.
    /// </summary>
    internal sealed class StreamLease
    {
        private readonly float durationSeconds;
        private float deadline;

        public StreamLease(float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds), "Lease duration must be greater than zero.");
            }

            this.durationSeconds = durationSeconds;
        }

        public void Renew(float currentTime)
        {
            deadline = currentTime + durationSeconds;
        }

        public bool HasExpired(float currentTime)
        {
            return currentTime >= deadline;
        }
    }
}
