using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
namespace LightingBlocks
{
    public sealed class TwilightEvidence:MonoBehaviour
    {
        public static bool Requested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-twilight-evidence")>=0;
        GameManager game;ForestWorld forest;string folder;
        public static void Capture(Camera camera,string path,bool hud=true)
        {
            var target=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=camera.targetTexture;var active=RenderTexture.active;
            var canvases=FindObjectsByType<Canvas>();
            try{foreach(var canvas in canvases){canvas.enabled=hud;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());Destroy(image);}
            finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);foreach(var canvas in canvases){canvas.enabled=true;canvas.renderMode=RenderMode.ScreenSpaceOverlay;}}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Begin(){if(Requested)new GameObject("Twilight evidence").AddComponent<TwilightEvidence>();}
        IEnumerator Start()
        {
            if(Application.isEditor)yield break;Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Evidence"));Directory.CreateDirectory(folder);
            yield return null;game=GameManager.Instance;forest=ForestWorld.For(game);game.persistenceEnabled=false;game.player.readInput=false;game.cameraRig.readInput=false;GameHUD.Current.BeginJourney();forest.Restore(forest.NewState(1907));game.strength=3;game.ApplyUpgrades();
            for(int gap=0;gap<2;gap++)for(int row=0;row<7;row++){if(!game.TrySpawnAtWisdomTree(forest.configuration.islands[gap].id,out var block))throw new Exception("Evidence block unavailable");block.transform.position=GameManager.Cell(gap,0,row)+Vector3.up*.54f;block.Box.Body.position=block.transform.position;Physics.SyncTransforms();if(!game.TryPlace(block))throw new Exception("Evidence bridge placement failed");}
            for(int island=0;island<3;island++){
                var d=forest.configuration.islands[island];Move(d.wisdomPosition+new Vector3(3,0,-3));forest.Inventory(d.id).inventory=3;forest.WisdomTrees[island].BeginCharge();
                foreach(float distance in new[]{14f,27f,42f}){game.cameraRig.distance=distance;game.cameraRig.CenterOnPlayer();yield return new WaitForSeconds(1);Capture(game.cameraRig.GetComponent<Camera>(),folder+"/island-"+(island+1)+"-"+distance+"-hud.png");yield return new WaitForSeconds(.2f);var canvas=GameHUD.Current.GetComponentInChildren<Canvas>();if(canvas!=null)canvas.enabled=false;yield return new WaitForEndOfFrame();Capture(game.cameraRig.GetComponent<Camera>(),folder+"/island-"+(island+1)+"-"+distance+".png",false);yield return new WaitForSeconds(.2f);if(canvas!=null)canvas.enabled=true;}
            }
            Move(new Vector3(1,0,6));game.cameraRig.distance=14;game.cameraRig.CenterOnPlayer();yield return new WaitForSeconds(1);Capture(game.cameraRig.GetComponent<Camera>(),folder+"/crystal-bridge.png");
            var creatures=FindObjectsByType<ForestCreature>();var bird=creatures.FirstOrDefault(a=>a.name.Contains("Bird")&&forest.IslandAt(a.transform.position)?.polygonIndex==0);
            if(bird!=null){var origin=bird.transform.position;Move(origin+Vector3.back*6);game.cameraRig.distance=14;game.cameraRig.CenterOnPlayer();yield return new WaitForSeconds(1);Directory.CreateDirectory(folder+"/frames");
                Time.captureDeltaTime=1f/20;
                using(var audio=new TwilightAudioCapture(folder+"/demonstration.wav")){
                    for(int frame=0;frame<400;frame++){if(frame<80)Move(Vector3.Lerp(origin+Vector3.back*6,origin+Vector3.back*2,frame/80f));if(frame==120)Move(origin+Vector3.back*10);yield return new WaitForEndOfFrame();Capture(game.cameraRig.GetComponent<Camera>(),folder+"/frames/frame-"+frame.ToString("D5")+".png",false);audio.Frame();}
                    var rabbit=creatures.First(a=>a.name.Contains("Rabbit")&&forest.IslandAt(a.transform.position)?.polygonIndex==0);origin=rabbit.transform.position;Move(origin+Vector3.back*6);game.cameraRig.CenterOnPlayer();
                    for(int frame=400;frame<560;frame++){if(frame<480)Move(Vector3.Lerp(origin+Vector3.back*6,origin+Vector3.back*2,(frame-400)/80f));yield return new WaitForEndOfFrame();Capture(game.cameraRig.GetComponent<Camera>(),folder+"/frames/frame-"+frame.ToString("D5")+".png",false);audio.Frame();}
                    Move(new Vector3(-2,0,-9));game.cameraRig.CenterOnPlayer();
                    for(int frame=560;frame<720;frame++){game.player.StepMovement(frame<640?Vector2.up:Vector2.down,.05f);yield return new WaitForEndOfFrame();Capture(game.cameraRig.GetComponent<Camera>(),folder+"/frames/frame-"+frame.ToString("D5")+".png",false);audio.Frame();}
                    Move(forest.configuration.islands[0].wisdomPosition+Vector3.right*2);game.cameraRig.CenterOnPlayer();
                    for(int frame=720;frame<880;frame++){if(frame%50==20){forest.Inventory("island-1").inventory=3;forest.WisdomTrees[0].BeginCharge();}yield return new WaitForEndOfFrame();Capture(game.cameraRig.GetComponent<Camera>(),folder+"/frames/frame-"+frame.ToString("D5")+".png",false);audio.Frame();}
                    Move(new Vector3(0,0,6));game.cameraRig.CenterOnPlayer();
                    for(int frame=880;frame<1040;frame++){game.player.StepMovement(Vector2.up,.05f);yield return new WaitForEndOfFrame();Capture(game.cameraRig.GetComponent<Camera>(),folder+"/frames/frame-"+frame.ToString("D5")+".png",false);audio.Frame();}
                }Time.captureDeltaTime=0;
            }
            File.WriteAllText(folder+"/complete.txt","Captured all three islands, crystal bridges and bird encounter; fixture isolated from user saves.");yield return new WaitForSeconds(1);Application.Quit();
        }
        void Move(Vector3 p){var cc=game.player.GetComponent<CharacterController>();cc.enabled=false;game.player.transform.position=new Vector3(p.x,Landscape.Height(p)+.025f,p.z);cc.enabled=true;}
    }
}
