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
            // Recomputed here on purpose: the quota rule is asserted independently of the generator that applies it.
            int Eligible(IslandForestDefinition d,Vector2[] poly)
            {
                var min=poly[0];var max=min;foreach(var v in poly){min=Vector2.Min(min,v);max=Vector2.Max(max,v);}
                int cells=0;for(float x=min.x+1;x<max.x;x+=2)for(float z=min.y+1;z<max.y;z+=2){var p=new Vector3(x,0,z);
                    if(ForestLayoutGenerator.Inside(p,poly,3)&&!ForestLayoutGenerator.Reserved(p,d,5)&&!ForestHabitat.MainPath(p,d)&&!ForestHabitat.Landing(p,d))cells++;}
                return cells;
            }
            foreach(var seed in Enumerable.Range(1907,100))foreach(var d in config.islands){var l=ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],config.trees.Length);var again=ForestLayoutGenerator.Generate(d,seed,Landscape.Shores[d.polygonIndex],config.trees.Length);
                if(!l.trees.Select(t=>t.position).SequenceEqual(again.trees.Select(t=>t.position))||!l.particles.SequenceEqual(again.particles))throw new Exception("Non-deterministic "+seed);
                if(!l.trees.Any(t=>Mathf.Abs(t.position.x)<17))throw new Exception("No interior trees "+d.id+" "+seed);
                if(l.trees.Any(t=>ForestHabitat.MainPath(t.position,d)||ForestHabitat.Landing(t.position,d)||ForestLayoutGenerator.Reserved(t.position,d,5)))throw new Exception("Reserved tree "+seed);
                if(l.particles.Count!=24||l.particles.Any(p=>!l.navigation.InReachableArea(p)))throw new Exception("Unreachable particle "+seed);
                if(l.animals.Count!=d.animalCount+d.rabbitCount+d.birdCount)throw new Exception("Species count "+seed);
                var poly=Landscape.Shores[d.polygonIndex];int eligible=Eligible(d,poly),expected=Mathf.Min(d.treeCount,Mathf.RoundToInt(eligible*4/45f));
                if(l.trees.Count!=expected)throw new Exception("Tree quota "+d.id+" "+seed+" got "+l.trees.Count+" expected "+expected);
                if(l.trees.Any(t=>!ForestLayoutGenerator.Inside(t.position,poly,3)))throw new Exception("Tree inside edge setback "+d.id+" "+seed);
                for(int i=0;i<l.trees.Count;i++)for(int j=i+1;j<l.trees.Count;j++)if((l.trees[i].position-l.trees[j].position).sqrMagnitude<12.2f)throw new Exception("Tree spacing under 3.5m "+d.id+" "+seed);
                if(l.cover.Count==0||l.cover.Any(c=>c.variant<0||c.variant>=config.coverMeshes.Length))throw new Exception("Cover records "+d.id+" "+seed);
                if(l.cover.Any(c=>!ForestLayoutGenerator.Inside(c.position,poly,.5f)||ForestLayoutGenerator.Reserved(c.position,d,2.3f)))throw new Exception("Cover in reservation "+d.id+" "+seed);
                foreach(var offset in d.outputOffsets)if(!l.navigation.InReachableArea(d.wisdomPosition+offset))throw new Exception("Unreachable output "+d.id+" "+seed);
                count++;report.AppendLine("PASS seed="+seed+" island="+d.id+" trees="+l.trees.Count+"/"+expected+" eligible="+eligible*4+"m2 cover="+l.cover.Count+" animals="+l.animals.Count);
            }
            File.WriteAllText("Library/Twilight-layout-tests.txt",report+"\nPASS "+count+" seeded island configurations");Debug.Log("TWILIGHT LAYOUT CHECKS PASS "+count);
        }
    }
}
