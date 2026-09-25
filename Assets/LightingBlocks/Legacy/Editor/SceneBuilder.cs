using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightingBlocks.Editor
{
    public static class SceneBuilder
    {
        const string Root = "Assets/LightingBlocks/Art";
        [MenuItem("Tools/Lighting Blocks/Legacy/Build Scene")]
        public static void Build() { ForestBuilder.Apply(); }
        static void BuildLegacyDisabled()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play before rebuilding.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new Exception("Open SampleScene first.");
            string backup = "Assets/Scenes/SampleScene_BeforeSoulBridge.unity";
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Scene backup failed.");
            var player = UnityEngine.Object.FindAnyObjectByType<BoxPlayer>(); var camera = UnityEngine.Object.FindAnyObjectByType<StrategyCamera>();
            if (player == null || camera == null) throw new Exception("Existing animated player and strategy camera required.");
            foreach (string name in new[] { "The Last Spark - World", "Last Spark Systems", "Soul Bridge World", "Soul Bridge Systems", "Environment", "Movable Boxes", "Controls" })
            { var obj = GameObject.Find(name); if (obj != null) UnityEngine.Object.DestroyImmediate(obj); }
            var oldSoul = player.transform.Find("Lighting Soul"); if (oldSoul != null) UnityEngine.Object.DestroyImmediate(oldSoul.gameObject);
            Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory(Root + "/Prefabs"); AssetDatabase.Refresh();
            var ground = Mat("Night Basalt", new Color(.16f, .2f, .24f));
            var ordinary = Mat("Wandering Stone", new Color(.37f, .55f, .56f));
            var grave = Mat("Grave Stone", new Color(.28f, .32f, .55f));
            var ancient = Mat("Ancient Obsidian", new Color(.42f, .18f, .46f));
            var glow = Mat("Lighting Soul", new Color(.4f, 1, .8f), 2.5f);
            var rune = Mat("Path Marker", new Color(.12f, .3f, .28f), .35f);
            var world = new GameObject("Soul Bridge World").transform;
            var blocks = new GameObject("Bridge Stones").transform; blocks.SetParent(world);
            Make("Initial Shore", PrimitiveType.Cube, new Vector3(0, -.5f, -1), new Vector3(20, 1, 16), ground, world);
            Make("The Silent Reach", PrimitiveType.Cube, new Vector3(0, -.5f, 27.5f), new Vector3(20, 1, 13), ground, world);
            Make("Next Dawn", PrimitiveType.Cube, new Vector3(0, -.5f, 55), new Vector3(20, 1, 14), ground, world);
            // No collision surface spans the rifts. The player's placed blocks are the bridge.
            for (int g = 0; g < 2; g++)
            {
                float z = 7 + g * 27;
                Make("Shore start marker " + g, PrimitiveType.Cube, new Vector3(0, .012f, z - .15f), new Vector3(2.05f, .025f, .3f), rune, world, false);
                Label(g == 0 ? "BUILD YOUR SOUL PATH" : "DEEP RIFT · GRAVE STONES", new Vector3(0, .07f, z - 1), world, .08f);
                Label("NEXT SHORE", new Vector3(0, .05f, z + 15.3f), world, .07f);
            }
            var prefabs = new BridgeBlock[3]; var mats = new[] { ordinary, grave, ancient };
            for (int tier = 0; tier < 3; tier++)
            {
                var obj = Make("Bridge Stone " + tier, PrimitiveType.Cube, new Vector3(0, .36f, 0), new Vector3(1.4f, .7f, 1.4f), mats[tier], null);
                var rb = obj.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation;
                obj.AddComponent<PullableBox>(); obj.AddComponent<BridgeBlock>().tier = tier;
                var crest = Make("Placeholder tier marking", PrimitiveType.Cube, new Vector3(0, .725f, 0), new Vector3(.4f + tier * .15f, .025f, .15f), rune, obj.transform, false);
                string path = Root + "/Prefabs/Bridge Stone " + tier + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(obj, path); UnityEngine.Object.DestroyImmediate(obj);
                prefabs[tier] = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BridgeBlock>();
            }
            var starts = new[] { new Vector3(0,.36f,4.6f), new Vector3(-2,.36f,3), new Vector3(2,.36f,3), new Vector3(-4,.36f,1), new Vector3(4,.36f,1), new Vector3(-2,.36f,-1), new Vector3(2,.36f,-1), new Vector3(-4,.36f,-3), new Vector3(4,.36f,-3) };
            for (int i=0;i<starts.Length;i++) Spawn(prefabs[0], "home-"+i, starts[i], blocks);
            for (int i=0;i<9;i++) Spawn(prefabs[1], "grave-"+i, new Vector3(-4+i%3*4,.36f,25+i/3*3), blocks);
            Spawn(prefabs[2], "obsidian-0", new Vector3(-7,.36f,31), blocks); Spawn(prefabs[2], "obsidian-1", new Vector3(7,.36f,31), blocks);
            var well = Make("Placeholder Soul Well", PrimitiveType.Cylinder, new Vector3(-6,.25f,-6), new Vector3(1.6f,.25f,1.6f), ground, world);
            var wellCore = Make("Soul Well core", PrimitiveType.Sphere, new Vector3(-6,1.2f,-6), Vector3.one*.5f, glow, well.transform, false); wellCore.SetActive(false);
            Label("SOUL WELL · UPGRADE [5]", new Vector3(-6,.03f,-7.3f), world, .065f);
            var exit = Make("Next Area Beacon", PrimitiveType.Cylinder, new Vector3(0,.3f,57), new Vector3(2,.3f,2), grave, world);
            var exitGlow = Make("Next Dawn core", PrimitiveType.Sphere, new Vector3(0,1.4f,57), Vector3.one*.65f, glow, exit.transform, false); exitGlow.SetActive(false);
            Label("THE NEXT DAWN [E]", new Vector3(0,.035f,55.3f), world, .08f);
            var soul = Make("Lighting Soul", PrimitiveType.Sphere, player.transform.position + new Vector3(.4f,1.8f,0), Vector3.one * .22f, glow, player.transform, false);
            soul.transform.localPosition = new Vector3(.4f,1.8f,0);
            var light = soul.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(.55f,1,.8f); light.range=9; light.intensity=3.5f; light.shadows=LightShadows.Soft;
            var systems = new GameObject("Soul Bridge Systems"); var game = systems.AddComponent<GameManager>();
            game.player=player; game.cameraRig=camera; game.blocksRoot=blocks; game.blockPrefabs=prefabs; game.soulLight=light; game.soulWell=well.transform; game.wellCore=wellCore.GetComponent<Renderer>(); game.exit=exit.transform; game.nextAreaGlow=exitGlow;
            systems.AddComponent<GameHUD>().game=game;
            player.transform.position=new Vector3(0,.025f,3); player.readInput=true; player.moveSpeed=4.7f; player.pullSpeed=2.4f; player.grabReach=1.4f;
            var cc = player.GetComponent<CharacterController>(); cc.stepOffset=.4f;
            camera.readInput=true; camera.distance=27; camera.bounds=new Vector2(14,62); camera.minimumFocus=new Vector2(-14,-9);
            camera.transform.rotation=Quaternion.Euler(55,0,0); camera.transform.position=player.transform.position-camera.transform.forward*27;
            var cam=camera.GetComponent<Camera>(); cam.backgroundColor=new Color(.002f,.004f,.009f); cam.clearFlags=CameraClearFlags.SolidColor; cam.fieldOfView=60; cam.farClipPlane=180;
            foreach(var sun in UnityEngine.Object.FindObjectsByType<Light>()) if(sun.type==LightType.Directional) {sun.intensity=.055f; sun.color=new Color(.35f,.45f,.65f);}
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight=new Color(.018f,.025f,.045f); RenderSettings.fog=false;
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene); Selection.activeGameObject=systems;
            File.WriteAllText("Library/LightingBlocks.setup-result.txt", "SUCCESS: 3 shores, 2 genuine gaps, 20 stones, animated player, lighting soul, Souls economy, group hauling, upgrades and separate persistence. Backup: "+backup);
        }
        static void Spawn(BridgeBlock prefab,string id,Vector3 p,Transform parent)
        { var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject); obj.transform.SetParent(parent,true); obj.transform.position=p; obj.name=id; obj.GetComponent<BridgeBlock>().id=id; }
        static GameObject Make(string name,PrimitiveType type,Vector3 p,Vector3 size,Material mat,Transform parent,bool collider=true)
        {var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.position=p;obj.transform.localScale=size;obj.transform.SetParent(parent,true);obj.GetComponent<Renderer>().sharedMaterial=mat;if(!collider)UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;}
        static Material Mat(string name,Color color,float emission=0)
        { string path=Root+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.2f);mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",color*emission);EditorUtility.SetDirty(mat);return mat;}
        static void Label(string text,Vector3 p,Transform parent,float size)
        {var obj=new GameObject("Placeholder label - "+text,typeof(TextMesh));obj.transform.SetParent(parent,true);obj.transform.position=p;obj.transform.rotation=Quaternion.Euler(90,0,0);var t=obj.GetComponent<TextMesh>();t.text=text;t.fontSize=48;t.characterSize=size;t.anchor=TextAnchor.MiddleCenter;t.color=new Color(.25f,.55f,.5f);}
    }
}
