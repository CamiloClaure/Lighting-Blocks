using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightingBlocks.Editor
{
    [InitializeOnLoad]
    public static class LastSparkSetup
    {
        const string Root = "Assets/LastSpark";
        static double next;
        static LastSparkSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            next = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists("Library/LastSpark.setup-request")) return;
            File.Delete("Library/LastSpark.setup-request");
            try { Build(); } catch (Exception e) { File.WriteAllText("Library/LastSpark.setup-result.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/Last Spark/Build MVP Scene")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new Exception("Open SampleScene before building the MVP.");
            const string backup = "Assets/Scenes/SampleScene_BeforeMysticalMVP.unity";
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Could not back up current scene.");
            var player = UnityEngine.Object.FindAnyObjectByType<BoxPlayer>();
            var camera = UnityEngine.Object.FindAnyObjectByType<StrategyCamera>();
            if (player == null || camera == null) throw new Exception("Existing player and camera required.");
            foreach (string name in new[] { "Environment", "Movable Boxes", "Controls", "The Last Spark - World", "Last Spark Systems" })
            {
                var previous = GameObject.Find(name); if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            }
            Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory(Root + "/Prefabs"); AssetDatabase.Refresh();
            var stone = Mat("Placeholder - Floating Basalt", new Color(.085f, .105f, .17f));
            var edge = Mat("Placeholder - Stone Edge", new Color(.19f, .23f, .32f));
            var star = Mat("Placeholder - Starlight", new Color(.18f, .67f, .95f), .7f);
            var life = Mat("Placeholder - Life", new Color(.23f, .95f, .55f), .6f);
            var ancient = Mat("Placeholder - Ancient Heart", new Color(.7f, .35f, .98f), .8f);
            var gold = Mat("Placeholder - Memory Gold", new Color(1, .66f, .22f), 1.2f);
            var dim = Mat("Placeholder - Dormant Rune", new Color(.25f, .35f, .52f), .3f);
            var black = Mat("Placeholder - Socket Center", new Color(.04f, .05f, .1f));
            var world = new GameObject("The Last Spark - World").transform;
            var home = new GameObject("01 - The Last Ember").transform; home.SetParent(world);
            var far = new GameObject("02 - Garden of Beginnings").transform; far.SetParent(world);
            Primitive("Placeholder Island A", PrimitiveType.Cube, new Vector3(0, -.65f, 0), new Vector3(24, 1.3f, 20), stone, home);
            Primitive("Placeholder Island B", PrimitiveType.Cube, new Vector3(0, -.65f, 28), new Vector3(20, 1.3f, 18), stone, far);
            // Visible curbs stop the player and fragments from slipping into the void.
            Wall("West Rim", new Vector3(-12, .25f, 0), new Vector3(.3f, .5f, 20), edge, home);
            Wall("East Rim", new Vector3(12, .25f, 0), new Vector3(.3f, .5f, 20), edge, home);
            Wall("South Rim", new Vector3(0, .25f, -10), new Vector3(24, .5f, .3f), edge, home);
            Wall("North Left Rim", new Vector3(-7.25f, .25f, 10), new Vector3(9.5f, .5f, .3f), edge, home);
            Wall("North Right Rim", new Vector3(7.25f, .25f, 10), new Vector3(9.5f, .5f, .3f), edge, home);
            Wall("Far West Rim", new Vector3(-10, .25f, 28), new Vector3(.3f, .5f, 18), edge, far);
            Wall("Far East Rim", new Vector3(10, .25f, 28), new Vector3(.3f, .5f, 18), edge, far);
            Wall("Far North Rim", new Vector3(0, .25f, 37), new Vector3(20, .5f, .3f), edge, far);
            Wall("Far South Left", new Vector3(-6.25f, .25f, 19), new Vector3(7.5f, .5f, .3f), edge, far);
            Wall("Far South Right", new Vector3(6.25f, .25f, 19), new Vector3(7.5f, .5f, .3f), edge, far);
            var bridge = new GameObject("Placeholder - Restored Starbridge"); bridge.transform.SetParent(world);
            Primitive("Walkable Light Path", PrimitiveType.Cube, new Vector3(0, -.18f, 14.5f), new Vector3(5, .36f, 9.2f), dim, bridge.transform);
            Wall("Bridge Left Rail", new Vector3(-2.5f, .3f, 14.5f), new Vector3(.2f, .6f, 9), gold, bridge.transform);
            Wall("Bridge Right Rail", new Vector3(2.5f, .3f, 14.5f), new Vector3(.2f, .6f, 9), gold, bridge.transform);
            var gate = Primitive("Placeholder - Sealed Bridge", PrimitiveType.Cube, new Vector3(0, .8f, 9.8f), new Vector3(5, 1.6f, .35f), ancient, world);
            bridge.SetActive(false);
            Text("RESTORE THE STARBRIDGE", new Vector3(0, 2.2f, 9.8f), world, .13f, Color.white);
            Text("THE LAST EMBER", new Vector3(-7, .035f, 7), home, .18f, new Color(.42f, .55f, .75f), true);
            Text("GARDEN OF BEGINNINGS", new Vector3(0, .035f, 35), far, .16f, new Color(.55f, .42f, .8f), true);
            // Silhouette placeholders suggest ruins without affecting the walkable space.
            for (int i = 0; i < 6; i++)
            {
                float x = i % 2 == 0 ? -10.5f : 10.5f, z = -7 + (i / 2) * 6;
                Primitive("Placeholder Broken Pillar " + i, PrimitiveType.Cylinder, new Vector3(x, .7f + i % 3 * .2f, z), new Vector3(.7f, .7f + i % 3 * .2f, .7f), edge, home);
            }
            var sockets = new GameObject("Altars").transform; sockets.SetParent(world);
            var altars = new AstralAltar[16];
            Vector3[] seats = { new Vector3(0,0,-.8f), new Vector3(3.2f,0,-.8f), new Vector3(-3.2f,0,-.8f), new Vector3(0,0,2.4f), new Vector3(3.2f,0,2.4f), new Vector3(-3.2f,0,2.4f), new Vector3(-3.2f,0,5.6f), new Vector3(3.2f,0,5.6f) };
            for (int i = 0; i < altars.Length; i++)
            {
                Vector3 p = i < 8 ? seats[i] : new Vector3(-4.8f + (i - 8) % 4 * 3.2f, 0, 24 + (i - 8) / 4 * 3.2f);
                var altarObject = new GameObject("Altar " + (i + 1).ToString("00")); altarObject.transform.SetParent(sockets); altarObject.transform.position = p;
                var a = altarObject.AddComponent<AstralAltar>(); a.id = "altar-" + i; a.outerIsland = i >= 8;
                a.ring = Primitive("Placeholder Rune Ring", PrimitiveType.Cylinder, p + Vector3.up * .018f, new Vector3(1.9f, .018f, 1.9f), dim, altarObject.transform, false).GetComponent<Renderer>();
                Primitive("Socket Center", PrimitiveType.Cylinder, p + Vector3.up * .038f, new Vector3(1.5f, .006f, 1.5f), black, altarObject.transform, false);
                var glow = new GameObject("Altar Glow", typeof(Light)); glow.transform.SetParent(altarObject.transform); glow.transform.position = p + Vector3.up * 1.8f;
                a.glow = glow.GetComponent<Light>(); a.glow.type = LightType.Point; a.glow.range = 4; a.glow.intensity = 2; a.glow.color = new Color(.35f, .85f, 1); a.glow.enabled = false;
                altars[i] = a;
            }
            var fragmentRoot = new GameObject("Fragments").transform; fragmentRoot.SetParent(world);
            var outerRoot = new GameObject("Ancient Fragments - Locked"); outerRoot.transform.SetParent(fragmentRoot);
            var prefabs = new AstralFragment[3];
            Material[] materials = { star, life, ancient };
            for (int i = 0; i < 3; i++)
            {
                var obj = Primitive("Fragment " + (FragmentKind)i, PrimitiveType.Cube, Vector3.up * .62f, Vector3.one * 1.2f, materials[i], null);
                var rb = obj.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation;
                obj.AddComponent<PullableBox>(); var fragment = obj.AddComponent<AstralFragment>(); fragment.kind = (FragmentKind)i;
                var cap = Primitive("Placeholder Floating Rune", i == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube, Vector3.up * 1.38f, Vector3.one * .24f, gold, obj.transform, false);
                cap.transform.localRotation = Quaternion.Euler(0, 45, 45);
                string path = Root + "/Prefabs/" + (FragmentKind)i + " Fragment.prefab";
                PrefabUtility.SaveAsPrefabAsset(obj, path); UnityEngine.Object.DestroyImmediate(obj);
                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<AstralFragment>();
            }
            Vector3[] starts = { new Vector3(0,.62f,-3.25f), new Vector3(5.2f,.62f,-4), new Vector3(-6.3f,.62f,1), new Vector3(6.7f,.62f,5) };
            for (int i = 0; i < starts.Length; i++) MakeFragment(prefabs[0], "star-free-" + i, starts[i], fragmentRoot);
            MakeFragment(prefabs[1], "life-free", new Vector3(-6.5f,.62f,6.5f), fragmentRoot);
            MakeFragment(prefabs[2], "ancient-0", new Vector3(-5,.62f,31), outerRoot.transform);
            MakeFragment(prefabs[2], "ancient-1", new Vector3(5,.62f,31), outerRoot.transform);
            outerRoot.SetActive(false);
            var well = Primitive("Placeholder - Origin Well", PrimitiveType.Cylinder, new Vector3(-7,.08f,-7), new Vector3(2,.08f,2), dim, home, false);
            Text("ORIGIN WELL", new Vector3(-7,1.2f,-7), home, .11f, Color.white);
            var shrine = Primitive("Placeholder - Final Seed Shrine", PrimitiveType.Cylinder, new Vector3(0,.2f,32), new Vector3(3,.2f,3), gold, far);
            var seed = Primitive("Placeholder - Final Seed", PrimitiveType.Sphere, new Vector3(0,1.6f,32), Vector3.one*1.6f, ancient, shrine.transform, false);
            var wisp = Primitive("Placeholder - Wisp", PrimitiveType.Sphere, new Vector3(0,2,-4), Vector3.one*.4f, life, world, false);
            wisp.SetActive(false);
            var systems = new GameObject("Last Spark Systems");
            var game = systems.AddComponent<LastSparkGame>();
            string balancePath = Root + "/MVP Balance.asset";
            var balance = AssetDatabase.LoadAssetAtPath<LastSparkBalance>(balancePath);
            if (balance == null) { balance = ScriptableObject.CreateInstance<LastSparkBalance>(); AssetDatabase.CreateAsset(balance,balancePath); }
            game.balance = balance; game.player = player; game.cameraRig = camera;
            game.altars = altars; game.fragmentPrefabs = prefabs; game.fragmentsRoot = fragmentRoot;
            game.bridge = bridge; game.bridgeBarrier = gate; game.outerFragments = outerRoot; game.shrine = shrine.transform; game.spiritVisual = wisp.transform;
            game.worldLight = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            game.worldLight.intensity = .45f; game.worldLight.color = new Color(.64f,.74f,1);
            systems.AddComponent<LastSparkHUD>().game = game;
            player.transform.position = new Vector3(0,.025f,-4.8f); player.readInput = true;
            camera.readInput = true; camera.distance = 20; camera.bounds = new Vector2(14,37); camera.minimumFocus = new Vector2(-14,-9);
            camera.transform.rotation = Quaternion.Euler(55,0,0); camera.transform.position = new Vector3(0,0,1) - camera.transform.forward * camera.distance;
            var cam = camera.GetComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.012f,.018f,.055f); cam.farClipPlane = 180;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.22f,.25f,.4f); RenderSettings.fog = false;
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = systems;
            File.WriteAllText("Library/LastSpark.setup-result.txt", "SUCCESS\nUpdated SampleScene. Preserved player, camera and supplied animation controller.\n16 altars, 4 free Starlight + 1 Life + 2 locked Ancient fragments, 2 islands, bridge, 7 upgrades/actions, save system.\nBackup: " + backup);
        }
        static void MakeFragment(AstralFragment prefab, string id, Vector3 position, Transform parent)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject); obj.transform.SetParent(parent,true); obj.transform.position = position;
            obj.name = "Fragment - " + id; obj.GetComponent<AstralFragment>().id = id;
        }
        static void Wall(string name, Vector3 position, Vector3 scale, Material mat, Transform parent) => Primitive(name,PrimitiveType.Cube,position,scale,mat,parent);
        static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material mat, Transform parent, bool collision = true)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.position = position; obj.transform.localScale = scale;
            obj.transform.SetParent(parent,true); obj.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collision) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
        static Material Mat(string name, Color color, float emission = 0)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,path); }
            mat.SetColor("_BaseColor",color); mat.SetFloat("_Smoothness",.25f);
            mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor",color*emission); EditorUtility.SetDirty(mat); return mat;
        }
        static void Text(string content, Vector3 position, Transform parent, float size, Color color, bool flat = false)
        {
            var obj = new GameObject("Placeholder Label - " + content,typeof(TextMesh)); obj.transform.SetParent(parent,true); obj.transform.position = position;
            obj.transform.rotation = flat ? Quaternion.Euler(90,0,0) : Quaternion.Euler(45,0,0);
            var text = obj.GetComponent<TextMesh>(); text.text = content; text.fontSize = 48; text.characterSize = size; text.anchor = TextAnchor.MiddleCenter; text.color = color;
        }
    }
}
