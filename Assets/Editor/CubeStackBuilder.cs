using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CubeStackBuilder {
    // CLI entry point (batchmode -executeMethod): build, verify, exit.
    public static void Perform () {
        try {
            Build();
            EditorApplication.Exit(0);
        } catch (Exception e) {
            Debug.LogException(e);
            EditorApplication.Exit(3);
        }
    }

    // Core logic, also callable from the live editor (no Exit here).
    // Builds Assets/prefabs/CubeStack.prefab: empty parent + 64 cubes in a 4x4x4 grid.
    // Each cube is 0.125 on a side, so the whole stack measures 0.5 x 0.5 x 0.5.
    // Then instantiates the prefab in the scene at position (0, 0, 0) and saves it.
    public static void Build () {
        try {
            const int    n        = 4;      // 4 x 4 x 4 = 64 cubes
            const float  cubeSize = 0.125f; // 4 * 0.125 = 0.5 total edge length
            const string folder     = "Assets/prefabs";
            const string prefabPath = "Assets/prefabs/CubeStack.prefab";
            const string scenePath  = "Assets/Scenes/SampleScene.unity";

            // 1) Create the prefabs folder if it doesn't exist yet
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "prefabs");

            // 2) Scene: in batchmode open SampleScene fresh; in the live editor use
            //    the scene the user currently has open (avoids save-prompt dialogs)
            Scene scene = Application.isBatchMode
                ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single)
                : SceneManager.GetActiveScene();

            // 3) Remove a leftover CubeStack root from a previous run (idempotent)
            GameObject old = GameObject.Find("CubeStack");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);

            // 4) Build the hierarchy: empty parent with 64 cube children, stack centered on the parent
            GameObject parent = new GameObject("CubeStack");
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    for (int z = 0; z < n; z++) {
                        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        c.name = "Cube_" + x + "_" + y + "_" + z;
                        c.transform.SetParent(parent.transform, false);
                        c.transform.localPosition = new Vector3(
                            (x - (n - 1) * 0.5f) * cubeSize,
                            (y - (n - 1) * 0.5f) * cubeSize,
                            (z - (n - 1) * 0.5f) * cubeSize);
                        c.transform.localScale = new Vector3(cubeSize, cubeSize, cubeSize);
                    }

            if (parent.transform.childCount != 64)
                throw new Exception("Expected 64 cubes, got " + parent.transform.childCount);

            // 5) Save the hierarchy as a prefab, then drop the temporary scene copy
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(parent, prefabPath);
            if (prefab == null)
                throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
            UnityEngine.Object.DestroyImmediate(parent);

            // 6) Place a prefab instance in the scene at (0, 0, 0)
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            if (instance == null)
                throw new Exception("InstantiatePrefab failed");
            instance.transform.position = Vector3.zero;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new Exception("SaveScene failed for " + scene.path);

            // 7) Verify everything actually landed before exiting clean
            if (prefab.transform.childCount != 64)
                throw new Exception("prefab childCount != 64");
            if (!instance.scene.IsValid())
                throw new Exception("instance is not in a scene");
            if (!PrefabUtility.IsPartOfPrefabInstance(instance))
                throw new Exception("scene object is not a prefab instance");
            if (instance.transform.position != Vector3.zero)
                throw new Exception("instance not at (0, 0, 0)");

            Debug.Log("[CubeStackBuilder] OK prefab=" + prefabPath
                + " cubes=64 totalSize=0.5 instance=CubeStack@(0,0,0) scene=" + scene.path);
        } catch (Exception e) {
            Debug.LogException(e);
            throw;
        }
    }
}
