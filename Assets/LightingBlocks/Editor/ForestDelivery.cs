using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    [InitializeOnLoad] public static class ForestDelivery
    {
        const string BuildReq="Library/LightingBlocks.build-request",BuildRes="Library/LightingBlocks.build-result.txt";
        const string BuildDir="Builds/SpectralForestPerformance",ExeName="LightingBlocks.exe";
        static ForestDelivery(){EditorApplication.update+=Poll;EditorApplication.update+=PollBuild;}
        static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
            if(File.Exists("Library/SpectralForest.delivery-request"))
            {
                File.Delete("Library/SpectralForest.delivery-request");
                try{Inspection();Build();}catch(Exception e){File.WriteAllText("Library/SpectralForest.delivery-result.txt",e.ToString());Debug.LogException(e);}
            }
        }
        static void PollBuild()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||!File.Exists(BuildReq))return;
            File.Delete(BuildReq);
            try{Build();File.WriteAllText(BuildRes,"SUCCESS: "+Path.GetFullPath(BuildDir)+"/"+ExeName);}
            catch(Exception e){File.WriteAllText(BuildRes,e.ToString());Debug.LogException(e);}
        }
        [MenuItem("Tools/Lighting Blocks/Build Forest Verification Player")]
        public static void Request(){File.WriteAllText("Library/SpectralForest.delivery-request","build");}
        static void Inspection()
        {
            var original=EditorSceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                EditorSceneManager.SetActiveScene(scene);
                var config=AssetDatabase.LoadAssetAtPath<ForestConfiguration>(ForestBuilder.Root+"/Configuration/Forest.asset");
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Forest floor material inspection";ground.transform.localScale=new Vector3(6,1,3);ground.GetComponent<Renderer>().sharedMaterial=config.ground;
                var wisdom=(GameObject)PrefabUtility.InstantiatePrefab(config.wisdomPrefab);wisdom.transform.position=new Vector3(-5,0,0);
                for(int i=0;i<config.trees.Length;i++){var tree=(GameObject)PrefabUtility.InstantiatePrefab(config.trees[i]);tree.transform.position=new Vector3(-15+i*6,0,8);}
                for(int i=0;i<config.animals.Length;i++){var animal=(GameObject)PrefabUtility.InstantiatePrefab(config.animals[i]);animal.transform.position=new Vector3(1+i*3,0,-1);}
                var camera=new GameObject("Inspection camera",typeof(Camera));camera.transform.position=new Vector3(15,13,-20);camera.transform.LookAt(new Vector3(0,2,2));camera.GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing=true;camera.GetComponent<Camera>().backgroundColor=new Color(.035f,.04f,.065f);camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;
                var light=new GameObject("Inspection moon",typeof(Light));light.transform.rotation=Quaternion.Euler(45,-35,0);light.GetComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=.6f;light.GetComponent<Light>().color=new Color(.7f,.8f,1);
                var volume=new GameObject("Inspection bloom",typeof(Volume)).GetComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.25f,.29f,.4f);
                EditorSceneManager.SaveScene(scene,ForestBuilder.Root+"/ForestArtInspection.unity");
            }
            finally{EditorSceneManager.CloseScene(scene,true);EditorSceneManager.SetActiveScene(original);}
        }
        static void Build()
        {
            string directory=Path.GetFullPath(BuildDir);Directory.CreateDirectory(directory);
            bool prior=PlayerSettings.enableFrameTimingStats;PlayerSettings.enableFrameTimingStats=true;
            try
            {
                File.WriteAllText("Library/SpectralForest.delivery-result.txt","BUILDING Windows development player");
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/SampleScene.unity"},locationPathName=directory+"/"+ExeName,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
                if(result==null)throw new Exception("Unity returned no build report. Inspect the preceding build error in the Console.");
                File.WriteAllText("Library/SpectralForest.delivery-result.txt",result.summary.result+"; errors="+result.summary.totalErrors+"; warnings="+result.summary.totalWarnings+"; size="+result.summary.totalSize+"; duration="+result.summary.totalTime+"; path="+directory);
                if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Windows development build failed: "+result.summary.result);
            }
            finally{PlayerSettings.enableFrameTimingStats=prior;}
        }
    }
}
