using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace LightingBlocks.Editor
{
    public static class TwilightBatch
    {
        public static void Apply(){TwilightBuilder.Apply();Debug.Log("TWILIGHT APPLY SUCCESS");}
        public static void ApplyAndBuild(){Apply();Build();}
        public static void Build()
        {
            PlayerSettings.enableFrameTimingStats=true;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/SampleScene.unity"},locationPathName="Builds/TwilightForest/LightingBlocks.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            File.WriteAllText("Library/Twilight-build.txt",report.summary.result+" errors="+report.summary.totalErrors+" warnings="+report.summary.totalWarnings+" bytes="+report.summary.totalSize);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Twilight build failed");
        }
        public static void CheckLayouts()
        {
            var config=AssetDatabase.LoadAssetAtPath<ForestConfiguration>(ForestBuilder.Root+"/Configuration/Forest.asset");int count=0;
            var report=new System.Text.StringBuilder();
            foreach(var mesh in config.coverMeshes){var colors=mesh.colors;report.AppendLine(mesh.name+" vertices="+mesh.vertexCount+" colors="+colors.Length+" example="+(colors.Length>0?colors[0].ToString():"missing"));}
            foreach(var seed in Enumerable.Range(1907,100))foreach(var d in config.islands){var l=ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],config.trees.Length);var again=ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],config.trees.Length);
                if(!l.trees.Select(t=>t.position).SequenceEqual(again.trees.Select(t=>t.position))||!l.particles.SequenceEqual(again.particles))throw new Exception("Non-deterministic "+seed);
                if(!l.trees.Any(t=>Mathf.Abs(t.position.x)<17))throw new Exception("No interior trees "+d.id+" "+seed);
                if(l.trees.Any(t=>ForestHabitat.MainPath(t.position,d)||ForestHabitat.Landing(t.position,d)||ForestLayoutGenerator.Reserved(t.position,d,5)))throw new Exception("Reserved tree "+seed);
                if(l.particles.Count!=24||l.particles.Any(p=>!l.navigation.InReachableArea(p)))throw new Exception("Unreachable particle "+seed);
                if(l.animals.Count!=d.animalCount+d.rabbitCount+d.birdCount)throw new Exception("Species count "+seed);
                count++;report.AppendLine("PASS seed="+seed+" island="+d.id+" trees="+l.trees.Count+" cover="+l.cover.Count+" animals="+l.animals.Count);
            }
            File.WriteAllText("Library/Twilight-layout-tests.txt",report+"\nPASS "+count+" seeded island configurations");Debug.Log("TWILIGHT LAYOUT CHECKS PASS "+count);
        }
    }
}
