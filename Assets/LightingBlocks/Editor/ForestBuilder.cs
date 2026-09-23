using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    [InitializeOnLoad] public static class ForestBuilder
    {
        public const string Root="Assets/LightingBlocks/Art/SpectralForest";
        const string Request="Library/SpectralForest.apply-request";
        static ForestBuilder(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||!File.Exists(Request))return;
            File.Delete(Request);
            try{Apply();File.WriteAllText("Library/SpectralForest.apply-result.txt","SUCCESS: forest assets and scene integrated.");}
            catch(Exception e){File.WriteAllText("Library/SpectralForest.apply-result.txt",e.ToString());Debug.LogException(e);}
        }
        static T Load<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(Root+"/"+path);
        static void Save(UnityEngine.Object obj,string path)
        {var old=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);if(old==null)AssetDatabase.CreateAsset(obj,path);else{EditorUtility.CopySerialized(obj,old);UnityEngine.Object.DestroyImmediate(obj);}}
        static Color Hex(string color){ColorUtility.TryParseHtmlString(color,out var result);return result;}
        static Material Material(string name,Color tint,string texture=null)
        {
            var shader=Shader.Find("LightingBlocks/Spectral Lit");if(shader==null)throw new Exception("Spectral shader missing");
            var m=new Material(shader){name=name};m.SetColor("_BaseColor",tint);
            if(texture!=null)m.SetTexture("_BaseMap",Load<Texture2D>("Textures/"+texture));
            m.SetFloat("_Smoothness",.3f);m.enableInstancing=true;
            Save(m,Root+"/Materials/"+name+".mat");return Load<Material>("Materials/"+name+".mat");
        }
        static void ImportSettings()
        {
            foreach(var path in Directory.GetFiles(Root+"/Textures","*.png"))
            {
                var t=(TextureImporter)AssetImporter.GetAtPath(path);bool normal=path.Contains("Normal")||path.Contains("nor_gl");
                t.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                t.sRGBTexture=!normal&&!path.Contains("Mask");t.wrapMode=TextureWrapMode.Repeat;t.mipmapEnabled=true;t.maxTextureSize=2048;t.anisoLevel=4;t.alphaIsTransparency=path.Contains("Leaves")||path.Contains("Grass");t.SaveAndReimport();
            }
            foreach(var path in Directory.GetFiles(Root+"/SourceModels","*.fbx"))
            {
                var m=(ModelImporter)AssetImporter.GetAtPath(path);m.animationType=ModelImporterAnimationType.Generic;m.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                m.importAnimation=true;m.materialImportMode=ModelImporterMaterialImportMode.None;m.importCameras=false;m.importLights=false;
                var clips=m.defaultClipAnimations;foreach(var clip in clips){clip.loopTime=true;clip.lockRootRotation=true;clip.lockRootHeightY=true;clip.lockRootPositionXZ=true;}m.clipAnimations=clips;m.SaveAndReimport();
            }
        }
        static GameObject ModelPrefab(string name,float height,bool wisdom=false)
        {
            var model=Load<GameObject>("ProcessedModels/"+name+".fbx");if(model==null)throw new Exception("Missing model "+name);
            var root=new GameObject(name);var visual=UnityEngine.Object.Instantiate(model,root.transform);visual.name="Visual";
            foreach(var importedLOD in visual.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(importedLOD);
            var renderers=visual.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float scale=height/bounds.size.y;visual.transform.localScale=Vector3.one*scale;visual.transform.localPosition=Vector3.up*(-bounds.min.y*scale);
            foreach(var r in renderers)
            {
                r.sharedMaterials=r.sharedMaterials.Select(m=>Load<Material>("Materials/"+m.name+".mat")??Load<Material>("Materials/ForestBark.mat")).ToArray();
                r.shadowCastingMode=wisdom?ShadowCastingMode.On:ShadowCastingMode.Off;
            }
            var group=root.AddComponent<LODGroup>();var lods=new LOD[3];
            for(int n=0;n<3;n++)lods[n]=new LOD(n==0?.3f:n==1?.12f:.025f,renderers.Where(r=>r.name.Contains("_LOD"+n)).ToArray());
            group.SetLODs(lods);group.RecalculateBounds();
            if(wisdom)
            {
                var tree=root.AddComponent<WisdomTree>();tree.glowRenderers=renderers;
                var capsule=root.AddComponent<CapsuleCollider>();capsule.radius=.45f;capsule.height=2;capsule.center=Vector3.up;
                var lamp=new GameObject("Wisdom glow",typeof(Light));lamp.transform.SetParent(root.transform,false);lamp.transform.localPosition=Vector3.up*2.4f;
                tree.glowLight=lamp.GetComponent<Light>();tree.glowLight.type=LightType.Point;tree.glowLight.range=7;tree.glowLight.intensity=.7f;tree.glowLight.color=Hex("#F5C36A");tree.glowLight.shadows=LightShadows.None;
                tree.motes=Motes(root.transform,"Wisdom sparks",Hex("#FFE9AE"),35,4,2.8f);
                tree.release=Motes(root.transform,"Block release",Hex("#FFE9AE"),0,.6f,.4f);var emission=tree.release.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(0,18)});var main=tree.release.main;main.loop=false;main.playOnAwake=false;
                tree.chargeLines=new LineRenderer[3];
                for(int n=0;n<3;n++){var o=new GameObject("Charge streak "+n,typeof(LineRenderer));o.transform.SetParent(root.transform,false);var line=o.GetComponent<LineRenderer>();line.positionCount=2;line.startWidth=.035f;line.endWidth=.015f;line.sharedMaterial=Load<Material>("Materials/GoldMotes.mat");line.enabled=false;tree.chargeLines[n]=line;}
                var anchors=JsonUtility.FromJson<AnchorList>(File.ReadAllText(Root+"/ProcessedModels/WisdomAnchors.json"));
                var sparkMesh=new Mesh();var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
                foreach(var point in anchors.points){int n=vertices.Count;var p=point*scale;float r=.025f;vertices.AddRange(new[]{p+Vector3.up*r,p-Vector3.up*r,p+Vector3.right*r,p-Vector3.right*r,p+Vector3.forward*r,p-Vector3.forward*r});triangles.AddRange(new[]{n,n+2,n+4,n,n+4,n+3,n,n+3,n+5,n,n+5,n+2,n+1,n+4,n+2,n+1,n+3,n+4,n+1,n+5,n+3,n+1,n+2,n+5});}
                sparkMesh.SetVertices(vertices);sparkMesh.SetTriangles(triangles,0);sparkMesh.RecalculateNormals();Save(sparkMesh,Root+"/ProcessedModels/WisdomSparkles.asset");
                var sparks=new GameObject("Surface sparkles",typeof(MeshFilter),typeof(MeshRenderer));sparks.transform.SetParent(root.transform,false);sparks.GetComponent<MeshFilter>().sharedMesh=Load<Mesh>("ProcessedModels/WisdomSparkles.asset");sparks.GetComponent<Renderer>().sharedMaterial=Load<Material>("Materials/Wisdom_Pods.mat");
                foreach(var p in IslandForestDefinition.Outputs()){var a=new GameObject("Block output");a.transform.SetParent(root.transform,false);a.transform.localPosition=p;}
                var interaction=new GameObject("Interaction anchor");interaction.transform.SetParent(root.transform,false);interaction.transform.localPosition=new Vector3(1.5f,0,0);
                var filaments=JsonUtility.FromJson<FilamentList>(File.ReadAllText(Root+"/ProcessedModels/WisdomFilaments.json"));
                foreach(var chain in filaments.chains){var o=new GameObject("Surface filament",typeof(LineRenderer));o.transform.SetParent(root.transform,false);var line=o.GetComponent<LineRenderer>();line.useWorldSpace=false;line.positionCount=chain.points.Length;line.SetPositions(chain.points.Select(p=>p*scale+visual.transform.localPosition).ToArray());line.widthMultiplier=.009f;line.sharedMaterial=Load<Material>("Materials/WisdomFilament.mat");line.shadowCastingMode=ShadowCastingMode.Off;}
            }
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+name+".prefab");UnityEngine.Object.DestroyImmediate(root);return prefab;
        }
        [Serializable] class AnchorList {public Vector3[] points;}
        [Serializable] class FilamentList {public AnchorList[] chains;}
        static ParticleSystem Motes(Transform parent,string name,Color color,float rate,float lifetime,float radius)
        {
            var o=new GameObject(name,typeof(ParticleSystem));o.transform.SetParent(parent,false);o.transform.localPosition=Vector3.up*2.3f;
            var ps=o.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.startLifetime=lifetime;main.startSpeed=.07f;main.startSize=.06f;main.maxParticles=140;main.startColor=color;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=radius;
            var emission=ps.emission;emission.rateOverTime=rate;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=Load<Material>("Materials/GoldMotes.mat");renderer.shadowCastingMode=ShadowCastingMode.Off;return ps;
        }
        static GameObject AnimalPrefab(string name)
        {
            string modelPath=Root+"/SourceModels/"+name+".fbx";
            var root=new GameObject("Spectral "+name);var visual=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),root.transform);visual.name="Visual";
            var renderers=visual.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float height=name=="Fox"?.7f:name=="Wolf"?1:1.7f;float scale=height/bounds.size.y;visual.transform.localScale=Vector3.one*scale;visual.transform.localPosition=Vector3.up*(-bounds.min.y*scale);
            foreach(var r in renderers){r.sharedMaterials=r.sharedMaterials.Select(_=>Load<Material>("Materials/SpectralAnimal.mat")).ToArray();r.shadowCastingMode=ShadowCastingMode.Off;}
            var clips=AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            var animator=visual.GetComponentInChildren<Animator>();animator.applyRootMotion=false;
            string controllerPath=Root+"/Configuration/"+name+".controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine=controller.layers[0].stateMachine;foreach(var state in machine.states)machine.RemoveState(state.state);
            foreach(var state in new[]{"Idle","Walk","Run"})
            {
                string suffix=state=="Run"?"Gallop":state;
                var clip=clips.FirstOrDefault(c=>c.name.EndsWith("|"+suffix)||c.name==suffix);
                if(clip==null)throw new Exception("Missing "+suffix+" clip on "+name+": "+string.Join(",",clips.Select(c=>c.name)));
                var a=machine.AddState(state);a.motion=clip;if(state=="Idle")machine.defaultState=a;
            }
            animator.runtimeAnimatorController=controller;var animal=root.AddComponent<SpectralAnimal>();animal.animator=animator;
            Motes(root.transform,"Spectral drift",Hex("#C0D4FC"),2,1.8f,.4f).transform.localPosition=Vector3.up*.4f;
            File.AppendAllText("Library/SpectralForest.animation-report.txt",name+": "+string.Join(", ",clips.Select(c=>c.name))+"\n");
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+name+".prefab");UnityEngine.Object.DestroyImmediate(root);return prefab;
        }
        [MenuItem("Tools/Lighting Blocks/Apply Spectral Forest")]
        public static void Apply(){TwilightBuilder.Apply();}
        static void LegacyApply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play before applying forest assets.");
            foreach(var folder in new[]{"Materials","Prefabs","Configuration"})Directory.CreateDirectory(Root+"/"+folder);AssetDatabase.Refresh();
            ImportSettings();
            Material("ForestBark",Hex("#777482"));
            foreach(var name in new[]{"BirchTree_Bark","PineTree_Bark","MapleTree_Bark","NormalTree_Bark","PalmTree_Trunk"})
            {var m=Material(name,Hex("#9B94B0"),name+".png");m.SetTexture("_NormalMap",Load<Texture2D>("Textures/"+name+"_Normal.png"));}
            foreach(var name in new[]{"BirchTree_Leaves","MapleTree_Leaves","PineTree_Leaves","Bush_Leaves","Grass"})
            {var m=Material(name,Hex("#708EAC"),name+".png");m.SetFloat("_Cutoff",.4f);m.EnableKeyword("_ALPHATEST_ON");m.SetFloat("_Cull",0);m.SetFloat("_Wind",.08f);}
            Material("Rock",Hex("#686E83"),"Rocks.png");
            var bark=Material("Wisdom_Bark",Hex("#4B426B"));bark.SetColor("_RimColor",Hex("#9F93D5")*.65f);
            var leaves=Material("Wisdom_Leaves",Hex("#A38AD0"));leaves.SetColor("_EmissionColor",Hex("#738DC2")*.3f);
            bark.SetColor("_EmissionColor",Hex("#4B426B")*.18f);
            var pods=Material("Wisdom_Pods",Hex("#F5C36A"));pods.SetColor("_EmissionColor",new Color(1,.35f,.055f)*2.3f);pods.SetFloat("_EmissionShape",1);
            var filament=Material("WisdomFilament",Hex("#FFE9AE"));filament.SetColor("_EmissionColor",Hex("#FFE9AE")*1.8f);
            var animalMat=Material("SpectralAnimal",Hex("#92A8CF"));animalMat.SetColor("_RimColor",Hex("#BCD8FF")*1.7f);animalMat.SetColor("_EmissionColor",Hex("#788CBA")*.25f);
            var particle=Material("FundamentalParticle",Hex("#D6F5FF"));particle.SetColor("_EmissionColor",new Color(.45f,.85f,1.3f));particle.SetFloat("_EmissionShape",1);
            var crystal=new Mesh{name="Fundamental crystal"};crystal.vertices=new[]{Vector3.up,Vector3.down,Vector3.left*.6f,Vector3.right*.6f,Vector3.forward*.6f,Vector3.back*.6f};crystal.triangles=new[]{0,4,3,0,3,5,0,5,2,0,2,4,1,3,4,1,5,3,1,2,5,1,4,2};crystal.RecalculateNormals();Save(crystal,Root+"/ProcessedModels/FundamentalCrystal.asset");
            var mote=new Material(Shader.Find("LightingBlocks/Spectral Motes"));mote.SetColor("_BaseColor",Color.white*2);Save(mote,Root+"/Materials/GoldMotes.mat");
            var ground=Material("ForestGround",Hex("#A0B1B0"),"forest_leaves_02_Color.png");
            ground.SetTexture("_NormalMap",Load<Texture2D>("Textures/forest_leaves_02_Normal.png"));ground.SetTexture("_MaskMap",Load<Texture2D>("Textures/forest_leaves_02_Mask.png"));ground.SetTexture("_SoilMap",Load<Texture2D>("Textures/forest_ground_04_Color.png"));ground.SetTexture("_SoilNormal",Load<Texture2D>("Textures/forest_ground_04_Normal.png"));ground.SetTexture("_SoilMask",Load<Texture2D>("Textures/forest_ground_04_Mask.png"));ground.SetFloat("_Terrain",1);ground.SetFloat("_Smoothness",1);ground.SetTextureScale("_BaseMap",Vector2.one*(8f/3));
            var cliff=new Material(ground);cliff.name="ForestCliff";cliff.SetFloat("_Terrain",0);cliff.SetFloat("_Triplanar",1);cliff.SetColor("_BaseColor",Hex("#707C8B"));Save(cliff,Root+"/Materials/ForestCliff.mat");
            var config=Load<ForestConfiguration>("Configuration/Forest.asset");
            if(config==null){config=ScriptableObject.CreateInstance<ForestConfiguration>();AssetDatabase.CreateAsset(config,Root+"/Configuration/Forest.asset");}
            config.islands=new[]{new IslandForestDefinition{id="island-1",polygonIndex=0,blockTier=0,authoredWisdom=new Vector3(-12,0,-3),treeCount=70,animalCount=6},new IslandForestDefinition{id="island-2",polygonIndex=1,blockTier=1,authoredWisdom=new Vector3(-12,0,27.5f),treeCount=18,animalCount=3},new IslandForestDefinition{id="island-3",polygonIndex=2,blockTier=2,authoredWisdom=new Vector3(-12,0,59),treeCount=85,animalCount=6}};
            config.trees=new[]{"BirchTree_1","BirchTree_2","MapleTree_1","MapleTree_2","PineTree_1","PineTree_2"}.Select((n,i)=>ModelPrefab(n,i%2==0?4.5f:6)).ToArray();
            config.undergrowth=new[]{ModelPrefab("Bush",.8f),ModelPrefab("Grass_Large",.35f),ModelPrefab("Rock_1",.5f)};
            config.wisdomPrefab=ModelPrefab("WisdomTree",6,true);config.wisdomSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Tree.obj");
            config.animals=new[]{"Deer","Stag","Fox","Wolf"}.Select(AnimalPrefab).ToArray();config.ground=ground;config.cliff=Load<Material>("Materials/ForestCliff.mat");config.particleMaterial=particle;EditorUtility.SetDirty(config);
            config.particleMesh=Load<Mesh>("ProcessedModels/FundamentalCrystal.asset");
            foreach(var m in AssetDatabase.FindAssets("t:Material",new[]{Root}))EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(m)));
            AssetDatabase.SaveAssets();
            var scene=EditorSceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/SampleScene.unity")scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            if(!File.Exists("Library/SampleScene.before-forest.unity"))File.Copy(scene.path,"Library/SampleScene.before-forest.unity");
            var game=UnityEngine.Object.FindAnyObjectByType<GameManager>();var world=game.blocksRoot.parent;
            var forest=game.GetComponent<ForestWorld>();if(forest==null)forest=game.gameObject.AddComponent<ForestWorld>();forest.configuration=config;
            foreach(var t in world.GetComponentsInChildren<Transform>(true).Reverse().ToArray())
            {if(t==null)continue;if(new[]{"ENV_BrokenColumn","ENV_PartialArch","ENV_LowWall","ENV_RubbleCluster","ENV_CliffChunk"}.Any(n=>t.name.StartsWith(n)))UnityEngine.Object.DestroyImmediate(t.gameObject);}
            foreach(var block in game.blocksRoot.GetComponentsInChildren<BridgeBlock>(true))UnityEngine.Object.DestroyImmediate(block.gameObject);
            foreach(var renderer in world.GetComponentsInChildren<Renderer>())
            {if(renderer.name.StartsWith("ShoreFloor"))renderer.sharedMaterial=config.ground;if(renderer.name.StartsWith("ShoreCliff"))renderer.sharedMaterial=config.cliff;}
            foreach(var text in world.GetComponentsInChildren<TextMesh>(true)){text.text=text.text.Replace("THE WANDERING COURT","THE WANDERING GROVE").Replace("THE SILENT REACH","THE SILENT WOOD");}
            var camera=game.cameraRig.GetComponent<Camera>();camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            var volume=UnityEngine.Object.FindAnyObjectByType<Volume>();if(volume==null){var o=new GameObject("Forest atmosphere",typeof(Volume));o.transform.SetParent(world,false);volume=o.GetComponent<Volume>();}volume.isGlobal=true;volume.sharedProfile=profile;
            var pipeline=QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if(pipeline!=null){var data=new SerializedObject(pipeline);var list=data.FindProperty("m_RendererDataList");for(int n=0;n<list.arraySize;n++){var renderer=list.GetArrayElementAtIndex(n).objectReferenceValue as ScriptableRendererData;if(renderer==null)continue;foreach(var feature in renderer.rendererFeatures)if(feature is FullScreenPassRendererFeature pass){pass.requirements|=ScriptableRenderPassInput.Depth;EditorUtility.SetDirty(pass);}EditorUtility.SetDirty(renderer);}}
            forest.EnsureState();forest.Restore(forest.NewState(1907));forest.Build();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
