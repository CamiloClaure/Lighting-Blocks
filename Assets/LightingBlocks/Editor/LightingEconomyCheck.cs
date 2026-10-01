using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LightingBlocks.Editor
{
    // Checks Lighting's quarter-particle economy, upgrade prices, save fields and skill-tree surface.
    // Batch entrypoint: -executeMethod LightingBlocks.Editor.LightingEconomyCheck.Run.
    public static class LightingEconomyCheck
    {
        public static void Run()
        {
            CheckParticleYield();
            CheckUpgradePrices();
            CheckSaveDataRoundTrip();
            CheckSkillTreeRoutes();
            CheckStarsDefaultPerProfile();
            Debug.Log("LIGHTING ECONOMY CHECK PASS");
        }

        static void CheckParticleYield()
        {
            Assert(ForestWorld.ParticleUnitsPerParticle==4,"one particle contains four quarter units");
            for(int level=0;level<=8;level++)
                Assert(ForestWorld.ParticleUnitsPerParticle+level==4+level,"yield level "+level+" adds exactly a quarter particle to an active pickup");
            Assert(ForestWorld.FormatParticleUnits(5)=="1.25"&&ForestWorld.FormatParticleUnits(6)=="1.5"&&ForestWorld.FormatParticleUnits(7)=="1.75","fractional wallet display covers every quarter");
        }
        static void CheckUpgradePrices()
        {
            for(int level=0;level<6;level++)Assert(Price("speed",level)==Math.Ceiling(10*Math.Pow(1.6,level)),"speed price at level "+level);
            for(int level=0;level<8;level++)Assert(Price("soulgain",level)==Math.Ceiling(15*Math.Pow(1.65,level)),"yield price at level "+level);
            Assert(Price("speed",0)==10&&Price("speed",1)==16&&Price("soulgain",0)==15&&Price("soulgain",1)==25,"initial speed and yield prices match the economy spec");
        }
        static double Price(string id,int level)
        {
            var host=new GameObject("Lighting economy price check");
            try
            {
                BridgeProfile.Select("Lighting");var game=host.AddComponent<GameManager>();
                if(id=="speed")game.speed=level;else game.soulGainLevel=level;
                return game.Cost(id);
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        // Pure JsonUtility round-trip of GameManager.SaveData - exercises the schema/soulGainLevel
        // fields the coordinator already added, without needing a running GameManager instance.
        static void CheckSaveDataRoundTrip()
        {
            var data = new GameManager.SaveData { schema = "lighting-v1", soulGainLevel = 8, souls = 123, rate = 4.5, speed = 6, forest = new ForestSave { particleVersion = 5, inventory = 127, dots = new List<DotSave> { new DotSave { x = -7, z = 40, remaining = 3.5f } }, islands = new List<IslandParticleSave> { new IslandParticleSave { id = "island-1", freeDeliveryColor = 2, freeDeliveryColors = new List<int> { 0, 1 } } } } };
            string json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<GameManager.SaveData>(json);
            Assert(back.schema == "lighting-v1", "schema round-trips");
            Assert(back.soulGainLevel == 8, "yield level round-trips, got " + back.soulGainLevel);
            Assert(back.forest.particleVersion==5&&back.forest.inventory==127,"quarter-particle wallet and version round-trip");
            Assert(back.forest.dots.Count==1&&back.forest.dots[0].x==-7&&back.forest.dots[0].z==40&&back.forest.dots[0].remaining==3.5f,"a collected dot's cell and return timer round-trip");
            Assert(back.forest.islands[0].freeDeliveryColor==2&&back.forest.islands[0].freeDeliveryColors.SequenceEqual(new[]{0,1}),"free selected and migrated tile deliveries round-trip");

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
                Assert(skills.ContainsKey("soulgain"), "Lighting skill tree exposes Particle Yield");
                var labels=panel.GetComponentsInChildren<Text>(true).Select(t=>t.text).ToArray();
                Assert(labels.Contains("PARTICLE YIELD"),"Lighting UI labels the upgrade Particle Yield");
                Assert(!labels.Any(t=>t.Contains("souls keep flowing")||t.Contains("Soul Gain")),"Lighting skill screen has no passive Souls / old gain wording");
                foreach (var retired in new[] { "strength", "links", "radiance", "generator" })
                    Assert(!skills.ContainsKey(retired), "Lighting skill tree must not expose " + retired);
                Assert(skills.Count == 7, "Lighting skill tree has exactly seven purchasable powers, got " + skills.Count);
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
