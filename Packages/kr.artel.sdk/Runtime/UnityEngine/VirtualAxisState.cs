using System;
using System.Collections.Generic;

namespace Artel
{
    /// <summary>
    /// The axis values the agent is holding, keyed by Input Manager axis name. The legacy Input
    /// Manager exposes no runtime API for its axis-to-key bindings, so a virtual key press cannot
    /// reach <c>GetAxis</c>. The agent names the axis and states the value instead.
    /// </summary>
    /// <remarks>
    /// A button is an axis in Unity: <c>Jump</c> is an axis entry whose positive button is a key,
    /// and the same entry reads as a bool through <c>GetButton</c> and as a float through
    /// <c>GetAxis</c>. One store therefore serves both, and a positive value is what "the button is
    /// down" means here.
    /// </remarks>
    internal sealed class VirtualAxisState
    {
        private readonly Dictionary<string, AxisHold> holds =
            new Dictionary<string, AxisHold>(StringComparer.Ordinal);

        /// <summary>
        /// Holds an axis until <see cref="Release"/> asks for it back. Setting an axis that is
        /// already held changes the value and keeps the original start frame, so a caller repeating
        /// the same request does not make <see cref="GetButtonDown"/> fire a second time.
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

            // The frame after the request, matching the virtual keyboard and mouse: the action is
            // handled in the manager's Update, and a consumer polling in its own Update must not
            // miss it because of script execution order.
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
        /// False when the agent is not driving this axis, which is what lets the proxy fall through
        /// to the real value.
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
        /// Drops holds whose up edge has already been read. The list is allocated only once
        /// something has actually expired, because this runs on every frame and an empty list per
        /// frame is garbage nobody asked for.
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
        /// The hold in force on this frame, or null. A hold scheduled for a later frame is not in
        /// force yet, and a release scheduled for a later frame leaves it in force until then.
        /// </summary>
        /// <remarks>
        /// Button edges come from the hold starting and being released, not from the value crossing
        /// zero. Driving an axis from 1 to -1 through <c>Set</c> leaves <see cref="GetButton"/>
        /// correct — it reads false — but reports no <see cref="GetButtonUp"/> for the crossing.
        /// Button-shaped callers hold and release, which does produce both edges.
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
