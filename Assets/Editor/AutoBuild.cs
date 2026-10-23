using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AutoBuild {
    public static void Perform () {
        try {
            var options = new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Build/test.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer (options);
            var s = report.summary;
            Debug.Log ("[AutoBuild] result=" + s.result + " errors=" + s.totalErrors + " warnings=" + s.totalWarnings + " size=" + s.totalSize + " output=" + s.outputPath);
            if (s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) { EditorApplication.Exit (0); }
            else { Debug.LogError ("[AutoBuild] build FAILED, see log"); EditorApplication.Exit (2); }
        } catch (Exception e) {
            Debug.LogException (e); EditorApplication.Exit (3);
        }
    }
}
