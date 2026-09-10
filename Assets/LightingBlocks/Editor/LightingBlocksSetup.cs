using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightingBlocks.Editor
{
    [InitializeOnLoad]
    public static class LightingBlocksSetup
    {
        const string Root = "Assets/LightingBlocks";
        const string Request = "Library/LightingBlocks.setup-request";
        static double nextCheck;
        static LightingBlocksSetup() { EditorApplication.update += CheckRequest; }
        static void CheckRequest()
        {
            if (EditorApplication.timeSinceStartup < nextCheck || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextCheck = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            try { Build(); }
            catch (Exception e) { File.WriteAllText("Library/LightingBlocks.setup-result.txt", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Tools/Lighting Blocks/Build Playable Scene")]
        public static void Build()
        {
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string backup = "Assets/Scenes/SampleScene_BeforeBoxControls.unity";
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true))
                throw new Exception("Could not save original scene backup.");
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            string idlePath = "Assets/Animations/base@Neutral Idle.fbx";
            ImportClip(idlePath, null);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(idlePath).OfType<Avatar>().First(a => a.isHuman && a.isValid);
            ImportClip("Assets/Animations/base@Running.fbx", avatar);
            ImportClip("Assets/Animations/base@Pull Heavy Object - 20%.fbx", avatar);
            var idle = Clip(idlePath);
            var run = Clip("Assets/Animations/base@Running.fbx");
            var pull = Clip("Assets/Animations/base@Pull Heavy Object - 20%.fbx");
            string controllerPath = Root + "/BoxCharacter.controller";
            var animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (animatorController == null) animatorController = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = animatorController.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var idleState = machine.AddState("Idle", new Vector3(230, 60)); idleState.motion = idle;
            machine.AddState("Run", new Vector3(230, 150)).motion = run;
            machine.AddState("Pull", new Vector3(230, 240)).motion = pull;
            machine.defaultState = idleState;
            EditorUtility.SetDirty(animatorController);

            foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            var floorMat = Material("Floor", new Color(.17f, .23f, .28f));
            var lineMat = Material("Grid", new Color(.25f, .33f, .37f));
            var wallMat = Material("Stone", new Color(.37f, .44f, .5f));
            var boxMat = Material("Boxes", new Color(.18f, .7f, .58f));
            var bandMat = Material("Box Bands", new Color(.09f, .23f, .25f));
            var playerMat = Material("Character", new Color(.85f, .75f, .58f));
            var environment = new GameObject("Environment").transform;
            Cube("Ground", new Vector3(0, -.25f, 0), new Vector3(36, .5f, 36), floorMat, environment);
            for (int i = -16; i <= 16; i += 2)
            {
                var x = Cube("Grid X " + i, new Vector3(i, .003f, 0), new Vector3(.025f, .005f, 35), lineMat, environment);
                var z = Cube("Grid Z " + i, new Vector3(0, .003f, i), new Vector3(35, .005f, .025f), lineMat, environment);
                UnityEngine.Object.DestroyImmediate(x.GetComponent<Collider>());
                UnityEngine.Object.DestroyImmediate(z.GetComponent<Collider>());
            }
            Cube("North Wall", new Vector3(0, .6f, 18), new Vector3(36, 1.2f, .6f), wallMat, environment);
            Cube("South Wall", new Vector3(0, .6f, -18), new Vector3(36, 1.2f, .6f), wallMat, environment);
            Cube("East Wall", new Vector3(18, .6f, 0), new Vector3(.6f, 1.2f, 36), wallMat, environment);
            Cube("West Wall", new Vector3(-18, .6f, 0), new Vector3(.6f, 1.2f, 36), wallMat, environment);
            Cube("Obstacle A", new Vector3(-5, .75f, 3), new Vector3(2, 1.5f, 5), wallMat, environment);
            Cube("Obstacle B", new Vector3(5, .75f, 5), new Vector3(5, 1.5f, 2), wallMat, environment);
            var boxes = new GameObject("Movable Boxes").transform;
            Vector3[] positions = { new Vector3(0, .62f, 1.65f), new Vector3(3, .62f, 1), new Vector3(-2.8f, .62f, -2), new Vector3(3.5f, .62f, -4), new Vector3(-1, .62f, 6) };
            for (int i = 0; i < positions.Length; i++)
            {
                var box = Cube("Pullable Box " + (i + 1), positions[i], Vector3.one * 1.2f, boxMat, boxes);
                var rigidbody = box.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true; rigidbody.useGravity = false;
                rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                box.AddComponent<PullableBox>();
                var band = Cube("Top Band", positions[i] + Vector3.up * .606f, new Vector3(.2f, .02f, 1.21f), bandMat, box.transform);
                UnityEngine.Object.DestroyImmediate(band.GetComponent<Collider>());
            }
            var player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(0, .025f, 0);
            var character = player.AddComponent<CharacterController>();
            character.height = 1.8f; character.radius = .3f; character.center = Vector3.up * .9f;
            character.skinWidth = .02f; character.stepOffset = .15f; character.minMoveDistance = 0;
            var visualRoot = new GameObject("Visual").transform;
            visualRoot.SetParent(player.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(idlePath));
            model.transform.SetParent(visualRoot, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new Exception("Character model contains no renderers.");
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            model.transform.localScale = Vector3.one * (1.8f / Mathf.Max(.0001f, bounds.size.y));
            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            model.transform.position += new Vector3(player.transform.position.x - bounds.center.x, player.transform.position.y - bounds.min.y, player.transform.position.z - bounds.center.z);
            foreach (var r in renderers) r.sharedMaterials = Enumerable.Repeat(playerMat, r.sharedMaterials.Length).ToArray();
            var animator = model.GetComponent<Animator>();
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var motor = player.AddComponent<BoxPlayer>();
            motor.visual = visualRoot; motor.animator = animator;
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 48; camera.nearClipPlane = .1f; camera.farClipPlane = 120;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .14f, .18f);
            camera.transform.rotation = Quaternion.Euler(55, 0, 0);
            camera.transform.position = -camera.transform.forward * 18;
            motor.viewCamera = camera;
            cameraObject.AddComponent<StrategyCamera>().player = player.transform;
            new GameObject("Controls").AddComponent<ControlsOverlay>().player = motor;
            var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.8f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .62f, .7f);
            RenderSettings.sun = light;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = player;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(Vector3.zero, Quaternion.Euler(55, 0, 0), 15);
            File.WriteAllText("Library/LightingBlocks.setup-result.txt", $"SUCCESS\nScene: {scenePath}\nBackup: {backup}\nAvatar: {avatar.name}, human={avatar.isHuman}, valid={avatar.isValid}\nClips: {idle.name} ({idle.length}), {run.name} ({run.length}), {pull.name} ({pull.length})\nModel scale: {model.transform.localScale}\nBoxes: {positions.Length}\n");
            Debug.Log("Lighting Blocks: playable scene created.");
        }

        static void ImportClip(string path, Avatar avatar)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = avatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
            if (avatar != null) importer.sourceAvatar = avatar;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true; clip.loopPose = true;
                clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionY = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
        static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        static Material Material(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .22f);
            EditorUtility.SetDirty(material);
            return material;
        }
        static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.SetParent(parent, true);
            cube.transform.position = position; cube.transform.localScale = scale;
            // Set world size correctly even when the parent is a scaled box.
            if (parent != null) cube.transform.localScale = new Vector3(scale.x / parent.lossyScale.x, scale.y / parent.lossyScale.y, scale.z / parent.lossyScale.z);
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }
    }
}
