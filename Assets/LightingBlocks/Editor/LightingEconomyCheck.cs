using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LightingBlocks.Editor
{
    // Agent C evidence for the Lighting economy/UI packet. Checks what is reachable without the
    // coordinator's still-pending GameManager wiring for "soulgain" (Cost/Level/Cap/Purchase/
    // Recalculate/hotkeys - see C-report.md "Change requests for coordinator" and the TODO(C)
    // comment above GameManager.soulGainLevel): the formulas themselves, the already-shipped
    // SaveData round-trip fields, this agent's own SkillTree purchase-route gating, and the
    // per-profile Stars default in EnvironmentVisibility.
    // Batch entrypoint: -executeMethod LightingBlocks.Editor.LightingEconomyCheck.Run
    public static class LightingEconomyCheck
    {
        public static void Run()
        {
            CheckSoulGainMultiplier();
            CheckSoulGainPrice();
            CheckSaveDataRoundTrip();
            CheckSkillTreeRoutes();
            CheckStarsDefaultPerProfile();
            Debug.Log("LIGHTING ECONOMY CHECK PASS");
        }

        // income = existingPathIncome * (1 + 0.25 * soulGainLevel), applied once after existing path
        // bonuses (see the change request against GameManager.Recalculate).
        static void CheckSoulGainMultiplier()
        {
            AssertNear(Multiplier(0), 1.0, "level 0 multiplier");
            AssertNear(Multiplier(1), 1.25, "level 1 multiplier");
            AssertNear(Multiplier(8), 3.0, "level 8 multiplier");
            const double existingPathIncome = 42.5; // an arbitrary already-bonused PathRate
            AssertNear(existingPathIncome * Multiplier(0), 42.5, "level 0 applied income");
            AssertNear(existingPathIncome * Multiplier(1), 53.125, "level 1 applied income");
            AssertNear(existingPathIncome * Multiplier(8), 127.5, "level 8 applied income");
        }
        static double Multiplier(int level) => 1 + .25 * level;

        // price = ceil(60 * 1.65^level) - same shape as the legacy Soul Well/generator cost, keyed
        // off soulGainLevel instead (see the change request against GameManager.Cost).
        static void CheckSoulGainPrice()
        {
            Assert(Price(0) == 60, "price level 0 should be 60, got " + Price(0));
            Assert(Price(1) == 99, "price level 1 should be 99, got " + Price(1));
            Assert(Price(2) == 164, "price level 2 should be 164, got " + Price(2));
            Assert(Price(8) == Math.Ceiling(60 * Math.Pow(1.65, 8)), "price level 8 matches the spec formula");
        }
        static double Price(int level) => Math.Ceiling(60 * Math.Pow(1.65, level));

        // Pure JsonUtility round-trip of GameManager.SaveData - exercises the schema/soulGainLevel
        // fields the coordinator already added, without needing a running GameManager instance.
        static void CheckSaveDataRoundTrip()
        {
            var data = new GameManager.SaveData { schema = "lighting-v1", soulGainLevel = 8, souls = 123, rate = 4.5, speed = 6 };
            string json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<GameManager.SaveData>(json);
            Assert(back.schema == "lighting-v1", "schema round-trips");
            Assert(back.soulGainLevel == 8, "soulGainLevel round-trips, got " + back.soulGainLevel);

            // Incompatible-schema rejection lives in GameManager.ImportSave, gated on
            // BridgeProfile.Current.saveSchema (coordinator-owned, already implemented). Confirm the
            // identity it depends on is actually in place: Lighting has its own schema, every legacy
            // profile leaves it null so the check is a no-op for them.
            Assert(BridgeProfile.Lighting.saveSchema == "lighting-v1", "Lighting.saveSchema is set");
            Assert(BridgeProfile.Terrain.saveSchema == null, "Terrain.saveSchema stays null (legacy check stays a no-op)");
            Assert(BridgeProfile.Default.saveSchema == null, "Default.saveSchema stays null (legacy check stays a no-op)");
        }

        // SkillTree (this agent's file) is the actual purchase route for upgrades. Build it under a
        // forced Lighting profile and confirm exactly Speed and Soul Gain are reachable - the retired
        // Strength/Links/Radiance/Soul Well never get a node, a button, or a skills[] entry.
        static void CheckSkillTreeRoutes()
        {
            bool wasLighting = BridgeProfile.Current.isLighting;
            var host = new GameObject("LightingEconomyCheck host");
            try
            {
                BridgeProfile.Select("Lighting");
                Assert(BridgeProfile.Current.isLighting, "profile switched to Lighting");
                var canvas = host.AddComponent<Canvas>();
                var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
                panel.SetParent(canvas.transform, false);
                SkillTree.Build(panel, null, null, null, out _, out var skills);
                Assert(skills.ContainsKey("speed"), "Lighting skill tree exposes speed");
                Assert(skills.ContainsKey("soulgain"), "Lighting skill tree exposes soulgain");
                foreach (var retired in new[] { "strength", "links", "radiance", "generator" })
                    Assert(!skills.ContainsKey(retired), "Lighting skill tree must not expose " + retired);
                Assert(skills.Count == 2, "Lighting skill tree has exactly two purchasable powers, got " + skills.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                BridgeProfile.Select(wasLighting ? "Lighting" : "LightingEconomyCheck-restore-non-lighting");
            }
        }

        // The Esc-menu Stars toggle must default off in Lighting and on in every legacy profile,
        // through two separate PlayerPrefs keys, so setting Lighting's preference never writes the
        // legacy key/value.
        static void CheckStarsDefaultPerProfile()
        {
            bool wasLighting = BridgeProfile.Current.isLighting;
            bool hadLegacyKey = PlayerPrefs.HasKey(EnvironmentVisibility.StarsKey);
            int legacyBefore = PlayerPrefs.GetInt(EnvironmentVisibility.StarsKey, 1);
            PlayerPrefs.DeleteKey(EnvironmentVisibility.StarsKeyLighting);
            try
            {
                BridgeProfile.Select("Lighting");
                Assert(!EnvironmentVisibility.StarsVisible, "Lighting defaults Stars off");
                EnvironmentVisibility.SetStarsVisible(true);
                Assert(PlayerPrefs.GetInt(EnvironmentVisibility.StarsKeyLighting, 0) == 1, "Lighting Stars pref persisted under its own key");
                Assert(PlayerPrefs.HasKey(EnvironmentVisibility.StarsKey) == hadLegacyKey, "legacy Stars key untouched by a Lighting toggle");
                if (hadLegacyKey) Assert(PlayerPrefs.GetInt(EnvironmentVisibility.StarsKey, 1) == legacyBefore, "legacy Stars value untouched");

                BridgeProfile.Select("LightingEconomyCheck-restore-non-lighting");
                Assert(!BridgeProfile.Current.isLighting, "left the Lighting profile");
                Assert(EnvironmentVisibility.StarsVisible == (PlayerPrefs.GetInt(EnvironmentVisibility.StarsKey, 1) != 0), "legacy Stars reads the shared key/default");
            }
            finally
            {
                PlayerPrefs.DeleteKey(EnvironmentVisibility.StarsKeyLighting);
                BridgeProfile.Select(wasLighting ? "Lighting" : "LightingEconomyCheck-restore-non-lighting");
            }
        }

        static void Assert(bool condition, string message) { if (!condition) throw new Exception("LightingEconomyCheck FAILED: " + message); }
        static void AssertNear(double a, double b, string message) { if (Math.Abs(a - b) > 1e-9) throw new Exception("LightingEconomyCheck FAILED: " + message + " (" + a + " vs " + b + ")"); }
    }
}
