using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LightingBlocks.Editor
{
    [InitializeOnLoad]
    public static class LightingBlocksVerification
    {
        static bool running;
        static int frame;
        static double nextCheck;
        static readonly StringBuilder Report = new StringBuilder();
        static LightingBlocksVerification()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += mode =>
            {
                if (mode == PlayModeStateChange.EnteredPlayMode) Enter(EnterPlayModeOptions.None);
            };
        }

        [MenuItem("Tools/Lighting Blocks/Run Gameplay Checks")]
        public static void Request()
        {
            if (UnityEngine.Object.FindAnyObjectByType<SoulBridgeGame>() != null) { SoulBridgeVerification.Request(); return; }
            if (UnityEngine.Object.FindAnyObjectByType<LastSparkGame>() != null) { LastSparkVerification.Request(); return; }
            File.WriteAllText("Library/LightingBlocks.test-request", "run");
        }
        static void Tick()
        {
            if (!running)
            {
                if (EditorApplication.timeSinceStartup < nextCheck || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                nextCheck = EditorApplication.timeSinceStartup + 1;
                if (!File.Exists("Library/LightingBlocks.test-request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete("Library/LightingBlocks.test-request");
                SessionState.SetBool("LightingBlocksVerification", true);
                EditorApplication.isPlaying = true;
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (++frame == 30)
            {
                try { Verify(); }
                catch (Exception error) { Report.AppendLine("FAIL: " + error); Debug.LogException(error); }
                File.WriteAllText("Library/LightingBlocks.test-result.txt", Report.ToString());
            }
            if (frame == 65)
            {
                ScreenCapture.CaptureScreenshot("Library/LightingBlocks-preview.png");
            }
            if (frame > 100)
            {
                running = false;
                SessionState.SetBool("LightingBlocksVerification", false);
                EditorApplication.isPlaying = false;
            }
        }
        [InitializeOnEnterPlayMode]
        static void Enter(EnterPlayModeOptions options)
        {
            if (!SessionState.GetBool("LightingBlocksVerification", false)) return;
            running = true; frame = 0; Report.Clear();
            EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        }
        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            Report.AppendLine("PASS: " + label);
        }
        static void Teleport(BoxPlayer player, Vector3 position)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false; player.transform.position = position; cc.enabled = true;
            Physics.SyncTransforms();
        }
        static void Move(BoxPlayer player, Vector2 direction, int count)
        {
            for (int i = 0; i < count; i++) player.StepMovement(direction, .02f);
        }
        static void Verify()
        {
            var player = UnityEngine.Object.FindAnyObjectByType<BoxPlayer>();
            var camera = UnityEngine.Object.FindAnyObjectByType<StrategyCamera>();
            var box = GameObject.Find("Pullable Box 1").GetComponent<PullableBox>();
            player.readInput = false; camera.readInput = false;
            Vector3 originalBox = box.Body.position;
            Check(player.animator.avatar.isHuman && player.animator.avatar.isValid, "Character has a valid humanoid avatar");
            player.animator.Play("Idle"); player.animator.Update(.1f);
            Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Idle animation plays");
            Teleport(player, new Vector3(0, .025f, -6));
            Move(player, Vector2.up, 25);
            float straight = player.transform.position.z + 6;
            Check(straight > 2.1f && straight < 2.4f, "WASD movement distance matches configured speed");
            player.animator.Update(.2f);
            Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), "Run animation transitions during movement");
            Teleport(player, new Vector3(0, .025f, -6));
            Move(player, Vector2.one, 25);
            var diagonal = player.transform.position - new Vector3(0, player.transform.position.y, -6);
            Check(Mathf.Abs(diagonal.magnitude - straight) < .1f, "Diagonal movement is normalized");
            Check(!player.TryGrab(box), "Distant boxes cannot be grabbed");
            Teleport(player, new Vector3(0, .025f, 0));
            Check(player.TryGrab(box), "Nearby box can be grabbed");
            Vector3 offset = box.Body.position - player.transform.position;
            Move(player, Vector2.down, 50);
            Check(box.Body.position.z < originalBox.z - 2, "Held box follows when pulling backward");
            Check(Vector3.ProjectOnPlane(box.Body.position - player.transform.position - offset, Vector3.up).magnitude < .05f, "Grab distance stays stable");
            player.animator.Update(.25f);
            Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Pull"), "Pull animation plays while grabbing");
            Check(Vector3.Dot(player.visual.forward, (box.transform.position - player.transform.position).normalized) > .8f, "Player faces the box while pulling");
            var released = box.Body.position;
            player.Release(); Move(player, Vector2.down, 15);
            Check(player.HeldBox == null && box.Holder == null && Vector3.Distance(released, box.Body.position) < .001f, "Release detaches and stops the box immediately");
            box.transform.position = originalBox; box.Body.position = originalBox;
            Teleport(player, new Vector3(0, .025f, 0));
            Check(player.TryGrab(box), "Box can be grabbed again after release");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "Verification obstacle";
            obstacle.transform.position = new Vector3(0, .75f, -2);
            obstacle.transform.localScale = new Vector3(4, 1.5f, .5f);
            Physics.SyncTransforms();
            Move(player, Vector2.down, 100);
            Check(player.transform.position.z > -1.5f, "Player stops at solid obstacles while dragging");
            player.Release();
            obstacle.transform.position = new Vector3(0, .75f, 3.3f);
            box.transform.position = originalBox; box.Body.position = originalBox;
            Teleport(player, new Vector3(0, .025f, 0));
            Check(player.TryGrab(box), "Regrab before push collision test"); Move(player, Vector2.up, 100);
            Report.AppendLine($"Push diagnostic: box={box.Body.position}, maxZ={box.Shape.bounds.max.z}, player={player.transform.position}, held={player.HeldBox != null}");
            Check(box.Shape.bounds.max.z < 3.1f && box.Body.position.z > originalBox.z + .2f, "Held box stops at obstacles when pushing");
            player.Release();
            obstacle.transform.position = new Vector3(0, .75f, .7f);
            obstacle.transform.localScale = new Vector3(3, 1.5f, .1f);
            box.transform.position = originalBox; box.Body.position = originalBox;
            Teleport(player, new Vector3(0, .025f, 0));
            Check(!player.TryGrab(box), "Cannot grab through a wall");
            obstacle.SetActive(false);
            UnityEngine.Object.Destroy(obstacle);
            Physics.SyncTransforms();

            // Exercise the actual mouse polling path using a temporary Input System device.
            var testMouse = InputSystem.AddDevice<Mouse>("LightingBlocksTestMouse");
            try
            {
                player.readInput = true;
                InputSystem.QueueStateEvent(testMouse, new MouseState { position = new Vector2(Screen.width / 2f, Screen.height / 2f) }.WithButton(MouseButton.Left));
                InputSystem.Update();
                player.SendMessage("Update");
                Report.AppendLine($"Input diagnostic: focused={Application.isFocused}, currentTestMouse={Mouse.current == testMouse}, pressed={testMouse.leftButton.isPressed}, down={testMouse.leftButton.wasPressedThisFrame}, nearby={player.NearbyBox}, box={box.transform.position}");
                Check(player.HeldBox == box, "Left mouse press grabs the nearby box through the input path");
                InputSystem.QueueStateEvent(testMouse, new MouseState()); InputSystem.Update();
                player.SendMessage("Update");
                Check(player.HeldBox == null, "Mouse release detaches through the input path");
                player.TryGrab(box); player.SendMessage("OnApplicationFocus", false);
                Check(player.HeldBox == null, "Losing focus releases the grab");
            }
            finally { player.readInput = false; InputSystem.RemoveDevice(testMouse); }
            camera.CenterOnPlayer(); camera.Pan(new Vector2(1000, 1000));
            Check(Mathf.Abs(camera.Focus.x) <= camera.bounds.x && Mathf.Abs(camera.Focus.z) <= camera.bounds.y, "Camera panning stays within arena bounds");
            camera.CenterOnPlayer();
            Check(Vector3.ProjectOnPlane(camera.Focus - player.transform.position, Vector3.up).magnitude < .01f, "Camera recenters on player");
            player.Release(); box.transform.position = originalBox; box.Body.position = originalBox;
            Teleport(player, new Vector3(0, .025f, 0));
            player.StepMovement(Vector2.zero, .02f); player.animator.Update(.2f);
            camera.CenterOnPlayer();
            Report.AppendLine("SUCCESS: all gameplay checks passed.");
        }
    }
}

