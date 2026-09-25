using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LightingBlocks.Editor
{
    // Actual current legacy profiles, without changing their production configuration to satisfy
    // historical Default-profile fixtures (which predate weighted colours and 50-row bridges).
    [InitializeOnLoad]
    public static class LightingLegacyCompatibilityCheck
    {
        const string Key="LightingLegacyCompatibilityCheck.Play";
        static readonly string[] scenes={"SampleScene","SampleSceneTerrain","terrainScene"};
        static readonly StringBuilder report=new StringBuilder();
        static int index,frames;static bool running,advance;
        static LightingLegacyCompatibilityCheck()
        {
            EditorApplication.update+=Poll;
            EditorApplication.playModeStateChanged+=state=>{
                if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){running=true;frames=0;}
                if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Key,false)){advance=true;running=false;}
            };
        }
        public static void Run(){report.Clear();index=0;SessionState.SetBool(Key,true);Open();}
        static void Open(){EditorSceneManager.OpenScene("Assets/Scenes/"+scenes[index]+".unity");EditorApplication.isPlaying=true;}
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);report.AppendLine("PASS: "+scenes[index]+": "+message);}
        static void Poll()
        {
            if(advance){advance=false;if(++index<scenes.Length){Open();return;}SessionState.SetBool(Key,false);report.AppendLine(report.ToString().Contains("FAIL:")?"FAIL: legacy compatibility":"SUCCESS: all current legacy scenes passed");File.WriteAllText("Library/LightingLegacyCompatibilityCheck.txt",report.ToString());EditorSceneManager.OpenScene("Assets/Scenes/Lighting.unity");return;}
            if(!running||++frames<40)return;running=false;
            try{Checks();}catch(Exception e){report.AppendLine("FAIL: "+scenes[index]+": "+e);}
            finally{var game=GameManager.Instance;if(game!=null){game.persistenceEnabled=false;if(game.savePathOverride!=null)foreach(var suffix in new[]{"",".bak",".tmp"})if(File.Exists(game.SavePath+suffix))File.Delete(game.SavePath+suffix);}EditorApplication.isPlaying=false;}
        }
        static void Checks()
        {
            Check(UnityEngine.Object.FindAnyObjectByType<LightingFog>()==null&&UnityEngine.Object.FindAnyObjectByType<LightingPickupAudio>()==null,"Lighting fog and audio components absent");
            Check(Shader.GetGlobalFloat("_LightingFogActive")==0,"Lighting shader fog disabled");
            var game=GameManager.Instance;
            // The separate terrain art prototype has no gameplay systems.
            if(game==null){Check(UnityEngine.Object.FindObjectsByType<Renderer>().Length>0||Terrain.activeTerrains.Any(t=>t.terrainData!=null),"Scenery-only prototype retains rendered geometry");return;}
            Check(!game.persistenceEnabled&&game.savePathOverride!=null,"Save isolation established before startup");game.autoTick=false;game.player.readInput=false;game.cameraRig.readInput=false;GameHUD.Current.BeginJourney();
            Check(!BridgeProfile.Current.isLighting&&BridgeProfile.Current.hasForestToggle&&BridgeProfile.Current.randomTreeStones,"Existing terrain controls and weighted rewards retained");
            var forest=ForestWorld.For(game);Check(forest.State.particleVersion==0,"Legacy particle schema retained");
            Check(game.GetComponent<LightingParticles>()==null&&forest.WisdomTrees.Count==3&&forest.PickupVisuals.Count==72,"Legacy trees and pickups remain, without receivers");
            var owner=forest.State.islands[0];var other=forest.State.islands[1];var slot=owner.slots[0];
            var controller=game.player.GetComponent<CharacterController>();controller.enabled=false;game.player.transform.position=slot.position+Vector3.up*.025f;controller.enabled=true;Physics.SyncTransforms();
            Check(forest.TryCollect(0)&&owner.inventory==1&&other.inventory==0&&forest.State.inventory==0,"Walk-over collection uses per-island legacy inventory");
            owner.inventory=3;var tree=forest.WisdomTrees[0];controller.enabled=false;game.player.transform.position=tree.transform.position+Vector3.right*2;controller.enabled=true;Physics.SyncTransforms();
            Check(tree.BeginCharge(),"Legacy E-to-charge action remains available");tree.Tick(1);
            Check(game.Blocks.Count==1&&owner.inventory==0&&game.Blocks[0].tier>=0&&game.Blocks[0].tier<=2&&game.Blocks[0].GetComponent<BlockArt>()!=null,"Three island particles produce an authored tile of an allowed random colour");
            game.souls=800;Check(game.Purchase("strength")&&game.Purchase("radiance")&&game.Purchase("generator"),"Legacy upgrades remain available");
            owner.inventory=7;other.inventory=5;slot.remaining=17;string before=game.ExportSave();
            game.persistenceEnabled=true;Check(game.Save(),"Isolated legacy save writes");owner.inventory=0;other.inventory=0;
            Check(game.Load()&&forest.State.islands[0].inventory==7&&forest.State.islands[1].inventory==5&&forest.State.islands[0].slots[0].remaining==17,"Legacy inventory and respawn state reload unchanged");
            Check(game.strength==1&&game.radiance==1&&game.generator==1&&game.Blocks.Count==1,"Legacy upgrades and tile survive reload");
            Check(forest.State.particleVersion==0&&forest.State.islands.All(i=>i.rewards.Count==0),"Lighting reward migration never runs in legacy scenes");
        }
    }
}
