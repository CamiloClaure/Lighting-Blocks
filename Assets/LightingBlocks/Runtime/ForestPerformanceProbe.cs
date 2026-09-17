using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
namespace LightingBlocks
{
    // Opt-in development-player fixture. It never reads or writes the player's save.
    public sealed class ForestPerformanceProbe : MonoBehaviour
    {
        public static bool Measuring;
        public static long ForestBytes,WildlifeBytes;
        public static bool Requested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-forest-profile")>=0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartProbe(){if(Requested)new GameObject("Forest performance route").AddComponent<ForestPerformanceProbe>();}
        [Serializable] struct Sample{public double elapsed,cpuMs,gpuMs;public float frameMs;public long drawCalls,batches,setPassCalls,triangles,allocatedBytes,forestBytes,wildlifeBytes;public int island;}
        [Serializable] class Report{public string cpu,gpu,os,unity,quality,note;public int width,height,memoryMB;public double meanFrameMs,p95FrameMs,meanCpuMs,meanGpuMs,meanDrawCalls,meanTriangles,meanAllocatedBytes;public bool cpuTimingAvailable,gpuTimingAvailable;public int frames;public List<Sample> samples;}
        GameManager game;ForestWorld forest;readonly FrameTiming[] timing=new FrameTiming[1];
        readonly List<Sample> samples=new List<Sample>(16000);string folder;
        IEnumerator Start()
        {
            if(Application.isEditor){Destroy(gameObject);yield break;}
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            var args=Environment.GetCommandLineArgs();bool clean=Array.IndexOf(args,"-forest-clean")>=0,stress=Array.IndexOf(args,"-forest-stress")>=0;string run=args.FirstOrDefault(a=>a.StartsWith("-forest-run="))?.Substring(12)??"Profile";if(run.IndexOfAny(Path.GetInvalidFileNameChars())>=0)run="Profile";
            folder=Path.Combine(Application.dataPath,"../"+run);Directory.CreateDirectory(folder);
            yield return null;game=GameManager.Instance;forest=ForestWorld.For(game);game.persistenceEnabled=false;game.player.readInput=false;game.cameraRig.readInput=false;GameHUD.Current.BeginJourney();
            forest.Restore(forest.NewState(1907));game.strength=3;game.ApplyUpgrades();
            var camera=game.cameraRig.GetComponent<Camera>();camera.targetTexture=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);camera.enabled=false;foreach(var canvas in FindObjectsByType<Canvas>()){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            // Completed bridge fixture exercises the existing placement, lighting and income paths.
            for(int gap=0;gap<2;gap++)for(int row=0;row<7;row++)
            {if(!game.TrySpawnAtWisdomTree(forest.configuration.islands[gap].id,out var block))throw new Exception("Profile fixture spawn failed");block.transform.position=GameManager.Cell(gap,0,row)+Vector3.up*.54f;block.Box.Body.position=block.transform.position;Physics.SyncTransforms();if(!game.TryPlace(block))throw new Exception("Profile fixture placement failed");}
            if(stress)for(int n=0;n<35;n++){if(!game.TrySpawnAtWisdomTree("island-1",out var block))throw new Exception("Stress population failed");block.transform.position=new Vector3(-24+(n%7)*2,.26f,-20+(n/7)*2);block.Box.Body.position=block.transform.position;Physics.SyncTransforms();}
            var reactive=UnityEngine.Object.FindObjectsByType<ForestCreature>();
            Move(forest.configuration.islands[0].wisdomPosition+Vector3.right*2);game.cameraRig.distance=27;
            for(int i=0;i<120;i++)yield return null;
            var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);File.WriteAllLines(Path.Combine(folder,"render-counters.txt"),handles.Select(h=>ProfilerRecorderHandle.GetDescription(h)).Where(d=>d.Name.Contains("Draw")||d.Name.Contains("Batch")||d.Name.Contains("Triangle")||d.Name.Contains("SetPass")).Select(d=>d.Category.Name+": "+d.Name+" ("+d.UnitType+")"));
            using(var standard=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Standard Draw Calls Count"))
            using(var standardIndirect=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Standard Indirect Draw Calls Count"))
            using(var instanced=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Standard Instanced Draw Calls Count"))
            using(var srp=ProfilerRecorder.StartNew(ProfilerCategory.Render,"SRP Batcher Draw Calls Count"))
            using(var brg=ProfilerRecorder.StartNew(ProfilerCategory.Render,"BRG Draw Calls Count"))
            using(var brgIndirect=ProfilerRecorder.StartNew(ProfilerCategory.Render,"BRG Indirect Draw Calls Count"))
            using(var nullGeometry=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Null Geometry Draw Calls Count"))
            using(var nullIndirect=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Null Geometry Indirect Draw Calls Count"))
            using(var batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Batches Count"))
            using(var setPass=ProfilerRecorder.StartNew(ProfilerCategory.Render,"SetPass Calls Count"))
            using(var triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count"))
            using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame"))
            {
                Measuring=true;double start=Time.realtimeSinceStartupAsDouble;int previous=-1;bool charged=false,captured=false,animalCaptured=false;
                while(Time.realtimeSinceStartupAsDouble-start<(stress?300:60))
                {
                    double elapsed=Time.realtimeSinceStartupAsDouble-start;int island=((int)(elapsed/20))%forest.configuration.islands.Length;float phase=(float)(elapsed%20);
                    if(island!=previous){previous=island;charged=false;captured=false;animalCaptured=false;}
                    var tree=forest.WisdomTrees[island];
                    if(phase<5){Move(tree.transform.position+Vector3.right*2);if(!charged){forest.Inventory(tree.Definition.id).inventory=3;tree.BeginCharge();charged=true;}}
                    else if(phase<14){var layout=forest.Layouts[island];var position=layout.animals[0];if(stress)foreach(var animal in reactive)if(animal!=null&&animal.name.Contains("Bird")&&forest.IslandAt(animal.transform.position)?.polygonIndex==island){position=animal.transform.position;break;}Move(position+Vector3.forward*Mathf.Sin(phase)*1.5f);}
                    else {var p=tree.transform.position+new Vector3(5,0,5);Move(p);game.cameraRig.distance=42;}
                    if(phase<14)game.cameraRig.distance=phase<5?14:27;game.cameraRig.CenterOnPlayer();
                    if(!clean&&!captured&&phase>3){TwilightEvidence.Capture(game.cameraRig.GetComponent<Camera>(),Path.Combine(folder,"island-"+(island+1)+"-gameplay.png"));captured=true;}
                    if(!clean&&!animalCaptured&&phase>8){TwilightEvidence.Capture(game.cameraRig.GetComponent<Camera>(),Path.Combine(folder,"island-"+(island+1)+"-wildlife.png"));animalCaptured=true;}
                    FrameTimingManager.CaptureFrameTimings();uint count=FrameTimingManager.GetLatestTimings(1,timing);
                    long draws=standard.LastValue+standardIndirect.LastValue+instanced.LastValue+srp.LastValue+brg.LastValue+brgIndirect.LastValue+nullGeometry.LastValue+nullIndirect.LastValue;
                    samples.Add(new Sample{elapsed=elapsed,frameMs=Time.unscaledDeltaTime*1000,cpuMs=count>0?timing[0].cpuFrameTime:0,gpuMs=count>0?timing[0].gpuFrameTime:0,drawCalls=draws,batches=batches.Valid?batches.LastValue:-1,setPassCalls=setPass.Valid?setPass.LastValue:-1,triangles=triangles.Valid?triangles.LastValue:-1,allocatedBytes=gc.Valid?gc.LastValue:-1,forestBytes=ForestBytes,wildlifeBytes=WildlifeBytes,island=island});ForestBytes=WildlifeBytes=0;camera.Render();yield return null;
                }
            }
            Measuring=false;var sorted=samples.Select(s=>s.frameMs).OrderBy(s=>s).ToArray();var cpu=samples.Where(s=>s.cpuMs>0).ToArray();var gpu=samples.Where(s=>s.gpuMs>0).ToArray();
            var report=new Report{cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,os=SystemInfo.operatingSystem,unity=Application.unityVersion,quality=QualitySettings.names[QualitySettings.GetQualityLevel()],width=Screen.width,height=Screen.height,memoryMB=SystemInfo.systemMemorySize,meanFrameMs=samples.Average(s=>s.frameMs),p95FrameMs=sorted[(int)(sorted.Length*.95)],cpuTimingAvailable=cpu.Length>0,gpuTimingAvailable=gpu.Length>0,meanCpuMs=cpu.Length>0?cpu.Average(s=>s.cpuMs):0,meanGpuMs=gpu.Length>0?gpu.Average(s=>s.gpuMs):0,meanDrawCalls=samples.Average(s=>(double)s.drawCalls),meanTriangles=samples.Average(s=>(double)s.triangles),meanAllocatedBytes=samples.Average(s=>(double)s.allocatedBytes),frames=samples.Count,samples=samples,note="Deterministic route, seed 1907, three islands, wildlife proximity, completed bridges. Development build; VSync off; isolated fixture; allocations include game systems and profiling. See run arguments for clean or screenshot-inclusive and stress settings."};
            File.WriteAllText(Path.Combine(folder,"performance.json"),JsonUtility.ToJson(report,true));Application.Quit();
        }
        void Move(Vector3 p){var cc=game.player.GetComponent<CharacterController>();cc.enabled=false;game.player.transform.position=new Vector3(p.x,.025f,p.z);cc.enabled=true;}
    }
}
