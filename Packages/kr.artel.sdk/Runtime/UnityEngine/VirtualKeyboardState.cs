using System.Collections.Generic;
using global::UnityEngine;

namespace Artel
{
    internal sealed class VirtualKeyboardState
    {
        private readonly Dictionary<KeyCode, KeyClickState> clicks =
            new Dictionary<KeyCode, KeyClickState>();

        public void Click(KeyCode key, float durationSeconds, int currentFrame)
        {
            clicks[key] = new KeyClickState(currentFrame + 1, durationSeconds);
        }

        public bool GetKeyDown(KeyCode key, int frame, float time)
        {
            return TryGetState(key, frame, time, out var state) &&
                   state.StartFrame == frame &&
                   !state.ReleaseFrame.HasValue;
        }

        public bool GetKey(KeyCode key, int frame, float time)
        {
            return TryGetState(key, frame, time, out var state) &&
                   state.StartTime.HasValue &&
                   !state.ReleaseFrame.HasValue;
        }

        public bool GetKeyUp(KeyCode key, int frame, float time)
        {
            return TryGetState(key, frame, time, out var state) &&
                   state.ReleaseFrame == frame;
        }

        public bool AnyKey(int frame, float time)
        {
            Refresh(frame, time);
            foreach (var state in clicks.Values)
            {
                if (state.StartTime.HasValue && !state.ReleaseFrame.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        public bool AnyKeyDown(int frame, float time)
        {
            Refresh(frame, time);
            foreach (var state in clicks.Values)
            {
                if (state.StartFrame == frame && !state.ReleaseFrame.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(int frame, float time)
        {
            var expiredKeys = new List<KeyCode>();
            foreach (var pair in clicks)
            {
                Refresh(pair.Value, frame, time);
                if (pair.Value.ReleaseFrame.HasValue && pair.Value.ReleaseFrame.Value < frame)
                {
                    expiredKeys.Add(pair.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                clicks.Remove(key);
            }
        }

        public void Clear()
        {
            clicks.Clear();
        }

        private bool TryGetState(KeyCode key, int frame, float time, out KeyClickState state)
        {
            if (!clicks.TryGetValue(key, out state))
            {
                return false;
            }

            Refresh(state, frame, time);
            return true;
        }

        private static void Refresh(KeyClickState state, int frame, float time)
        {
            if (frame < state.StartFrame || state.ReleaseFrame.HasValue)
            {
                return;
            }

            if (!state.StartTime.HasValue)
            {
                state.StartTime = time;
            }

            if (time >= state.StartTime.Value + state.DurationSeconds)
            {
                state.ReleaseFrame = frame;
            }
        }

        private sealed class KeyClickState
        {
            public KeyClickState(int startFrame, float durationSeconds)
            {
                StartFrame = startFrame;
                DurationSeconds = durationSeconds;
            }

            public int StartFrame { get; }
            public float DurationSeconds { get; }
            public float? StartTime { get; set; }
            public int? ReleaseFrame { get; set; }
        }
    }
}
