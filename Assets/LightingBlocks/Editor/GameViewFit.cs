using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightingBlocks.Editor
{
    // Game view zoom is separate from Canvas scaling. A QHD render at 1x can
    // crop a correctly laid out Canvas when the editor viewport is smaller.
    [InitializeOnLoad]
    public static class GameViewFit
    {
        struct SizeStamp { public Rect window; public Vector2 target; }
        static readonly Dictionary<EditorWindow, SizeStamp> sizes = new Dictionary<EditorWindow, SizeStamp>();
        static double nextCheck;

        static GameViewFit()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state => sizes.Clear();
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying || GameHUD.Current == null || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + .25;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window.GetType().Name != "GameView") continue;
                var settings = new SerializedObject(window);
                var targetProperty = settings.FindProperty("m_TargetSize");
                if (targetProperty == null) continue;
                var target = targetProperty.vector2Value;
                if (sizes.TryGetValue(window, out var last) && last.window == window.position && last.target == target) continue;
                if (Fit(window)) sizes[window] = new SizeStamp { window = window.position, target = target };
            }
        }

        [MenuItem("Tools/Lighting Blocks/Fit Game View")]
        public static void FitAll()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().Name == "GameView") Fit(window);
        }

        public static bool Fit(EditorWindow window)
        {
            var settings = new SerializedObject(window);
            var zoom = settings.FindProperty("m_ZoomArea");
            if (zoom == null) return false;
            var area = zoom.FindPropertyRelative("m_DrawArea");
            var scale = zoom.FindPropertyRelative("m_Scale");
            var translation = zoom.FindPropertyRelative("m_Translation");
            var xMin = zoom.FindPropertyRelative("m_HBaseRangeMin");
            var xMax = zoom.FindPropertyRelative("m_HBaseRangeMax");
            var yMin = zoom.FindPropertyRelative("m_VBaseRangeMin");
            var yMax = zoom.FindPropertyRelative("m_VBaseRangeMax");
            if (area == null || scale == null || translation == null || xMin == null || xMax == null || yMin == null || yMax == null) return false;
            var viewport = area.rectValue;
            float width = xMax.floatValue - xMin.floatValue, height = yMax.floatValue - yMin.floatValue;
            if (viewport.width < 1 || viewport.height < 1 || width < 1 || height < 1) return false;
            float fit = Mathf.Min(1, viewport.width / width, viewport.height / height);
            scale.vector2Value = Vector2.one * fit;
            translation.vector2Value = viewport.size * .5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            window.Repaint();
            return true;
        }
    }
}
