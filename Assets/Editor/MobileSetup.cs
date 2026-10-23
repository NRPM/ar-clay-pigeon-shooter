using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MobileSetup {
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    // CLI entry point (batchmode -executeMethod MobileSetup.Perform)
    public static void Perform () {
        try {
            Apply();
            EditorApplication.Exit(0);
        } catch (Exception e) {
            Debug.LogException(e);
            EditorApplication.Exit(3);
        }
    }

    // Core logic, also callable from the live editor (no Exit here).
    public static void Apply () {
        // --- 1) Portrait-only orientation + 1080x2160 target resolution ---
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;

        // defaultScreenWidth/Height have no public API in Unity 6 -> SerializedObject
        var settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settingsAssets.Length > 0) {
            var so = new SerializedObject(settingsAssets[0]);
            var wProp = so.FindProperty("defaultScreenWidth");
            var hProp = so.FindProperty("defaultScreenHeight");
            if (wProp != null) wProp.intValue = 1080;
            if (hProp != null) hProp.intValue = 2160;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // --- 2) 1080x2160 portrait Game View preset in the editor (best effort) ---
        try {
            AddGameViewSize(1080, 2160, "Phone 1080x2160");
        } catch (Exception e) {
            Debug.LogWarning("[MobileSetup] Game View preset skipped: " + e.Message);
        }

        // --- 3) Attach GyroCameraController to the scene's main camera ---
        Scene scene = Application.isBatchMode
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : SceneManager.GetActiveScene();

        GameObject camGo = FindMainCamera(scene);
        if (camGo == null)
            throw new Exception("No camera found in scene " + scene.path);

        if (camGo.GetComponent<GyroCameraController>() == null)
            camGo.AddComponent<GyroCameraController>();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new Exception("SaveScene failed for " + scene.path);
        AssetDatabase.SaveAssets();

        if (camGo.GetComponent<GyroCameraController>() == null)
            throw new Exception("GyroCameraController missing after AddComponent");

        Debug.Log("[MobileSetup] OK orientation=Portrait resolution=1080x2160 gyroAttached=" + camGo.name + " scene=" + scene.path);
    }

    static GameObject FindMainCamera (Scene scene) {
        foreach (var root in scene.GetRootGameObjects()) {
            var cam = root.GetComponentInChildren<Camera>(true);
            if (cam != null) return cam.gameObject;
        }
        return null;
    }

    static void AddGameViewSize (int width, int height, string label) {
        var asm = typeof(Editor).Assembly;
        var sizesType = asm.GetType("UnityEditor.GameViewSizes");
        if (sizesType == null) throw new Exception("GameViewSizes type not found");

        PropertyInfo instanceProp = null;
        for (var t = sizesType; t != null && instanceProp == null; t = t.BaseType)
            instanceProp = t.GetProperty("instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var instance = instanceProp?.GetValue(null, null);
        if (instance == null) throw new Exception("GameViewSizes.instance not found");

        var group = sizesType
            .GetProperty("currentGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance, null);
        if (group == null) throw new Exception("currentGroup not found");

        var gvsType  = asm.GetType("UnityEditor.GameViewSize");
        var gvstType = asm.GetType("UnityEditor.GameViewSizeType");
        var fixedRes = Enum.Parse(gvstType, "FixedResolution");

        var gType   = group.GetType();
        var getSize = gType.GetMethod("GetGameViewSize");
        int total   = (int)gType.GetMethod("GetTotalCount").Invoke(group, null);
        var wProp = gvsType.GetProperty("width");
        var hProp = gvsType.GetProperty("height");
        for (int i = 0; i < total; i++) {
            var s = getSize.Invoke(group, new object[] { i });
            if ((int)wProp.GetValue(s, null) == width && (int)hProp.GetValue(s, null) == height)
                return; // preset already exists
        }

        var newSize = Activator.CreateInstance(gvsType, new object[] { fixedRes, width, height, label });
        gType.GetMethod("AddCustomSize").Invoke(group, new object[] { newSize });
    }
}
