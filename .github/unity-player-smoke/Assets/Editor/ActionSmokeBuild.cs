using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ActionSmokeBuild
{
    public static void Build()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneWindows64)
            throw new InvalidOperationException("The action smoke build requires Linux or Windows x64.");

        var args = Environment.GetCommandLineArgs();
        var pathIndex = Array.IndexOf(args, "-customBuildPath");
        if (pathIndex < 0 || pathIndex + 1 >= args.Length)
            throw new ArgumentException("Pass -customBuildPath with the player output path.");

        var extension = target == BuildTarget.StandaloneLinux64 ? ".x86_64" : ".exe";
        var output = Path.GetFullPath(Path.ChangeExtension(args[pathIndex + 1], extension));
        if (File.Exists(output))
            throw new InvalidOperationException("Use a fresh action smoke build output path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        const string scenePath = "Assets/ActionSmoke.unity";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Game code").AddComponent<ActionSmokeScene>();
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new InvalidOperationException("Could not save the action smoke scene.");

        var standalone = NamedBuildTarget.Standalone;
        PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.High);
        if (PlayerSettings.GetScriptingBackend(standalone) != ScriptingImplementation.IL2CPP
            || PlayerSettings.GetManagedStrippingLevel(standalone) != ManagedStrippingLevel.High)
            throw new InvalidOperationException("The action smoke build requires IL2CPP and High stripping.");

        var report = BuildPipeline.BuildPlayer(new[] { scenePath }, output, target, BuildOptions.Development);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Action smoke build failed: " + report.summary.result);
    }
}
