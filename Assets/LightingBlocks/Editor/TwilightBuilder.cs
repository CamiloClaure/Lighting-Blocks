using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    public static class TwilightBuilder
    {
        const string Root="Assets/LightingBlocks/Art/SpectralForest",Art=Root+"/Twilight";
        static T Load<T>(string p)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(p);
        static Color Hex(string h){ColorUtility.TryParseHtmlString(h,out var c);return c;}
        static T Save<T>(T value,string path)where T:UnityEngine.Object{var old=Load<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static Material Material(string name,string shader,Color color,bool defaults)
        {string path=Art+"/Materials/"+name+".mat";var m=Load<Material>(path);if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}if(defaults){m.shader=Shader.Find(shader);m.SetColor("_BaseColor",color);}m.enableInstancing=true;EditorUtility.SetDirty(m);return m;}
        static Mesh ModelMesh(string name)
        {
            var source=Load<GameObject>(Art+"/Models/"+name+".fbx");var list=new List<CombineInstance>();foreach(var f in source.GetComponentsInChildren<MeshFilter>())for(int i=0;i<f.sharedMesh.subMeshCount;i++)list.Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=i,transform=f.transform.localToWorldMatrix});
            var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.CombineMeshes(list.ToArray(),true,true);var colors=m.colors;for(int i=0;i<colors.Length;i++)colors[i]=colors[i].linear;m.colors=colors;m.RecalculateBounds();m.RecalculateTangents();return Save(m,Art+"/Meshes/"+name+".asset");
        }
        static GameObject Creature(string name,Material material)
        {
            string path=Art+"/Models/"+name+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=true;importer.importCameras=false;importer.importLights=false;
            var settings=importer.defaultClipAnimations;foreach(var clip in settings){clip.loopTime=!clip.name.EndsWith("Takeoff")&&!clip.name.EndsWith("Landing");clip.lockRootRotation=true;clip.lockRootHeightY=true;clip.lockRootPositionXZ=true;}importer.clipAnimations=settings;importer.SaveAndReimport();
            var root=new GameObject("Spectral "+name);var visual=UnityEngine.Object.Instantiate(Load<GameObject>(path),root.transform);visual.name="Visual";
            foreach(var r in visual.GetComponentsInChildren<Renderer>()){r.sharedMaterials=r.sharedMaterials.Select(_=>material).ToArray();r.shadowCastingMode=ShadowCastingMode.Off;}
            var animator=visual.GetComponentInChildren<Animator>();if(animator==null)animator=visual.AddComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
            string controllerPath=Art+"/"+name+".controller";var controller=Load<AnimatorController>(controllerPath)??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);var machine=controller.layers[0].stateMachine;
            foreach(var state in machine.states)machine.RemoveState(state.state);
            var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            var names=name=="Rabbit"?new[]{"Idle","Sniff","Alert","Hop","Flee"}:new[]{"GroundIdle","Peck","Hop","Alert","Takeoff","Flight","Landing"};
            foreach(var stateName in names){var clip=clips.FirstOrDefault(c=>c.name==stateName||c.name.EndsWith("|"+stateName));if(clip==null)throw new Exception(name+" missing "+stateName+": "+string.Join(",",clips.Select(c=>c.name)));var state=machine.AddState(stateName);state.motion=clip;if(stateName==names[0])machine.defaultState=state;}
            animator.runtimeAnimatorController=controller;root.AddComponent<ForestCreature>().animator=animator;
            File.AppendAllText("Library/Twilight-animations.txt",name+": "+string.Join(", ",clips.Select(c=>c.name))+"\n");
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Art+"/Prefabs/"+name+".prefab");UnityEngine.Object.DestroyImmediate(root);return prefab;
        }
        public static void HabitatTexture(ForestConfiguration config)
        {
            const int w=512,h=1024;var tex=new Texture2D(w,h,TextureFormat.RGBA32,false,true);var colors=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                var p=new Vector3(x/(float)(w-1)*128-64,0,y/(float)(h-1)*256-64);IslandForestDefinition island=null;
                foreach(var d in config.islands)if(Landscape.Inside(new Vector2(p.x,p.z),Landscape.Shores[d.polygonIndex])){island=d;break;}
                float green=island==null?.65f:ForestHabitat.Green(p,island);if(island!=null&&ForestLayoutGenerator.Reserved(p,island,2.5f))green*=.55f;
                float moss=green*(.55f+Mathf.PerlinNoise(p.x*.15f+3,p.z*.15f)*.2f),grass=green-moss,soil=(1-green)*.65f;colors[y*w+x]=new Color(moss,grass,soil,1-green-soil);
            }
            tex.SetPixels32(colors);tex.Apply();File.WriteAllBytes(Art+"/Textures/Habitat.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(Art+"/Textures/Habitat.png");
            var imp=(TextureImporter)AssetImporter.GetAtPath(Art+"/Textures/Habitat.png");imp.sRGBTexture=false;imp.wrapMode=TextureWrapMode.Clamp;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.maxTextureSize=1024;imp.SaveAndReimport();
        }
        [MenuItem("Tools/Lighting Blocks/Apply Luminous Forest")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play before rebuilding.");
            foreach(var folder in new[]{"Materials","Meshes","Prefabs","Textures"})Directory.CreateDirectory(Art+"/"+folder);Directory.CreateDirectory("Assets/Resources/Forest");AssetDatabase.Refresh();
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");var game=UnityEngine.Object.FindAnyObjectByType<GameManager>();var forest=game.GetComponent<ForestWorld>();var c=forest.configuration;bool defaults=c.artVersion<2;
            if(defaults){c.islands[0].treeCount=120;c.islands[1].treeCount=28;c.islands[2].treeCount=130;for(int i=0;i<3;i++){c.islands[i].rabbitCount=i==1?2:4;c.islands[i].birdCount=i==1?3:6;}c.islands[0].pathPoints=new[]{new Vector3(0,0,-9),new Vector3(-6,0,-9)};c.haze=Hex("#151E37");c.fogStart=48;c.fogEnd=150;c.ambientBudget=600;}
            foreach(var p in Directory.GetFiles(Art+"/Textures","*.png")){var imp=(TextureImporter)AssetImporter.GetAtPath(p.Replace('\\','/'));if(imp==null)continue;imp.textureType=p.Contains("Normal")?TextureImporterType.NormalMap:TextureImporterType.Default;imp.sRGBTexture=!p.Contains("Normal")&&!p.Contains("Habitat");imp.wrapMode=TextureWrapMode.Repeat;imp.mipmapEnabled=true;imp.SaveAndReimport();}
            HabitatTexture(c);
            var ground=Material("LushGround","LightingBlocks/Spectral Lit",Hex("#B7C8BC"),defaults);
            if(defaults){ground.SetFloat("_Terrain",1);ground.SetFloat("_NormalStrength",.35f);ground.SetFloat("_Smoothness",.22f);ground.SetTexture("_BaseMap",Load<Texture2D>(Root+"/Textures/forest_leaves_02_Color.png"));ground.SetTexture("_NormalMap",Load<Texture2D>(Root+"/Textures/forest_leaves_02_Normal.png"));ground.SetTexture("_MaskMap",Load<Texture2D>(Root+"/Textures/forest_leaves_02_Mask.png"));ground.SetTexture("_SoilMap",Load<Texture2D>(Root+"/Textures/forest_ground_04_Color.png"));ground.SetTexture("_SoilNormal",Load<Texture2D>(Root+"/Textures/forest_ground_04_Normal.png"));ground.SetTexture("_SoilMask",Load<Texture2D>(Root+"/Textures/forest_ground_04_Mask.png"));ground.SetTexture("_MossMap",Load<Texture2D>(Art+"/Textures/Moss.png"));ground.SetTexture("_GrassMap",Load<Texture2D>(Art+"/Textures/GrassGround.png"));}
            ground.SetFloat("_SoftFill",.14f);ground.SetTexture("_MossNormal",Load<Texture2D>(Art+"/Textures/MossNormal.png"));ground.SetTexture("_HabitatMap",Load<Texture2D>(Art+"/Textures/Habitat.png"));c.ground=ground;
            var cover=Material("LivingPlants","LightingBlocks/Spectral Lit",Color.white,defaults);cover.SetFloat("_VertexTint",1);cover.SetFloat("_Cull",0);cover.SetFloat("_Wind",.14f);cover.SetFloat("_Smoothness",.06f);cover.SetFloat("_SoftFill",.24f);
            var glow=Material("LuminousPlants","LightingBlocks/Spectral Lit",Color.white,defaults);glow.SetFloat("_VertexTint",1);glow.SetFloat("_Cull",0);glow.SetFloat("_Wind",.035f);if(defaults)glow.SetColor("_EmissionColor",new Color(.055f,.035f,.10f));
            c.coverMeshes=new[]{"GrassShort","GrassMedium","GrassTall","Fern1","Fern2","Mushroom1","Mushroom2","Flowers","Twigs"}.Select(ModelMesh).ToArray();c.coverMaterials=new[]{cover,cover,cover,cover,cover,glow,glow,glow,cover};
            var spectral=Material("LivingSpectral","LightingBlocks/Spectral Lit",Hex("#7896C4"),defaults);if(defaults){spectral.SetColor("_RimColor",new Color(.4f,.6f,.95f)*.8f);spectral.SetColor("_EmissionColor",new Color(.055f,.075f,.15f));spectral.SetFloat("_VertexTint",.4f);}
            c.species=new[]{new ForestSpecies{id="Rabbit",prefab=Creature("Rabbit",spectral)},new ForestSpecies{id="Bird",prefab=Creature("Bird",spectral),flying=true,walkSpeed=.45f,idle="GroundIdle",forage="Peck",move="Hop",flee="Flight"}};
            var mote=Material("Motes","LightingBlocks/Twilight Mote",Color.white,defaults);Save(new Material(mote),"Assets/Resources/Forest/Mote.mat");
            var wisdomPath=AssetDatabase.GetAssetPath(c.wisdomPrefab);var wisdom=PrefabUtility.LoadPrefabContents(wisdomPath);
            if(wisdom.transform.Find("Living lavender canopy")==null){var canopy=new GameObject("Living lavender canopy",typeof(MeshFilter),typeof(MeshRenderer));canopy.transform.SetParent(wisdom.transform,false);canopy.GetComponent<MeshFilter>().sharedMesh=ModelMesh("WisdomCanopy");canopy.GetComponent<Renderer>().sharedMaterial=glow;canopy.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;}
            PrefabUtility.SaveAsPrefabAsset(wisdom,wisdomPath);PrefabUtility.UnloadPrefabContents(wisdom);
            for(int i=0;i<3;i++)CrystalPrefab(game.blockPrefabs[i],i,defaults);
            if(defaults){foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())if(light.type==LightType.Directional){light.intensity=.42f;light.color=Hex("#9FBDEB");light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(48,-35,0);}RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=Hex("#7188B4")*.5f;RenderSettings.ambientEquatorColor=Hex("#456B71")*.45f;RenderSettings.ambientGroundColor=Hex("#30444F")*.4f;RenderSettings.fogColor=c.haze;RenderSettings.fogStartDistance=c.fogStart;RenderSettings.fogEndDistance=c.fogEnd;
                var profile=Load<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");if(profile.TryGet<Bloom>(out var bloom)){bloom.intensity.Override(.32f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.55f);}EditorUtility.SetDirty(profile);}
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>())if(renderer.name.StartsWith("ShoreFloor"))renderer.sharedMaterial=ground;
            game.cameraRig.GetComponent<Camera>().backgroundColor=c.haze;
            var reveal=Load<Material>("Assets/LightingBlocks/Shaders/M_PlayerFog.mat");if(reveal!=null){reveal.shader=Shader.Find("LightingBlocks/Twilight Reveal");if(defaults){reveal.SetFloat("_FogRadius",9);reveal.SetFloat("_FogFade",12);reveal.SetFloat("_FogStrength",.35f);reveal.SetColor("_FogColor",c.haze);}EditorUtility.SetDirty(reveal);}
            c.artVersion=2;EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();forest.Restore(forest.NewState(1907));forest.Build();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
            File.WriteAllText("Library/Twilight-apply.txt","Luminous forest applied; settings retained on subsequent rebuilds.");
        }
        static Mesh Slab(string name,float width,float height)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();float half=width/2,bevel=.035f;
            Vector3[] ring(float y,float inset)=>new[]{new Vector3(-half+inset,y,-half+inset),new Vector3(half-inset,y,-half+inset),new Vector3(half-inset,y,half-inset),new Vector3(-half+inset,y,half-inset)};
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int n=v.Count;v.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
            var r0=ring(-height/2,bevel);var r1=ring(-height/2+bevel,0);var r2=ring(height/2-bevel,0);var r3=ring(height/2,bevel);
            foreach(var pair in new[]{new[]{r0,r1},new[]{r1,r2},new[]{r2,r3}})for(int i=0;i<4;i++){int j=(i+1)%4;Quad(pair[0][i],pair[1][i],pair[1][j],pair[0][j]);}
            Quad(r3[0],r3[3],r3[2],r3[1]);Quad(r0[0],r0[1],r0[2],r0[3]);var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();return Save(mesh,Art+"/Meshes/"+name+".asset");
        }
        static Mesh Lines(string name,float width,float height,bool hanging,int tier)
        {
            var v=new List<Vector3>();var t=new List<int>();
            void Line(Vector3 a,Vector3 b,float thick){var n=Vector3.Cross((b-a).normalized,Vector3.up);if(n.sqrMagnitude<.1f)n=Vector3.right;n*=thick;int k=v.Count;v.AddRange(new[]{a-n,a+n,b+n,b-n});t.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});}
            float h=width*.5f-.025f,y=height*.5f+.003f;var corners=new[]{new Vector3(-h,y,-h),new Vector3(h,y,-h),new Vector3(h,y,h),new Vector3(-h,y,h)};
            for(int i=0;i<4;i++){Line(corners[i],corners[(i+1)%4],.009f);Line(corners[i],new Vector3(corners[i].x,-y,corners[i].z),.007f);}
            float r=width*.22f;int sides=tier==0?4:tier==1?6:8;
            for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;Line(new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r),new Vector3(Mathf.Cos(b)*r,y,Mathf.Sin(b)*r),.005f);}
            Line(new Vector3(-r,y,0),new Vector3(r,y,0),.005f);Line(new Vector3(0,y,-r),new Vector3(0,y,r),.005f);
            if(hanging)for(int i=0;i<3;i++){var a=corners[i];a.y=-height*.5f;var b=a-Vector3.up*(.5f+i*.19f);Line(a,b,.004f);Line(b-Vector3.right*.022f,b+Vector3.right*.022f,.018f);}
            var m=new Mesh{name=name};m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();return Save(m,Art+"/Meshes/"+name+".asset");
        }
        static void CrystalPrefab(BridgeBlock prefab,int tier,bool defaults)
        {
            string path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);foreach(Transform child in root.transform.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            var tint=tier==0?Hex("#4AC7F5"):tier==1?Hex("#BE78E7"):Hex("#FFC66F");
            var body=Material("Crystal"+tier,"LightingBlocks/Twilight Crystal",tint,defaults);
            // The crystal shader's face-on output is about 0.39 x tint, and Bloom's threshold is 1.1, so an LDR tint can
            // never glow. _BaseColor is [HDR]: author it above 1. Set unconditionally, because defaults is false once
            // artVersion reaches 2 and anything inside that guard is silently skipped on a rebuilt project.
            body.SetColor("_BaseColor",new Color(tint.r*2.2f,tint.g*2.2f,tint.b*2.2f,1));
            var edge=Material("CrystalEdge"+tier,"LightingBlocks/Spectral Lit",Color.white,defaults);if(defaults){edge.SetColor("_EmissionColor",(tier==0?new Color(.25f,.75f,1):tier==1?new Color(.8f,.3f,1):new Color(1,.65f,.22f))*2);edge.SetFloat("_Cull",0);}
            var art=root.GetComponent<BlockArt>()??root.AddComponent<BlockArt>();var bodies=new List<Renderer>();
            for(int state=0;state<2;state++){bool placed=state==1;var visual=new GameObject(placed?"Placed crystal":"Loose crystal");visual.transform.SetParent(root.transform,false);float width=placed?1.96f:1.4f,height=placed?.36f:.7f;
                var stone=new GameObject("Crystal body",typeof(MeshFilter),typeof(MeshRenderer));stone.transform.SetParent(visual.transform,false);stone.GetComponent<MeshFilter>().sharedMesh=Slab("CrystalBody"+state,width,height);stone.GetComponent<Renderer>().sharedMaterial=body;stone.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;bodies.Add(stone.GetComponent<Renderer>());
                var lines=new GameObject("Crystal runes",typeof(MeshFilter),typeof(MeshRenderer));lines.transform.SetParent(visual.transform,false);lines.GetComponent<MeshFilter>().sharedMesh=Lines("CrystalLines"+state+"-"+tier,width,height,placed,tier);lines.GetComponent<Renderer>().sharedMaterial=edge;lines.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                if(placed)art.placedVisual=visual.transform;else art.looseVisual=visual.transform;
            }
            art.bodies=bodies.ToArray();art.seams=Array.Empty<Renderer>();root.GetComponent<Renderer>().enabled=false;art.Refresh();PrefabUtility.SaveAsPrefabAsset(root,path);PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
