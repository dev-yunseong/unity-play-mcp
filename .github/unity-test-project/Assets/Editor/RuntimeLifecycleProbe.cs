using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityPlayMcp;

// 별도 batch Editor에서만 실행한다. SessionState가 실제 domain reload 사이의 진행 상태를 보존한다.
[InitializeOnLoad]
public static class RuntimeLifecycleProbe
{
    private const string Key = "UnityPlayMcp.RuntimeLifecycleProbe.";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string DomainToken = Guid.NewGuid().ToString("N");
    private static int frames;

    [Serializable]
    private sealed class Result
    {
        public bool passed;
        public string detail;
        public string unityVersion;
        public bool domainReloaded;
        public double idleSeconds;
        public bool statusOnlyWake;
        public bool readingsWake;
    }

    static RuntimeLifecycleProbe() { EditorApplication.update += Tick; }

    public static void Begin()
    {
        SessionState.SetInt(Key + "phase", 1);
        SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddMinutes(2).ToString("O"));
        SessionState.SetString(Key + "token", DomainToken);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        var phase = SessionState.GetInt(Key + "phase", 0);
        if (phase == 0) return;
        try
        {
            Require(DateTime.UtcNow < DateTime.Parse(SessionState.GetString(Key + "deadline", "")), "Probe timed out");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var host = UnityEngine.Object.FindObjectOfType<UnityPlayMcpHost>();
            if (host == null || ++frames < 10) return;
            if (phase == 1)
            {
                Require(Flag(host, "TransportOpen"), "Transport unavailable; check port 17311");
                host.StartReadings();
                host.SetOverlay(true);
                // Play Mode 진입 자체도 domain을 바꾸므로 검사 직전 token을 저장한다.
                SessionState.SetString(Key + "token", DomainToken);
                SessionState.SetInt(Key + "phase", 2);
                EditorUtility.RequestScriptReload();
                return;
            }
            if (phase == 2)
            {
                if (SessionState.GetString(Key + "token", "") == DomainToken) return;
                Require(Field(host, "playApi") != null, "PlayApi not rebuilt after domain reload");
                Require(Flag(host, "TransportOpen"), "Transport not reopened after domain reload");
                Require(host.StartReadings(), "Readings did not restart after domain reload");
                host.SetOverlay(true);
                RequireActive(host);
                SessionState.SetInt(Key + "overlayFrame", Time.frameCount);
                SessionState.SetInt(Key + "phase", 3);
                return;
            }
            // reload 전 canvas의 Destroy는 프레임 끝에 완료된다.
            if (Time.frameCount <= SessionState.GetInt(Key + "overlayFrame", 0)) return;
            Require(host.GetComponentsInChildren<Canvas>(true).Length == 2, "Duplicate or missing overlays");
            var elapsed = Time.realtimeSinceStartupAsDouble - (double)Field(host, "lastRequestTime");
            // Editor update와 runtime Update의 경계 차이만 100ms 허용한다.
            if (elapsed < 14.9d) RequireActive(host);
            if (elapsed < 15d) return;
            Require(elapsed < 20d, "Idle did not suspend within five seconds of the 15-second boundary");
            if (!(bool)Field(host, "idleSuspended")) return;
            Require(!Flag(host, "Reading") && !(bool)Field(host, "collectingPerformance"), "Idle left collection active");
            Require(host.GetComponentsInChildren<Canvas>().Length == 0, "Idle left overlays visible");
            Require(Flag(host, "TransportOpen"), "Idle closed transport");
            // handler를 직접 호출하므로 wire 전달 검사가 아닌 선택적 재활성화 검사다.
            Activity(host, "status");
            Require((bool)Field(host, "collectingPerformance") && !Flag(host, "Reading"), "Status woke readings or failed to wake metrics");
            Require(host.GetComponentsInChildren<Canvas>().Length == 0, "Status woke overlays");
            Activity(host, "readings");
            Require(Flag(host, "Reading"), "Readings request did not wake readings");
            Require(host.GetComponentsInChildren<Canvas>().Length == 0, "Readings woke overlays");
            Finish(new Result { passed = true, detail = "Actual domain reload, elapsed idle and selective handler wake passed", domainReloaded = true, idleSeconds = elapsed, statusOnlyWake = true, readingsWake = true });
        }
        catch (Exception error) { Finish(new Result { detail = error.ToString() }); }
    }

    private static object Field(UnityPlayMcpHost host, string name) => typeof(UnityPlayMcpHost).GetField(name, Private).GetValue(host);
    private static void RequireActive(UnityPlayMcpHost host)
    {
        Require(Flag(host, "Reading") && (bool)Field(host, "collectingPerformance"), "Collection stopped before the idle boundary");
        Require(host.GetComponentsInChildren<Canvas>().Length == 2, "Overlays stopped before the idle boundary");
    }
    private static bool Flag(UnityPlayMcpHost host, string name) => (bool)typeof(UnityPlayMcpHost).GetProperty(name, Private).GetValue(host);
    private static void Activity(UnityPlayMcpHost host, string intent) => typeof(UnityPlayMcpHost).GetMethod("RuntimeActivity", Private).Invoke(host, new object[] { 1, new List<object> { intent } });
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }

    private static void Finish(Result result)
    {
        SessionState.SetInt(Key + "phase", 0);
        result.unityVersion = Application.unityVersion;
        var path = Path.Combine(Application.dataPath, "../runtime-lifecycle.json");
        File.WriteAllText(path, JsonUtility.ToJson(result, true));
        EditorApplication.Exit(result.passed ? 0 : 1);
    }
}
