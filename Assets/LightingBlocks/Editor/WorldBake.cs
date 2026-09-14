using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightingBlocks.Editor
{
    // WorldDressing rebuilds the courts every time the game starts, which keeps the scene
    // honest at runtime but leaves the Scene view showing whatever was last baked. This writes the
    // same meshes and the same neon paving to disk so the editor shows the world you actually play.
    [InitializeOnLoad]
    public static class WorldBake
    {
        const string Art = "Assets/LightingBlocks/Art/Integrated";
        const string Pack = "Assets/LightingBlocks/Art/Pack/Materials/M_ENV_Paving.mat";
        const string Request = "Library/LightingBlocks.bake-request";

        // Same trigger the verification harness uses: writing the request file makes an already
        // open editor do the work, so baking never depends on anyone finding the menu item.
        static WorldBake() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Request)) return;
            File.Delete(Request);
            try { Bake(); }
            catch (Exception e) { File.WriteAllText("Library/LightingBlocks.bake-result.txt", "FAIL: " + e.Message); Debug.LogError(e); }
        }

        [MenuItem("Tools/Lighting Blocks/Bake Immersive World")]
        public static void Bake()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play before baking the world.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            Directory.CreateDirectory(Art); AssetDatabase.Refresh();
            // This saves SampleScene, so keep one copy of what it looked like beforehand.
            const string backup = "Assets/Scenes/SampleScene_BeforeBake.unity";
            if (!File.Exists(backup)) EditorSceneManager.SaveScene(scene, backup, true);

            var paving = Paving();
            int shores = Landscape.Shores.Length;
            for (int i = 0; i < shores; i++)
            {
                var points = Landscape.Shores[i];
                Bind("ShoreFloor" + i, WorldDressing.FloorMesh(points, i), paving);
                Bind("ShoreCliff" + i, WorldDressing.CliffMesh(points, i), null);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var bounds = WorldDressing.CourtBounds();
            File.WriteAllText("Library/LightingBlocks.bake-result.txt",
                "SUCCESS: baked " + shores + " courts spanning " + bounds.size.x.ToString("0") + " x " + bounds.size.z.ToString("0") + " metres, neon paving and cliffs, into SampleScene.");
        }

        // Overwrite the existing mesh asset in place rather than creating a new one: every
        // MeshFilter and MeshCollider in the scene already points at it, and repointing them is
        // how a bake ends up with half the scene on old geometry.
        static void Bind(string name, Mesh mesh, Material material)
        {
            string path = Art + "/" + name + ".asset";
            var prior = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (prior == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, prior); UnityEngine.Object.DestroyImmediate(mesh); }
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (filter.name != name) continue;
                filter.sharedMesh = asset;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider != null) collider.sharedMesh = asset;
                if (material != null) filter.GetComponent<Renderer>().sharedMaterial = material;
                EditorUtility.SetDirty(filter);
            }
        }

        // The neon emission texture and the material that carries it, as real assets. The pixels
        // come from the same WorldDressing.Circuit() the runtime uses, so the baked scene and
        // Play mode cannot drift apart.
        static Material Paving()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Pack);
            if (material == null) throw new Exception("Import the supplied art pack first.");
            return material;
        }
    }
}
