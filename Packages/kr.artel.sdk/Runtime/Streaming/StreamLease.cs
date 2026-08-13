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
        /// <summary>
        /// The most elapsed time one frame may charge against the lease.
        ///
        /// A gap wider than this was not the game waiting for a renewal that never came — the
        /// process was not running. A backgrounded mobile app is frozen outright by the OS, and a
        /// window the platform stops stepping is no different; either way no renewal could be read
        /// no matter how present the viewer was. Charging that wall-clock gap expires the lease on
        /// the very frame the app comes back, which is the one moment the viewer is certainly
        /// still there.
        ///
        /// Capping the frame rather than forgiving the gap outright is what keeps the timer alive
        /// on a game that is merely running slowly. A forgiven gap would never expire below one
        /// frame per second; a capped one keeps charging, so the lease still runs out — later than
        /// the wall clock says once frames are longer than the cap, but it runs out.
        /// </summary>
        private const float MaxCountedFrameSeconds = 1f;

        private readonly float durationSeconds;
        private float remainingSeconds;
        private float lastSampleTime;

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
            remainingSeconds = durationSeconds;
            lastSampleTime = currentTime;
        }

        /// <summary>
        /// Sampled once per frame from <see cref="ArtelStreamHost.Tick"/>, and consumes the time
        /// since the previous sample. It is the frame loop that spends the lease, so nothing else
        /// may call this — a second caller in the same frame would spend it twice.
        /// </summary>
        public bool HasExpired(float currentTime)
        {
            var elapsed = Math.Min(
                Math.Max(currentTime - lastSampleTime, 0f), MaxCountedFrameSeconds);

            lastSampleTime = currentTime;
            remainingSeconds -= elapsed;

            return remainingSeconds <= 0f;
        }
    }
}
