using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightingBlocks.Editor
{
    public static class SoulBridgeArtIntegration
    {
        const string Pack="Assets/LastSparkArtPack/";
        const string Art="Assets/LastSpark/SoulBridge/IntegratedArt";
        static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Pack+"Materials/M_"+name+".mat");
        static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/"+name+".prefab");
        static GameObject Instance(string name,Transform parent,Vector3 position,float scale=1,float yaw=0)
        {
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(Prefab(name));
            obj.transform.SetParent(parent,false);obj.transform.position=position;obj.transform.rotation=Quaternion.Euler(0,yaw,0);obj.transform.localScale=Vector3.one*scale;
            return obj;
        }
        static void MeshAsset(Mesh mesh,string name)
        {
            string path=Art+"/"+name+".asset";var prior=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(prior==null)AssetDatabase.CreateAsset(mesh,path);else {EditorUtility.CopySerialized(mesh,prior);UnityEngine.Object.DestroyImmediate(mesh);}
        }
        static GameObject MeshObject(string name,Mesh mesh,Material material,Transform parent,bool collider=false)
        {
            MeshAsset(mesh,name);mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/"+name+".asset");
            var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(parent,false);
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<Renderer>().sharedMaterial=material;
            if(collider)obj.AddComponent<MeshCollider>().sharedMesh=mesh;
            return obj;
        }
        [MenuItem("Tools/Last Spark/Apply Art and Expanded Shores")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play before applying art.");
            var scene=EditorSceneManager.GetActiveScene();
            if(scene.path!="Assets/Scenes/SampleScene.unity")scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            if(Prefab("BLK_Wandering_LooseVisual")==null)throw new Exception("Import the supplied art pack first.");
            Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            string backup="Assets/Scenes/SampleScene_BeforeArtOrbit.unity";
            if(!File.Exists(backup))EditorSceneManager.SaveScene(scene,backup,true);
            var game=UnityEngine.Object.FindAnyObjectByType<SoulBridgeGame>();
            if(game==null)throw new Exception("Soul Bridge game missing.");
            var player=game.player;
            foreach(var stone in game.blockPrefabs)
            {
                string path=AssetDatabase.GetAssetPath(stone);var root=PrefabUtility.LoadPrefabContents(path);
                AttachStone(root,root.GetComponent<SoulBridgeBlock>().tier);
                PrefabUtility.SaveAsPrefabAsset(root,path);PrefabUtility.UnloadPrefabContents(root);
            }
            foreach(var b in game.blocksRoot.GetComponentsInChildren<SoulBridgeBlock>(true))
            {
                if(b.GetComponent<SoulBridgeArt>()==null)AttachStone(b.gameObject,b.tier);
                b.transform.localScale=b.placed?new Vector3(2,.36f,2):new Vector3(.84f,.5f,.84f);
                if(!b.placed){var p=b.transform.position;p.y=.26f;b.transform.position=p;}
                b.GetComponent<SoulBridgeArt>().Refresh();
            }
            var world=GameObject.Find("Soul Bridge World").transform;
            foreach(var name in new[]{"Initial Shore","The Silent Reach","Next Dawn","Immersive Shores"})
            {var old=world.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
            var terrain=new GameObject("Immersive Shores").transform;terrain.SetParent(world,false);
            for(int i=0;i<3;i++)BuildShore(terrain,i);
            Dress(terrain);
            foreach(var text in world.GetComponentsInChildren<TextMesh>())text.gameObject.SetActive(false);
            foreach(var r in world.GetComponentsInChildren<Renderer>())if(r.name.StartsWith("Shore start marker"))r.enabled=false;
            for(int i=0;i<2;i++)Instance("PROP_PathRoot",terrain,new Vector3(0,0,6.6f+i*27));
            ReplaceProp(game.soulWell,"PROP_SoulWell_Active",out var wellCore);game.wellCore=wellCore;wellCore.gameObject.SetActive(false);
            ReplaceProp(game.exit,"PROP_NextDawn_Active",out var exitCore);game.nextAreaGlow=exitCore.gameObject;game.nextAreaGlow.SetActive(false);
            Instance("ENV_PartialArch",terrain,new Vector3(0,0,60),1.25f);
            AddLabel("SOUL WELL  [5]",new Vector3(-6,.035f,-7.5f),terrain);
            AddLabel("THE NEXT DAWN  [E]",new Vector3(0,.035f,54),terrain);
            AddLabel("THE WANDERING COURT",new Vector3(0,.035f,-3),terrain);
            AddLabel("THE SILENT REACH",new Vector3(0,.035f,23),terrain);
            AddLabel("BUILD AT THE LIT EDGE",new Vector3(0,.035f,5.8f),terrain);
            var soul=game.soulLight.transform;soul.GetComponent<Renderer>().sharedMaterial=Mat("SoulCore");
            game.soulLight.color=new Color(1,.73f,.34f);game.soulLight.range=9;game.soulLight.intensity=3.5f;
            var atmos=player.GetComponent<SoulBridgeAtmosphere>();if(atmos==null)atmos=player.gameObject.AddComponent<SoulBridgeAtmosphere>();atmos.soul=soul;
            var halo=soul.Find("Soul halo");if(halo!=null)UnityEngine.Object.DestroyImmediate(halo.gameObject);
            var haloObject=GameObject.CreatePrimitive(PrimitiveType.Quad);haloObject.name="Soul halo";UnityEngine.Object.DestroyImmediate(haloObject.GetComponent<Collider>());haloObject.transform.SetParent(soul,false);haloObject.transform.localScale=Vector3.one*4;
            var haloMat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));haloMat.name="Soul halo";haloMat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Pack+"Textures/T_FX_SoulHalo.png"));haloMat.SetColor("_BaseColor",new Color(1,.74f,.35f,.35f));
            haloMat.SetFloat("_Surface",1);haloMat.SetFloat("_Blend",2);haloMat.SetFloat("_SrcBlend",5);haloMat.SetFloat("_DstBlend",1);haloMat.SetFloat("_ZWrite",0);haloMat.SetFloat("_Cull",0);haloMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");haloMat.renderQueue=3000;
            SaveMaterial(haloMat,Art+"/SoulHalo.mat");haloObject.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Art+"/SoulHalo.mat");atmos.halo=haloObject.GetComponent<Renderer>();
            DressTraveler(player);
            foreach(var sun in UnityEngine.Object.FindObjectsByType<Light>())if(sun.type==LightType.Directional){sun.intensity=.085f;sun.color=new Color(.55f,.65f,.85f);sun.transform.rotation=Quaternion.Euler(42,-30,0);}
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.13f,.16f,.22f);RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.035f,.045f,.065f);RenderSettings.fogStartDistance=45;RenderSettings.fogEndDistance=145;
            var camera=game.cameraRig.GetComponent<Camera>();camera.backgroundColor=new Color(.035f,.045f,.065f);game.cameraRig.minimumFocus=new Vector2(-24,-27);game.cameraRig.bounds=new Vector2(24,80);game.cameraRig.distance=27;
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Library/ArtOrbit.setup-result.txt","SUCCESS: imported materials, smaller stones, 3 enlarged polygon shores, ruin dressing, warm light and orbit-ready scene saved.");
        }
        static void SaveMaterial(Material material,string path)
        {var old=AssetDatabase.LoadAssetAtPath<Material>(path);if(old==null)AssetDatabase.CreateAsset(material,path);else{EditorUtility.CopySerialized(material,old);UnityEngine.Object.DestroyImmediate(material);}}
        static void AttachStone(GameObject root,int tier)
        {
            string type=tier==0?"Wandering":tier==1?"Grave":"Obsidian";
            foreach(Transform child in root.transform.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            var component=root.GetComponent<SoulBridgeArt>();if(component==null)component=root.AddComponent<SoulBridgeArt>();
            var loose=Instance("BLK_"+type+"_LooseVisual",root.transform,root.transform.position);
            var placed=Instance("BLK_"+type+"_PlacedVisual",root.transform,root.transform.position);
            loose.transform.localPosition=Vector3.zero;placed.transform.localPosition=Vector3.zero;
            component.looseVisual=loose.transform;component.placedVisual=placed.transform;
            component.bodies=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Stone").ToArray();
            component.seams=placed.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.Contains("seam")).ToArray();
            root.GetComponent<Renderer>().enabled=false;root.transform.localScale=new Vector3(.84f,.5f,.84f);component.Refresh();
        }
        static void ReplaceProp(Transform root,string name,out Renderer core)
        {
            foreach(Transform child in root.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            root.GetComponent<Renderer>().enabled=false;
            var art=Instance(name,root,new Vector3(root.position.x,0,root.position.z));
            // Keep physical metre dimensions despite the original primitive's non-uniform root scale.
            art.transform.localScale=new Vector3(1/root.lossyScale.x,1/root.lossyScale.y,1/root.lossyScale.z);
            core=art.GetComponentsInChildren<Renderer>().First(r=>r.name=="Core");
        }
        static void BuildShore(Transform parent,int index)
        {
            // The floor and cliff geometry comes from SoulBridgeImmersion, which is the same code
            // the game runs on Start. Keeping a second copy here is what let the baked scene and
            // Play mode drift apart, so this path only places the props the runtime does not.
            var points=SoulBridgeLandscape.Shores[index];int count=points.Length;
            MeshObject("ShoreFloor"+index,SoulBridgeImmersion.FloorMesh(points,index),Mat("ENV_Paving"),parent,true);
            MeshObject("ShoreCliff"+index,SoulBridgeImmersion.CliffMesh(points,index),Mat("ENV_Cliff"),parent);
            for(int i=0;i<count;i++)
            {
                Vector2 a=points[i],b=points[(i+1)%count];
                // Cliff blocks break up the straight silhouette, only below the floor.
                int segments=Mathf.CeilToInt(Vector2.Distance(a,b)/3);
                for(int k=0;k<segments;k++){var p=Vector2.Lerp(a,b,(k+.5f)/segments);Instance("ENV_CliffChunk",parent,new Vector3(p.x,-2.1f-(k%3)*.6f,p.y),1.2f,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg);}
            }
        }
        static void Dress(Transform parent)
        {
            var random=new System.Random(41);
            for(int shore=0;shore<3;shore++)
            {
                var poly=SoulBridgeLandscape.Shores[shore];float minZ=poly.Min(p=>p.y),maxZ=poly.Max(p=>p.y);
                for(int side=-1;side<=1;side+=2)
                {
                    for(float z=minZ+4;z<maxZ-2;z+=5.5f)
                    {
                        float x=side*(13+(float)random.NextDouble()*3);
                        if(!SoulBridgeLandscape.Inside(new Vector2(x,z),poly))continue;
                        Instance("ENV_BrokenColumn",parent,new Vector3(x,0,z),.8f+(float)random.NextDouble()*.6f);
                        Instance("ENV_LowWall",parent,new Vector3(x-side*.8f,0,z+1.8f),1,90);
                        Instance("ENV_RubbleCluster",parent,new Vector3(x-side*2,0,z-1.2f),.8f,(float)random.NextDouble()*360);
                    }
                    Instance("ENV_PartialArch",parent,new Vector3(side*10.5f,0,shore==0?-12:shore==1?27:66),1.15f,side*15);
                }
            }
            // Buried ruins and monoliths establish a world beyond the playable courts, not another square platform.
            for(int i=0;i<16;i++)
            {
                float side=i%2==0?-1:1;float z=-26+i*7;
                Instance("ENV_BrokenColumn",parent,new Vector3(side*(29+i%3*5),-6,z),2.5f+i%3,15*i);
                if(i%3==0)Instance("ENV_PartialArch",parent,new Vector3(side*33,-7,z+4),2.4f,20*i);
            }
            foreach(var p in new[]{new Vector3(-9,0,-11),new Vector3(9,0,-11),new Vector3(-10,0,28),new Vector3(10,0,28),new Vector3(-8,0,62),new Vector3(8,0,62)})
            {
                var lantern=Instance("PROP_SoulWell_Active",parent,p,.65f);
                var lamp=new GameObject("Courtyard lantern glow",typeof(Light));lamp.transform.SetParent(lantern.transform,false);lamp.transform.localPosition=Vector3.up*1.1f;
                var light=lamp.GetComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.62f,.26f);light.intensity=1.1f;light.range=4;light.shadows=LightShadows.None;
            }
        }
        static void AddLabel(string text,Vector3 p,Transform parent)
        {var go=new GameObject(text,typeof(TextMesh));go.transform.SetParent(parent,false);go.transform.position=p;go.transform.rotation=Quaternion.Euler(90,0,0);var label=go.GetComponent<TextMesh>();label.text=text;label.fontSize=48;label.characterSize=.06f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(.8f,.69f,.48f);}
        static void DressTraveler(BoxPlayer player)
        {
            var head=player.animator.GetBoneTransform(HumanBodyBones.Head);
            if(head==null)return;
            var old=head.Find("Traveler leather hat");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();int sides=16;
            var rings=new[]{new Vector3(0,-.04f,.56f),new Vector3(0,.04f,.34f),new Vector3(.015f,.45f,.22f),new Vector3(-.13f,.73f,.08f),new Vector3(-.24f,.67f,.001f)};
            for(int j=0;j<rings.Length;j++)for(int i=0;i<=sides;i++){float a=i*Mathf.PI*2/sides;vertices.Add(new Vector3(rings[j].x+Mathf.Cos(a)*rings[j].z,rings[j].y,Mathf.Sin(a)*rings[j].z));uv.Add(new Vector2((float)i/sides,j*.25f));}
            for(int j=0;j<rings.Length-1;j++)for(int i=0;i<sides;i++){int a=j*(sides+1)+i,b=a+sides+1;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1,a+1,b,a,b+1,b,a+1});}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            var hat=MeshObject("Traveler leather hat",mesh,Mat("CHR_Leather"),head);hat.transform.position=head.position+Vector3.up*.12f;hat.transform.rotation=Quaternion.identity;
            var spine=player.animator.GetBoneTransform(HumanBodyBones.Chest);if(spine==null)spine=player.animator.GetBoneTransform(HumanBodyBones.Spine);
            if(spine!=null)
            {
                old=spine.Find("Traveler cloth cape");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                vertices.Clear();uv.Clear();triangles.Clear();
                for(int row=0;row<5;row++)for(int col=0;col<7;col++){float t=row/4f,x=col/6f*2-1;vertices.Add(new Vector3(x*Mathf.Lerp(.25f,.52f,t),.18f-t*.95f,-.17f-t*.10f+Mathf.Abs(x)*.12f+Mathf.Sin(col*2)*.035f));uv.Add(new Vector2(col/6f,t));}
                for(int row=0;row<4;row++)for(int col=0;col<6;col++){int a=row*7+col;triangles.AddRange(new[]{a,a+1,a+7,a+1,a+8,a+7,a+7,a+1,a,a+7,a+8,a+1});}
                mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();var cape=MeshObject("Traveler cloth cape",mesh,Mat("CHR_Cloth"),spine);cape.transform.position=spine.position;cape.transform.rotation=Quaternion.identity;
            }
        }
    }
}
