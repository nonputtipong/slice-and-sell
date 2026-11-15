using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.XR.CoreUtils.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Headless XR project validation and Quest APK build.
// Batch mode: -executeMethod BuildApk.Validate  /  -executeMethod BuildApk.Build [-development]
public static class BuildApk
{
    const string StallScene = "Assets/_Project/Scenes/Stall.unity";
    const string AppId = "com.cmkl.hcd545.sliceandsell";
    const string OutputPath = "Builds/slice-and-sell.apk";

    [MenuItem("Slice & Sell/Validate Android")]
    public static void Validate()
    {
        // Scene rules (XRI samples) look at the open scenes; batch mode starts with an empty one.
        if (Application.isBatchMode && File.Exists(StallScene)) EditorSceneManager.OpenScene(StallScene);

        // BuildValidator.GetCurrentValidationIssues is internal in XR Core Utils 2.6.0, so call it by reflection.
        // It only evaluates rules; it never runs FixIt.
        var failures = new HashSet<BuildValidationRule>();
        var check = typeof(BuildValidator).GetMethod("GetCurrentValidationIssues", BindingFlags.Static | BindingFlags.NonPublic);
        if (check == null)
        {
            Debug.LogError("[BuildApk] BuildValidator.GetCurrentValidationIssues not found (XR Core Utils changed?); open Project Validation in the editor instead.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        check.Invoke(null, new object[] { failures, BuildTargetGroup.Android });

        foreach (var rule in failures.OrderByDescending(r => r.Error))
            Debug.Log($"[BuildApk] {(rule.Error ? "ERROR" : "WARNING")} [{rule.Category}] {rule.Message}");
        int errors = failures.Count(r => r.Error);
        Debug.Log($"[BuildApk] Validation: {errors} errors, {failures.Count - errors} warnings");

        if (Application.isBatchMode) EditorApplication.Exit(errors > 0 ? 1 : 0);
    }

    [MenuItem("Slice & Sell/Build APK")]
    public static void Build()
    {
        bool ok = false;
        try { ok = DoBuild(); }
        catch (Exception e) { Debug.LogError("[BuildApk] " + e); }
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool DoBuild()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            return Fail("could not switch the active build target to Android");

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0 || scenes[0] != StallScene)
            return Fail($"first enabled scene must be {StallScene}, got '{scenes.FirstOrDefault()}'");
        var id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (id != AppId)
            return Fail($"Android application identifier must be {AppId}, got '{id}'");

        bool dev = Environment.GetCommandLineArgs().Contains("-development");
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = dev ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
        };

        var clock = Stopwatch.StartNew();
        var summary = BuildPipeline.BuildPlayer(options).summary;
        clock.Stop();
        if (summary.result != BuildResult.Succeeded)
            return Fail($"build {summary.result} with {summary.totalErrors} errors after {clock.Elapsed.TotalSeconds:F0} s");

        // summary.totalSize is the player data size; the file on disk is what gets sideloaded.
        double mb = new FileInfo(OutputPath).Length / (1024.0 * 1024.0);
        Debug.Log($"[BuildApk] Succeeded ({(dev ? "development" : "release")}): {Path.GetFullPath(OutputPath)}, " +
                  $"{mb:F1} MB, {clock.Elapsed.TotalSeconds:F0} s");
        return true;
    }

    static bool Fail(string message)
    {
        Debug.LogError("[BuildApk] Build failed: " + message);
        return false;
    }
}
