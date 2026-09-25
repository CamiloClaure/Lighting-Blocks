using UnityEngine;

namespace LightingBlocks
{
    // Presentation preferences are independent of journey saves and apply to newly built worlds.
    public static class EnvironmentVisibility
    {
        public const string ForestKey = "LightingBlocks.ForestVisible";
        public const string StarsKey = "LightingBlocks.StarsVisible";
        // Lighting keeps its own Stars preference under a separate key. That is what lets it default
        // off (BridgeProfile.starsDefaultOn) without ever reading or overwriting the legacy scenes'
        // shared StarsKey/default (which stays on).
        public const string StarsKeyLighting = "LightingBlocks.StarsVisible.Lighting";
        static bool forestLoaded;
        static bool forestVisible = true, starsVisible = true;
        // The key StarsVisible was last loaded from. Re-checked (not just loaded once) so a profile
        // switch within the same running session - e.g. Lighting, then a legacy scene - re-reads the
        // right preference instead of keeping Lighting's cached value.
        static string starsLoadedKey;

        public static bool ForestVisible { get { LoadForest(); return forestVisible; } }
        public static bool StarsVisible { get { LoadStars(); return starsVisible; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { forestLoaded = false; starsLoadedKey = null; }

        static string StarsPrefKey => BridgeProfile.Current.isLighting ? StarsKeyLighting : StarsKey;

        static void LoadForest()
        {
            if (forestLoaded) return;
            forestVisible = PlayerPrefs.GetInt(ForestKey, 1) != 0;
            forestLoaded = true;
        }
        static void LoadStars()
        {
            string key = StarsPrefKey;
            if (starsLoadedKey == key) return;
            starsVisible = PlayerPrefs.GetInt(key, BridgeProfile.Current.starsDefaultOn ? 1 : 0) != 0;
            starsLoadedKey = key;
        }
        public static void ApplyStars()
        {
            Shader.SetGlobalFloat("_LightingBlocksHideStars", StarsVisible ? 0 : 1);
        }
        // Global shader state must not leave the editor's sky hidden after exiting Play.
        public static void RestoreEditorSky() => Shader.SetGlobalFloat("_LightingBlocksHideStars", 0);

        public static void SetForestVisible(bool visible)
        {
            LoadForest(); forestVisible = visible;
            foreach (var forest in Object.FindObjectsByType<ForestWorld>(FindObjectsInactive.Include))
                forest.SetSceneryVisible(visible);
            PlayerPrefs.SetInt(ForestKey, visible ? 1 : 0); PlayerPrefs.Save();
        }
        public static void SetStarsVisible(bool visible)
        {
            starsVisible = visible; starsLoadedKey = StarsPrefKey; ApplyStars();
            PlayerPrefs.SetInt(StarsPrefKey, visible ? 1 : 0); PlayerPrefs.Save();
        }
    }
}
