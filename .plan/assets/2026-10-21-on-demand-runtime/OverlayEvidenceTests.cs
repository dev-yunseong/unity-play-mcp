using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    public sealed class OverlayEvidenceTests
    {
        [UnityTest]
        public IEnumerator RenderOverlayStates()
        {
            foreach (var previous in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
                Object.DestroyImmediate(previous.gameObject);
            var host = new GameObject("Overlay evidence fixture").AddComponent<UnityPlayMcpHost>();
            var camera = new GameObject("Evidence camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.15f, 0.18f, 0.23f);
            var render = new RenderTexture(960, 540, 24);
            camera.targetTexture = render;
            yield return null;
            var cursor = host.GetComponent<CursorController>();
            yield return cursor.MoveTo(new Vector2(420, 270), VirtualInput.MoveMouse);
            Save(camera, render, "default-hidden");
            host.SetOverlay(true);
            foreach (var canvas in host.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            Save(camera, render, "visible");
            host.SetOverlay(false);
            yield return null;
            Save(camera, render, "hidden-again");
            camera.targetTexture = null;
            Object.Destroy(host.gameObject);
            Object.Destroy(camera.gameObject);
            Object.Destroy(render);
        }

        private static void Save(Camera camera, RenderTexture render, string name)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = render;
            var texture = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
            texture.Apply();
            var path = System.Environment.GetEnvironmentVariable("UNITY_PLAY_MCP_EVIDENCE_DIR")
                ?? Path.Combine(Application.dataPath, "../OverlayEvidence");
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, name + ".png"), texture.EncodeToPNG());
            RenderTexture.active = previous;
            Object.Destroy(texture);
        }
    }
}
