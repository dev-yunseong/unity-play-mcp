using System;
using UnityPlayMcp.Affordances.Scan;
using UnityPlayMcp.Capture;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp
{
    internal sealed class ActionContext
    {
        public ActionContext(
            TargetLookup targetLookup, CursorController cursorController, PointerEventDispatcher pointerEvents,
            IScreenCapturer capturer, IReadingChannel readings, Action<bool> setOverlay)
        {
            TargetLookup = targetLookup;
            CursorController = cursorController;
            PointerEvents = pointerEvents;
            Capturer = capturer;
            Readings = readings;
            SetOverlay = setOverlay;

            var startupScene = SceneManager.GetActiveScene();
            StartupSceneBuildIndex = startupScene.buildIndex;
            StartupScenePath = startupScene.path;

            CursorMoved = VirtualInput.MoveMouse;
            PointerMoved = position =>
            {
                VirtualInput.MoveMouse(position);
                pointerEvents.MoveTo(position);
            };
        }

        public TargetLookup TargetLookup { get; }
        public CursorController CursorController { get; }
        public PointerEventDispatcher PointerEvents { get; }
        public IScreenCapturer Capturer { get; }
        public IReadingChannel Readings { get; }
        public Action<bool> SetOverlay { get; }
        public Action<Vector2> CursorMoved { get; }
        public Action<Vector2> PointerMoved { get; }
        public int StartupSceneBuildIndex { get; }
        public string StartupScenePath { get; }
        public float? ScaleBeforePause { get; set; }

        // 키 입력의 Mouse0과 mouse_down이 같은 마우스 상태와 uGUI 이벤트를 갱신해야 한다.
        public void SetMouseButton(int button, bool press)
        {
            if (press)
            {
                VirtualInput.PressMouseButton(button);
                PointerEvents.Press(button);
            }
            else
            {
                VirtualInput.ReleaseMouseButton(button);
                PointerEvents.Release(button);
            }
        }

        public void RestoreTimeScale()
        {
            if (ScaleBeforePause.HasValue)
            {
                Time.timeScale = ScaleBeforePause.Value;
                ScaleBeforePause = null;
            }
        }
    }
}
