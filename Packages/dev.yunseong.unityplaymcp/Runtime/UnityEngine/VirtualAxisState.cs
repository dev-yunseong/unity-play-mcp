using System;
using System.Collections.Generic;

namespace UnityPlayMcp
{
    /// <summary>
    /// The axis values the agent is holding, keyed by Input Manager axis name. The legacy Input
    /// Manager has no runtime API for axis-to-key bindings, so a virtual key press cannot reach
    /// <c>GetAxis</c>.
    /// </summary>
    /// <remarks>
    /// In Unity a button is an axis entry read through both <c>GetButton</c> and <c>GetAxis</c>,
    /// so one store serves both. A positive value means the button is down.
    /// </remarks>
    internal sealed class VirtualAxisState
    {
        private readonly Dictionary<string, AxisHold> holds =
            new Dictionary<string, AxisHold>(StringComparer.Ordinal);

        /// <summary>
        /// Holds an axis until <see cref="Release"/>. Re-setting a held axis keeps its start frame,
        /// so <see cref="GetButtonDown"/> does not fire twice.
        /// </summary>
        public void Set(string axisName, float value, int currentFrame)
        {
            if (string.IsNullOrEmpty(axisName))
            {
                return;
            }

            if (holds.TryGetValue(axisName, out var held) && !held.ReleaseFrame.HasValue)
            {
                held.Value = value;
                return;
            }

            // Starts next frame, like the virtual keyboard and mouse, so a consumer polling in
            // Update does not miss it due to script execution order.
            holds[axisName] = new AxisHold(value, currentFrame + 1);
        }

        public void Release(string axisName, int currentFrame)
        {
            if (axisName != null && holds.TryGetValue(axisName, out var held))
            {
                Release(held, currentFrame);
            }
        }

        public void ReleaseAll(int currentFrame)
        {
            foreach (var held in holds.Values)
            {
                Release(held, currentFrame);
            }
        }

        /// <summary>
        /// False when the agent is not driving this axis, so the proxy falls back to the real value.
        /// </summary>
        public bool TryGetValue(string axisName, int frame, out float value)
        {
            var held = HoldOn(axisName, frame);
            value = held == null ? 0f : held.Value;
            return held != null;
        }

        public bool GetButton(string axisName, int frame)
        {
            var held = HoldOn(axisName, frame);
            return held != null && held.Value > 0f;
        }

        public bool GetButtonDown(string axisName, int frame)
        {
            var held = HoldOn(axisName, frame);
            return held != null && held.StartFrame == frame && held.Value > 0f;
        }

        public bool GetButtonUp(string axisName, int frame)
        {
            return axisName != null &&
                   holds.TryGetValue(axisName, out var held) &&
                   held.ReleaseFrame == frame &&
                   held.Value > 0f;
        }

        /// <summary>
        /// Drops holds whose up edge has passed. Runs every frame, so the list is allocated only when needed.
        /// </summary>
        public void Refresh(int frame)
        {
            List<string> expiredAxes = null;
            foreach (var pair in holds)
            {
                if (pair.Value.ReleaseFrame.HasValue && pair.Value.ReleaseFrame.Value < frame)
                {
                    expiredAxes = expiredAxes ?? new List<string>();
                    expiredAxes.Add(pair.Key);
                }
            }

            if (expiredAxes == null)
            {
                return;
            }

            foreach (var axisName in expiredAxes)
            {
                holds.Remove(axisName);
            }
        }

        public void Clear()
        {
            holds.Clear();
        }

        private static void Release(AxisHold held, int currentFrame)
        {
            if (held.ReleaseFrame.HasValue)
            {
                return;
            }

            held.ReleaseFrame = currentFrame + 1;
        }

        /// <summary>
        /// The hold in force on this frame (from its start frame until its release frame), or null.
        /// </summary>
        /// <remarks>
        /// Button edges come from hold start and release, not from the value crossing zero. Moving
        /// from 1 to -1 via <c>Set</c> reports no <see cref="GetButtonUp"/>.
        /// </remarks>
        private AxisHold HoldOn(string axisName, int frame)
        {
            if (axisName == null || !holds.TryGetValue(axisName, out var held))
            {
                return null;
            }

            if (frame < held.StartFrame)
            {
                return null;
            }

            return !held.ReleaseFrame.HasValue || frame < held.ReleaseFrame.Value ? held : null;
        }

        private sealed class AxisHold
        {
            public AxisHold(float value, int startFrame)
            {
                Value = value;
                StartFrame = startFrame;
            }

            public float Value { get; set; }

            public int StartFrame { get; }

            public int? ReleaseFrame { get; set; }
        }
    }
}
