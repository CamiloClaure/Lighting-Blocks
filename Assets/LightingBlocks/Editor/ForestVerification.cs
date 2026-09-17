using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    [InitializeOnLoad] public static class ForestVerification
    {
        static readonly StringBuilder report=new StringBuilder();static bool running;static int frames;static double start;
        const string Result="Library/SpectralForest.tests.txt";
        static ForestVerification()
        {
            EditorApplication.update+=Poll;
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("Forest.Test",false)){running=true;frames=0;start=EditorApplication.timeSinceStartup;}if(s==PlayModeStateChange.EnteredEditMode){running=false;SessionState.SetBool("Forest.Test",false);if(File.Exists("Library/LightingBlocks.test-active"))File.Delete("Library/LightingBlocks.test-active");}};
        }
        static void Poll()
        {
            if(!running)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||!File.Exists("Library/SpectralForest.test-request"))return;
                File.Delete("Library/SpectralForest.test-request");File.WriteAllText("Library/LightingBlocks.test-active","forest tests isolate saves");report.Clear();EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");SessionState.SetBool("Forest.Test",true);EditorApplication.isPlaying=true;return;
            }
            var game=GameManager.Instance;
            if(game!=null){game.persistenceEnabled=false;game.autoTick=false;game.player.readInput=false;game.cameraRig.readInput=false;}
            if(++frames==40){try{Run();game.StartCoroutine(Captures());}catch(Exception e){report.AppendLine("FAIL: "+e);File.WriteAllText(Result,report.ToString());if(game!=null&&F.WisdomTrees.Count>0)game.StartCoroutine(Captures());else Finish();}}
            if(EditorApplication.timeSinceStartup-start>240){report.AppendLine("FAIL: timeout");Finish();}
        }
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);report.AppendLine("PASS: "+message);File.WriteAllText(Result,report.ToString());}
        [MenuItem("Tools/Lighting Blocks/Run Spectral Forest Checks")]public static void Request(){File.WriteAllText("Library/SpectralForest.test-request","run");}
        public static void BatchRun(){SessionState.SetBool("Twilight.Batch",true);Request();}
        static GameManager G=>GameManager.Instance;static ForestWorld F=>ForestWorld.For(G);
        static string StableSave(){var data=JsonUtility.FromJson<GameManager.SaveData>(G.ExportSave());data.utc=0;return JsonUtility.ToJson(data);}
        static void Teleport(Vector3 p){var c=G.player.GetComponent<CharacterController>();c.enabled=false;G.player.transform.position=new Vector3(p.x,.025f,p.z);c.enabled=true;Physics.SyncTransforms();}
        static void CollectThree(int island)
        {
            var d=F.configuration.islands[island];int before=F.Inventory(d.id).inventory,offset=0;for(int i=0;i<island;i++)offset+=F.configuration.islands[i].particleCount;
            for(int i=0;i<d.particleCount&&F.Inventory(d.id).inventory<before+3;i++)
            {if(F.Inventory(d.id).slots[i].remaining>0)continue;Teleport(F.Layouts[island].particles[i]);F.TryCollect(offset+i);}
            Check(F.Inventory(d.id).inventory==before+3,"Collect three distinct particles on "+d.id);
        }
        static BridgeBlock Charge(int island)
        {
            var tree=F.WisdomTrees[island];Teleport(tree.transform.position+Vector3.right*2);int count=G.Blocks.Count;var inv=F.Inventory(tree.Definition.id);int energy=inv.inventory;
            Check(tree.BeginCharge(),"Wisdom Tree accepts charge on "+tree.Definition.id);Check(!tree.BeginCharge(),"Repeated E cannot queue a second charge");tree.Tick(.61f);
            Check(G.Blocks.Count==count+1&&inv.inventory==energy-3,"One block costs exactly three particles");return G.Blocks.Last();
        }
        static void Place(BridgeBlock b,int gap,int row)
        {b.transform.position=GameManager.Cell(gap,0,row)+Vector3.up*.54f;b.Box.Body.position=b.transform.position;Physics.SyncTransforms();Check(G.TryPlace(b),"Existing bridge placement works: "+gap+":"+row);}
        static void Run()
        {
            Check(!G.persistenceEnabled,"Tests isolate the actual player save");GameHUD.Current.BeginJourney();
            Check(G.Blocks.Count==0,"New game has no free scattered starter blocks");
            Check(F.WisdomTrees.Count==F.configuration.islands.Length&&UnityEngine.Object.FindObjectsByType<WisdomTree>().Length==3,"Exactly one Wisdom Tree per island");
            Check(UnityEngine.Object.FindObjectsByType<SpectralAnimal>().Length==15,"Fifteen reactive spectral animals instantiated");
            Check(!UnityEngine.Object.FindObjectsByType<Transform>().Any(t=>t.name.StartsWith("ENV_BrokenColumn")||t.name.StartsWith("ENV_PartialArch")||t.name.StartsWith("Court ruin")||t.name.StartsWith("Deep ruin")),"Ruined architecture removed");
            Check(!G.CanStand(new Vector3(0,0,14))&&!Physics.Raycast(new Vector3(0,2,14),Vector3.down,4),"Bridge gap has no hidden floor");
            Check(G.cameraRig.GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing,"Post-processing enabled on gameplay camera");
            string pristine=G.ExportSave();
            foreach(var layout in F.Layouts)
            {
                var d=layout.definition;var duplicate=ForestLayoutGenerator.Generate(d,F.State.seed,Landscape.Shores[d.polygonIndex],F.configuration.trees.Length);
                Check(duplicate.particles.SequenceEqual(layout.particles)&&duplicate.trees.Select(t=>t.position).SequenceEqual(layout.trees.Select(t=>t.position)),"Deterministic layout for "+d.id);
                var nav=new ForestNavigation(Landscape.Shores[d.polygonIndex],layout,false);
                Check(layout.particles.Count==24&&layout.particles.All(p=>nav.Reachable(d.wisdomPosition+Vector3.right*2,p)),"Every particle is reachable on "+d.id);
                Check(layout.trees.All(t=>!ForestHabitat.MainPath(t.position,d))&&layout.trees.Any(t=>Mathf.Abs(t.position.x)<17),"Connected hauling paths and interior trees on "+d.id);
                report.AppendLine("COUNT: "+d.id+" trees="+layout.trees.Count+" animals="+layout.animals.Count+" particles="+layout.particles.Count);
            }
            for(int n=0;n<5;n++)
            {
                var polygon=Landscape.Shores[0].Select(p=>p+new Vector2(n*110,0)).ToArray();
                var d=new IslandForestDefinition{id="synthetic-"+n,polygonIndex=n,wisdomPosition=new Vector3(n*110-12,0,-3),treeCount=6,animalCount=2};
                var layout=ForestLayoutGenerator.Generate(d,1907,polygon,6);Check(layout.particles.Count==24,"Five-island configuration supports index "+n);
            }
            CollectThree(0);int inventory=F.Inventory("island-1").inventory;F.TryCollect(0);Check(F.Inventory("island-1").inventory==inventory,"Collected particle cannot be counted twice");
            Teleport(F.WisdomTrees[1].transform.position+Vector3.right*2);Check(!F.WisdomTrees[1].BeginCharge()&&F.Inventory("island-1").inventory==3,"Island energy cannot charge another island");
            var saved=G.ExportSave();Check(G.ImportSave(saved)&&F.Inventory("island-1").inventory==3&&F.Inventory("island-1").slots.Count(s=>s.remaining>0)==3,"Particle inventories and cooldowns survive reload");
            var beforeInvalid=StableSave();var invalid=JsonUtility.FromJson<GameManager.SaveData>(saved);invalid.forest.islands[0].inventory=-1;Check(!G.ImportSave(JsonUtility.ToJson(invalid))&&StableSave()==beforeInvalid,"Invalid forest data rejected before any state mutation");
            invalid=JsonUtility.FromJson<GameManager.SaveData>(saved);invalid.forest.islands[0].slots[1].id=invalid.forest.islands[0].slots[0].id;Check(!G.ImportSave(JsonUtility.ToJson(invalid)),"Duplicate particle IDs rejected");
            var tree0=F.WisdomTrees[0];Teleport(tree0.transform.position+Vector3.right*2);Check(tree0.BeginCharge(),"Charging begins");var pending=G.ExportSave();Check(G.ImportSave(pending)&&F.Inventory("island-1").inventory==3&&G.Blocks.Count==0&&!F.WisdomTrees[0].Charging,"Saving mid-charge restores pre-charge state");
            var obstacles=new List<GameObject>();foreach(var offset in F.configuration.islands[0].outputOffsets){var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.position=F.configuration.islands[0].wisdomPosition+offset;obstacles.Add(cube);}Physics.SyncTransforms();
            Check(!F.WisdomTrees[0].BeginCharge()&&F.Inventory("island-1").inventory==3,"Blocked output consumes no particles");foreach(var o in obstacles)ForestWorld.Remove(o);Physics.SyncTransforms();
            GameHUD.Current.ShowPause();float cooldown=F.Inventory("island-1").slots[0].remaining;F.Tick(10,false);Check(F.Inventory("island-1").slots[0].remaining==cooldown,"Pause freezes particle cooldowns");GameHUD.Current.HandleEscape();
            var first=Charge(0);Check(first.tier==0&&first.GetComponent<BlockArt>()!=null,"Tree output preserves existing block art and tier");
            double souls=G.souls;Check(!G.Purchase("stone")&&!G.Purchase("grave")&&G.souls==souls,"Retired paid supply cannot create blocks or spend Souls");
            Place(first,0,0);Check(G.souls==25&&G.PathRate==1,"First bridge reward and Soul production unchanged");
            for(int row=1;row<7;row++){CollectThree(0);Place(Charge(0),0,row);}
            Check(G.Bridges[0],"First bridge completes entirely from collected particles");
            G.Advance(30);double rate=G.Rate;Check(G.Purchase("generator")&&G.Rate>rate,"Soul Well still uses Souls and adds income");
            Check(G.Purchase("strength")&&G.strength==1,"Existing Strength upgrade still costs Souls");
            for(int row=0;row<7;row++){CollectThree(1);Place(Charge(1),1,row);}
            Check(G.Bridges[1]&&G.ConnectedCount==14,"Second bridge completes with existing Grave blocks");
            Teleport(new Vector3(0,0,55));G.Interact();Check(G.completed,"Exit beacon completes the current chapter");CollectThree(0);Charge(0);Check(G.completed,"Wisdom Trees still charge after chapter completion");
            saved=G.ExportSave();Check(G.ImportSave(saved)&&G.ConnectedCount==14&&G.generator==1&&G.strength==1,"Version 3 restores bridges, skills and forest state");
            var legacy=JsonUtility.FromJson<GameManager.SaveData>(saved);legacy.version=1;legacy.forest=null;var oldSouls=legacy.souls;Check(G.ImportSave(JsonUtility.ToJson(legacy))&&G.ConnectedCount==14&&G.souls==oldSouls&&F.State.seed==1907&&F.State.islands.All(i=>i.inventory==0),"Version 1 migration preserves economy and blocks");
            G.savePathOverride=Path.GetFullPath("Library/Forest-test-save.json");G.persistenceEnabled=true;
            try{Check(G.Save(),"Version 3 save writes atomically");G.souls+=1;Check(G.Save()&&File.Exists(G.SavePath+".bak"),"Recovery backup retained");File.WriteAllText(G.SavePath,"corrupt");G.Load();Check(G.ConnectedCount==14,"Corrupt primary recovers from backup");}finally{G.persistenceEnabled=false;G.savePathOverride=null;}
            Check(G.ImportSave(pristine),"Restore clean scene for visual captures");
            Regression(pristine);
            foreach(var animal in UnityEngine.Object.FindObjectsByType<SpectralAnimal>())
            {var start=animal.transform.position;var playerPoint=new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back}.Select(v=>start+v*2).First(p=>F.IslandAt(p)==F.IslandAt(start));Teleport(playerPoint);animal.Simulate(.21f);Check(animal.State==SpectralAnimal.Mode.Flee||animal.State==SpectralAnimal.Mode.Recover,"Wildlife reacts safely: "+animal.name+" at "+start+" ("+animal.State+")");}
            TwilightRegression(pristine);
            report.AppendLine("SUCCESS: spectral forest gameplay and persistence checks passed.");File.WriteAllText(Result,report.ToString());
        }
        static void TwilightRegression(string pristine)
        {
            Check(UnityEngine.Object.FindObjectsByType<ForestCreature>().Length==25,"Ten spectral rabbits and fifteen ground birds augment existing wildlife");
            Check(UnityEngine.Object.FindObjectsByType<ForestCoverRenderer>().Sum(c=>c.InstanceCount)>2500,"Instanced ground cover spans the islands");
            var inv=F.Inventory("island-1");var slot=inv.slots[0];var old=slot.position;slot.remaining=.1f;Teleport(new Vector3(0,0,3));F.Tick(.2f,false);F.Tick(.6f,false);
            Check(slot.remaining==0&&!slot.pending&&slot.generation==1&&(slot.position-old).sqrMagnitude>=4,"Renewed collectible moves to a safe new position");
            var save=G.ExportSave();var pos=slot.position;Check(G.ImportSave(save)&&F.Inventory("island-1").slots[0].position==pos&&F.Inventory("island-1").slots[0].generation==1,"Randomized position and generation persist on reload");
            var v2=JsonUtility.FromJson<GameManager.SaveData>(save);v2.version=2;v2.forest.layoutVersion=1;v2.forest.islands[0].inventory=7;v2.forest.islands[0].slots[1].remaining=18;
            Check(G.ImportSave(JsonUtility.ToJson(v2))&&F.State.layoutVersion==2&&F.Inventory("island-1").inventory==7&&F.Inventory("island-1").slots[1].remaining==18,"V2 migration preserves inventory and cooldowns");
            Check(G.ImportSave(pristine),"Reset for wildlife movement checks");
            var animals=UnityEngine.Object.FindObjectsByType<ForestCreature>();var bird=animals.First(a=>a.name.Contains("Bird"));var start=bird.transform.position;Teleport(start+Vector3.back*2);
            bool tookOff=false,landed=false;for(int i=0;i<1000;i++){bird.Simulate(.05f);if(bird.Airborne)tookOff=true;if(tookOff){Teleport(start+Vector3.back*10);if(!bird.Airborne){landed=true;break;}}}
            Check(tookOff&&landed,"Ground bird takes off and returns to a safe landing");
            var rabbit=animals.First(a=>a.name.Contains("Rabbit"));start=rabbit.transform.position;Teleport(start+Vector3.back*2);for(int i=0;i<30;i++)rabbit.Simulate(.05f);Check((rabbit.transform.position-start).sqrMagnitude>.1f,"Rabbit flees an approaching player");
            GameHUD.Current.ShowPause();var frozen=rabbit.transform.position;for(int i=0;i<20;i++)rabbit.Simulate(.1f);Check(rabbit.transform.position==frozen,"Rabbit simulation respects pause");GameHUD.Current.HandleEscape();
            var mixer=Resources.Load<UnityEngine.Audio.AudioMixer>("Forest/ForestMix");Check(mixer!=null&&mixer.FindMatchingGroups("Footsteps").Length==1&&mixer.FindMatchingGroups("Ambience").Length==1,"Forest audio mixer groups imported");
            Check(Resources.Load<AudioClip>("Audio/Forest/step_0_5")!=null&&Resources.Load<AudioClip>("Audio/Forest/step_1_5")!=null&&Resources.Load<AudioClip>("Audio/Forest/step_2_5")!=null,"Six variations exist for all footstep surfaces");
            Check(G.ImportSave(pristine),"Reset after twilight tests");
        }
        static void Regression(string pristine)
        {
            var inv=F.Inventory("island-1");inv.inventory=30;Teleport(F.Layouts[0].particles[0]);Check(!F.TryCollect(0)&&inv.slots[0].remaining==0,"Full inventory leaves pickup available");inv.inventory=0;
            Check(F.TryCollect(0),"Pickup becomes collectable after inventory has space");var clock=inv.slots[0].remaining;F.Tick(5,false);Check(inv.slots[0].remaining==clock-5,"Particle renewal uses simulation time");
            string inventory=G.ExportSave();F.Build();Check(G.ExportSave().Contains("island-1")&&F.Inventory("island-1").inventory==1&&F.Inventory("island-1").slots[0].remaining==25&&UnityEngine.Object.FindObjectsByType<WisdomTree>().Length==3&&UnityEngine.Object.FindObjectsByType<SpectralAnimal>().Length==15,"Visual rebuild preserves state without duplicate live systems");
            var invalid=JsonUtility.FromJson<GameManager.SaveData>(inventory);invalid.version=4;Check(!G.ImportSave(JsonUtility.ToJson(invalid)),"Future save versions rejected");invalid.version=2;invalid.forest.islands[0].id="unknown";Check(!G.ImportSave(JsonUtility.ToJson(invalid)),"Unknown island save IDs rejected");invalid=JsonUtility.FromJson<GameManager.SaveData>(inventory);invalid.forest.islands[0].slots[0].remaining=-1;Check(!G.ImportSave(JsonUtility.ToJson(invalid)),"Negative cooldown rejected");
            var fixture=JsonUtility.FromJson<GameManager.SaveData>(pristine);fixture.souls=5000;fixture.version=1;fixture.forest=null;
            fixture.blocks.Add(new GameManager.BlockSave{id="fixture-a",tier=0,position=new Vector3(-3,.26f,1)});fixture.blocks.Add(new GameManager.BlockSave{id="fixture-b",tier=0,position=new Vector3(-1.4f,.26f,1)});fixture.blocks.Add(new GameManager.BlockSave{id="fixture-heavy",tier=1,position=new Vector3(-3,.26f,-3)});fixture.blocks.Add(new GameManager.BlockSave{id="fixture-obsidian",tier=2,position=new Vector3(-7,.26f,-3)});
            Check(G.ImportSave(JsonUtility.ToJson(fixture)),"Version 1 loose-block regression fixture imports without relocation");
            var player=G.player;var heavy=G.Blocks[2];Teleport(heavy.transform.position+Vector3.back*1.2f);Check(!player.TryGrab(heavy.Box),"Strength requirement remains enforced");Check(G.Purchase("strength")&&player.TryGrab(heavy.Box),"Strength I unlocks Grave hauling");player.Release();
            float pull=player.pullSpeed;Check(G.Purchase("speed")&&player.pullSpeed>pull,"Speed upgrade improves hauling");float range=G.soulLight.range;Check(G.Purchase("radiance")&&G.soulLight.range==range+3,"Radiance upgrade expands illumination");
            Teleport(new Vector3(-3,0,-.5f));Check(player.TryGrab(G.Blocks[0].Box),"Ordinary block can be hauled");player.Release();Check(G.Purchase("links")&&G.Capacity==2,"Soul Links upgrade preserves capacity");
            G.Blocks[0].transform.position=new Vector3(-3,.26f,1);G.Blocks[0].Box.Body.position=G.Blocks[0].transform.position;G.Blocks[1].transform.position=new Vector3(-1.4f,.26f,1);G.Blocks[1].Box.Body.position=G.Blocks[1].transform.position;Physics.SyncTransforms();
            Check(player.TryGrab(G.Blocks[0].Box)&&player.Orbiting&&player.HeldBoxes.Count==2,"Soul Links gathers two nearby blocks into orbit");var old=G.Blocks[0].transform.position;player.StepMovement(Vector2.up,.1f);Check(G.Blocks[0].transform.position!=old,"Orbit follows player movement");
            GameHUD.Current.ShowPause();Check(!player.Orbiting&&player.HeldBoxes.Count==0,"Pause safely releases orbiting blocks");GameHUD.Current.HandleEscape();
            GameHUD.Current.ToggleSkills();var skills=GameHUD.Current.GetComponentInChildren<SkillTree>(true);Check(skills.transform.Find("Power stone")==null&&skills.transform.Find("Power grave")==null&&skills.transform.Find("Power strength")!=null,"Skill tree preserves powers and removes paid block supply");GameHUD.Current.HandleEscape();
            var capped=JsonUtility.FromJson<GameManager.SaveData>(pristine);for(int n=0;n<35;n++)capped.blocks.Add(new GameManager.BlockSave{id="cap-"+n,tier=0,position=new Vector3(18+n%7*2,.26f,-20+n/7*2)});Check(G.ImportSave(JsonUtility.ToJson(capped)),"Loose-block cap fixture loads");F.Inventory("island-1").inventory=3;Teleport(F.configuration.islands[0].wisdomPosition+Vector3.right*2);Check(!F.WisdomTrees[0].BeginCharge()&&F.Inventory("island-1").inventory==3,"35 loose blocks prevent charging without spending particles");
            Check(G.ImportSave(pristine),"Regression fixtures removed before visual review");
        }
        static IEnumerator Captures()
        {
            string folder=Path.GetFullPath("Library/SpectralForestCaptures");Directory.CreateDirectory(folder);
            for(int island=0;island<F.configuration.islands.Length;island++)
            {
                var p=F.configuration.islands[island].wisdomPosition;Teleport(p+new Vector3(3,0,-2));G.cameraRig.CenterOnPlayer();
                foreach(float distance in new[]{14f,27f,42f})
                {
                    G.cameraRig.distance=distance;for(int i=0;i<20;i++){G.cameraRig.SendMessage("LateUpdate");yield return null;}
                    CaptureCamera(G.cameraRig.GetComponent<Camera>(),folder+"/island-"+(island+1)+"-"+distance+".png");
                }
            }
            Teleport(new Vector3(-10,.025f,-5));G.cameraRig.distance=14;G.cameraRig.CenterOnPlayer();for(int i=0;i<15;i++){G.cameraRig.SendMessage("LateUpdate");yield return null;}
            ScreenCapture.CaptureScreenshot(folder+"/gameplay.png");yield return null;
            report.AppendLine("CAPTURES: "+folder);Finish();
        }
        public static void CaptureCamera(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=camera.targetTexture;var active=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.Destroy(image);}finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);}
        }
        static void Finish(){File.WriteAllText(Result,report.ToString());running=false;EditorApplication.isPlaying=false;if(SessionState.GetBool("Twilight.Batch",false)){SessionState.SetBool("Twilight.Batch",false);EditorApplication.Exit(report.ToString().Contains("FAIL:")?1:0);}}
    }
}
