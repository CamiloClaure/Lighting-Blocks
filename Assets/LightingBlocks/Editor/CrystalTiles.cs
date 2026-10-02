using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LightingBlocks.Editor
{
    // A tile-only rebuild. Does not regenerate a forest or open/save the user's scene
    // (AddMissingTiers is the exception: it adds new tiers to Lighting.unity's blockPrefabs).
    [InitializeOnLoad]
    public static class CrystalTiles
    {
        const string Art="Assets/LightingBlocks/Art/SpectralForest/Twilight";
        const string Prefabs="Assets/LightingBlocks/Art/Prefabs/Bridge Stone ";
        // blue, violet, amber, green, red: BridgeBlock.TierColour order.
        static readonly string[] Colours={"#246DFF","#B03DFF","#FFAE38","#33E07A","#FF4A3D"};
        static CrystalTiles(){EditorApplication.update+=Poll;}
        // Library/CrystalTiles.request runs AddMissingTiers from an open editor; the result lands in Library/CrystalTiles.txt.
        static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||!File.Exists("Library/CrystalTiles.request"))return;
            AssetDatabase.Refresh();if(EditorApplication.isCompiling)return;
            File.Delete("Library/CrystalTiles.request");
            string result;try{result="SUCCESS: "+AddMissingTiers();}catch(Exception e){result="FAIL: "+e;}
            File.WriteAllText("Library/CrystalTiles.txt",result);
        }
        // Builds every Bridge Stone N.prefab that Colours names but the project lacks (copied from tier 0,
        // then rebuilt in its own colour), and points Lighting.unity's GameManager at all of them.
        // Existing tiers are left as they are.
        public static string AddMissingTiers()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode before adding tiers.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard the open scene first: "+UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path);
            var built=new List<int>();
            for(int tier=0;tier<Colours.Length;tier++)
            {
                if(AssetDatabase.LoadAssetAtPath<BridgeBlock>(Prefabs+tier+".prefab")!=null) continue;
                if(!AssetDatabase.CopyAsset(Prefabs+"0.prefab",Prefabs+tier+".prefab")) throw new Exception("Could not copy Bridge Stone 0 to tier "+tier);
                Build(AssetDatabase.LoadAssetAtPath<BridgeBlock>(Prefabs+tier+".prefab"),tier); built.Add(tier);
            }
            AssetDatabase.SaveAssets();
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Lighting.unity");
            var game=UnityEngine.Object.FindAnyObjectByType<GameManager>();
            if(game==null) throw new Exception("No GameManager in Lighting.unity");
            var prefabs=new BridgeBlock[Colours.Length];
            for(int tier=0;tier<prefabs.Length;tier++) prefabs[tier]=AssetDatabase.LoadAssetAtPath<BridgeBlock>(Prefabs+tier+".prefab");
            game.blockPrefabs=prefabs; EditorUtility.SetDirty(game); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "built tiers ["+string.Join(",",built)+"], Lighting.unity blockPrefabs = "+prefabs.Length;
        }
        static Color Tint(int tier) { ColorUtility.TryParseHtmlString(Colours[tier],out var c); return c; }
        static T Save<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null) { AssetDatabase.CreateAsset(value,path); return value; }
            if(value is Mesh mesh && old is Mesh destination)
            {
                // CopySerialized leaves an already-uploaded Mesh's GPU buffer stale in the editor.
                destination.Clear(); destination.vertices=mesh.vertices; destination.uv=mesh.uv;
                destination.colors=mesh.colors; destination.normals=mesh.normals; destination.triangles=mesh.triangles;
                destination.RecalculateBounds(); destination.UploadMeshData(false);
            }
            else EditorUtility.CopySerialized(value,old);
            UnityEngine.Object.DestroyImmediate(value);
            EditorUtility.SetDirty(old); return old;
        }
        static Material Material(string name,string shader,Color tint)
        {
            var path=Art+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material,path); }
            material.shader=Shader.Find(shader);
            if(material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",tint);
            if(material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor",tint*5f);
            if(material.HasProperty("_Opacity")) material.SetFloat("_Opacity",.66f);
            material.SetFloat("_Glow",1); material.enableInstancing=true;
            EditorUtility.SetDirty(material); return material;
        }
        [MenuItem("Tools/Lighting Blocks/Apply Reference Crystal Tiles")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode before rebuilding tiles.");
            for(int tier=0;tier<Colours.Length;tier++)
                Build(AssetDatabase.LoadAssetAtPath<BridgeBlock>(Prefabs+tier+".prefab"),tier);
            AssetDatabase.SaveAssets();
        }
        public static void Build(BridgeBlock prefab,int tier)
        {
            string path=AssetDatabase.GetAssetPath(prefab);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                for(int i=root.transform.childCount-1;i>=0;i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var body=Material("Crystal"+tier,"LightingBlocks/Twilight Crystal",Tint(tier));
                body.SetFloat("_FacetSeed",tier*7.13f);
                var edge=Material("CrystalEdge"+tier,"LightingBlocks/Crystal Filaments",Tint(tier));
                var art=root.GetComponent<BlockArt>()??root.AddComponent<BlockArt>();
                var bodies=new List<Renderer>(); var details=new List<Renderer>();
                for(int state=0;state<2;state++)
                {
                    bool placed=state==1;
                    var visual=new GameObject(placed?"Placed crystal":"Loose crystal");
                    visual.transform.SetParent(root.transform,false);
                    float width=placed?1.96f:1.4f,height=placed?.36f:.26f;
                    // Keep the loose top at its original height without changing the haul collider.
                    float lift=placed?0:.22f;
                    bodies.Add(Renderer(visual.transform,"Crystal body",Slab("CrystalBody"+state,width,height,lift),body));
                    details.Add(Renderer(visual.transform,"Crystal runes",Filaments("CrystalLines"+state+"-"+tier,width,height,lift,placed,tier),edge));
                    if(placed) art.placedVisual=visual.transform; else art.looseVisual=visual.transform;
                }
                art.bodies=bodies.ToArray(); art.lightDetails=details.ToArray(); art.seams=Array.Empty<Renderer>();
                root.GetComponent<Renderer>().enabled=false;
                root.GetComponent<BridgeBlock>().tier=tier; // TrySpawnAtWisdomTree and save loads take the prefab's own tier
                art.Refresh(); PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static Renderer Renderer(Transform parent,string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(parent,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.GetComponent<MeshRenderer>(); r.sharedMaterial=material;
            r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false; return r;
        }
        static Mesh Slab(string name,float width,float height,float lift)
        {
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var t=new List<int>();
            float half=width*.5f,bevel=.012f;
            Vector3[] Ring(float y,float inset)=>new[]{new Vector3(-half+inset,y+lift,-half+inset),new Vector3(half-inset,y+lift,-half+inset),new Vector3(half-inset,y+lift,half-inset),new Vector3(-half+inset,y+lift,half-inset)};
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int n=v.Count; v.AddRange(new[]{a,b,c,d}); uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            var r0=Ring(-height/2,bevel); var r1=Ring(-height/2+bevel,0);
            var r2=Ring(height/2-bevel,0); var r3=Ring(height/2,bevel);
            foreach(var pair in new[]{new[]{r0,r1},new[]{r1,r2},new[]{r2,r3}})
                for(int i=0;i<4;i++) { int j=(i+1)%4; Quad(pair[0][i],pair[1][i],pair[1][j],pair[0][j]); }
            Quad(r3[0],r3[3],r3[2],r3[1]); Quad(r0[0],r0[1],r0[2],r0[3]);
            var m=new Mesh{name=name}; m.SetVertices(v); m.SetUVs(0,uv); m.SetTriangles(t,0); m.RecalculateNormals(); m.RecalculateBounds();
            return Save(m,Art+"/Meshes/"+name+".asset");
        }
        static Mesh Filaments(string name,float width,float height,float lift,bool hanging,int tier)
        {
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var colors=new List<Color>(); var triangles=new List<int>();
            void Strip(Vector3 a,Vector3 b,Vector3 side,float widthA,float widthB,float alphaA,float alphaB,float white=0)
            {
                int k=v.Count; v.AddRange(new[]{a-side*widthA,a+side*widthA,b+side*widthB,b-side*widthB});
                uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                colors.AddRange(new[]{new Color(white,0,0,alphaA),new Color(white,0,0,alphaA),new Color(white,0,0,alphaB),new Color(white,0,0,alphaB)});
                triangles.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
            }
            void Line(Vector3 a,Vector3 b,float thickness,float alpha=1,float white=0)
            {
                var side=Vector3.Cross((b-a).normalized,Vector3.up);
                if(side.sqrMagnitude<.1f) side=Vector3.right; else side.Normalize();
                Strip(a,b,side,thickness,thickness,alpha,alpha,white);
            }
            void Star(Vector3 p,float size,float strength)
            {
                foreach(var axis in new[]{Vector3.right,Vector3.forward,Vector3.up})
                {
                    var side=axis==Vector3.up?Vector3.right:Vector3.up;
                    Strip(p-axis*size,p,side,0,.019f,0,strength,.5f);
                    Strip(p,p+axis*size,side,.019f,0,strength,0,.5f);
                }
            }
            float h=width*.5f-.009f,top=height*.5f+lift+.002f,bottom=-height*.5f+lift;
            var corners=new[]{new Vector3(-h,top,-h),new Vector3(h,top,-h),new Vector3(h,top,h),new Vector3(-h,top,h)};
            for(int i=0;i<4;i++)
            {
                var a=corners[i]; var b=corners[(i+1)%4];
                Line(a,b,.025f,.65f,.2f); Line(a,b,.18f,.18f);
                var down=new Vector3(a.x,bottom,a.z); var downB=new Vector3(b.x,bottom,b.z);
                Line(a,down,.017f,.3f); Line(down,downB,.015f,.11f);
                Star(a,.12f,i==1?.8f:.4f);
                if(!hanging) continue;
                float length=.85f+((i*3+tier*2)%5)*.42f;
                var end=down-Vector3.up*length;
                // Crossed, tapered ribbons remain visible from every camera direction.
                foreach(var side in new[]{Vector3.right,Vector3.forward})
                {
                    Strip(down,end,side,.012f,.002f,.14f,0);
                    Strip(down,end,side,.045f,.012f,.035f,0);
                }
                Star(Vector3.Lerp(down,end,.34f),.038f,.22f);
                Star(Vector3.Lerp(down,end,.83f),.05f,.32f);
            }
            // Tiny floating lights, sparse enough for the void to stay dark.
            if(hanging) for(int i=0;i<4;i++)
            {
                float x=Mathf.Sin(i*17.1f+tier)*width*.6f,z=Mathf.Cos(i*11.7f+tier)*width*.6f;
                Star(new Vector3(x,bottom-.4f-i*.45f,z),.022f,.12f);
            }
            var m=new Mesh{name=name}; m.SetVertices(v); m.SetUVs(0,uv); m.SetColors(colors); m.SetTriangles(triangles,0);
            m.RecalculateNormals(); m.RecalculateBounds(); return Save(m,Art+"/Meshes/"+name+".asset");
        }

        // Render the real prefabs in a temporary scene; never edits the user's open scene or save.
        public static void Capture()
        {
            var scene=EditorSceneManager.NewPreviewScene();
            bool asyncShaders=ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation=false;
            RenderTexture target=null; Texture2D image=null; VolumeProfile profile=null;
            try
            {
                GameObject Host(string name)
                {
                    var go=new GameObject(name); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene); go.layer=31; return go;
                }
                var camera=Host("Tile inspection camera").AddComponent<Camera>(); camera.scene=scene;
                camera.transform.position=new Vector3(8,6,-12); camera.transform.LookAt(new Vector3(0,-.45f,0));
                camera.orthographic=true; camera.orthographicSize=4.2f; camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.001f,.002f,.006f); camera.cullingMask=1<<31;
                camera.allowHDR=true; camera.nearClipPlane=.1f; camera.farClipPlane=80;
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true; data.requiresColorTexture=true;
                data.volumeLayerMask=1<<31; data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var volume=Host("Tile preview grading").AddComponent<Volume>(); volume.isGlobal=true;
                var source=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
                profile=ScriptableObject.CreateInstance<VolumeProfile>();
                foreach(var component in source.components) profile.components.Add(UnityEngine.Object.Instantiate(component));
                volume.sharedProfile=profile;
                if(profile.TryGet<DepthOfField>(out var depth)) depth.active=false;
                for(int tier=0;tier<3;tier++) for(int row=0;row<2;row++)
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<BridgeBlock>("Assets/LightingBlocks/Art/Prefabs/Bridge Stone "+tier+".prefab");
                    var block=UnityEngine.Object.Instantiate(prefab); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(block.gameObject,scene);
                    block.transform.position=new Vector3((tier-1)*3.1f,0,(row-.5f)*3.0f);
                    block.transform.localScale=new Vector3(2,.36f,2); block.placed=true; block.Connected=true;
                    // Compare a stepped-on tile with a fully settled, unstepped tile.
                    block.GetComponent<BlockArt>().Refresh(); block.GetComponent<BlockArt>().SetGlow(row==0?1:0);
                    foreach(var tr in block.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer=31;
                }
                target=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGBHalf); target.Create(); camera.targetTexture=target;
                camera.Render(); var previous=RenderTexture.active;
                try { RenderTexture.active=target; image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); }
                finally { RenderTexture.active=previous; }
                Directory.CreateDirectory("Library/CrystalTiles"); File.WriteAllBytes("Library/CrystalTiles/reference-preview.png",image.EncodeToPNG());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if(target!=null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                if(profile!=null)
                {
                    foreach(var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
                    UnityEngine.Object.DestroyImmediate(profile);
                }
                ShaderUtil.allowAsyncCompilation=asyncShaders;
            }
        }
    }
}
