using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks.Editor
{
    [InitializeOnLoad] public static class SpiritPathLook
    {
        const string Request="Library/SpiritPath.apply-request",Result="Library/SpiritPath.apply-result.txt";
        const string Scene="Assets/Scenes/SampleScene.unity",SkyPath="Assets/LightingBlocks/Art/SpectralForest/Materials/NightSky.mat";
        static SpiritPathLook(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||!File.Exists(Request))return;
            File.Delete(Request);
            try{Apply();File.WriteAllText(Result,"SUCCESS: spirit path look applied.");}
            catch(Exception e){File.WriteAllText(Result,e.ToString());Debug.LogException(e);}
        }
        static Color Hex(string h){ColorUtility.TryParseHtmlString(h,out var c);return c;}
        static T Load<T>(string p)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(p);
        // Volume components are sub-assets of the profile, so an added one has to be hidden and attached like the authored four.
        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component))
            {
                component = profile.Add<T>(false);
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                if (EditorUtility.IsPersistent(profile)) AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true; EditorUtility.SetDirty(component); return component;
        }
        [MenuItem("Tools/Lighting Blocks/Legacy/Apply Spirit Path Look")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play before applying the look.");
            var scene=EditorSceneManager.GetActiveScene();if(scene.path!=Scene)scene=EditorSceneManager.OpenScene(Scene);
            var pipeline=Load<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var rp=new SerializedObject(pipeline);rp.FindProperty("m_ShadowDistance").floatValue=130f;rp.FindProperty("m_ColorGradingMode").intValue=1;rp.FindProperty("m_ColorGradingLutSize").intValue=64;rp.FindProperty("m_AdditionalLightsShadowmapResolution").intValue=4096;rp.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);
            var renderer=Load<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
            // ScreenSpaceAmbientOcclusion is internal to URP, so the feature is matched by type name rather than cast.
            foreach(var feature in renderer.rendererFeatures)
            {
                if(feature==null||feature.GetType().Name!="ScreenSpaceAmbientOcclusion")continue;
                var so=new SerializedObject(feature);so.FindProperty("m_Settings.Intensity").floatValue=.5f;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);
            }
            var profile=Load<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            Component<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom=Component<Bloom>(profile);bloom.threshold.Override(1f);bloom.intensity.Override(.55f);bloom.scatter.Override(.65f);bloom.tint.Override(new Color(.85f,.9f,1f));bloom.highQualityFiltering.Override(true);
            // Spec said +0.3, but the spec assumed skybox ambient; with the gradient ambient below, ACES needs +0.6 or the forest goes to silhouette.
            var grade=Component<ColorAdjustments>(profile);grade.postExposure.Override(.6f);grade.contrast.Override(12f);grade.saturation.Override(8f);
            var split=Component<SplitToning>(profile);split.shadows.Override(Hex("#3A2E6B"));split.highlights.Override(Hex("#FFC27A"));split.balance.Override(-10f);
            var smh=Component<ShadowsMidtonesHighlights>(profile);smh.shadows.Override(new Vector4(.9f,.9f,1f,-.03f));smh.highlights.Override(new Vector4(1f,1f,1f,.05f));
            var dof=Component<DepthOfField>(profile);dof.mode.Override(DepthOfFieldMode.Gaussian);dof.gaussianStart.Override(58f);dof.gaussianEnd.Override(95f);dof.gaussianMaxRadius.Override(1.2f);dof.highQualitySampling.Override(true);
            var vignette=Component<Vignette>(profile);vignette.intensity.Override(.3f);vignette.smoothness.Override(.4f);
            var grain=Component<FilmGrain>(profile);grain.type.Override(FilmGrainLookup.Thin1);grain.intensity.Override(.2f);
            Component<ChromaticAberration>(profile).intensity.Override(.07f);
            EditorUtility.SetDirty(profile);
            var game=UnityEngine.Object.FindAnyObjectByType<GameManager>();var camera=game.cameraRig.GetComponent<Camera>();
            camera.fieldOfView=26f;camera.clearFlags=CameraClearFlags.Skybox;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())
            {if(light.type!=LightType.Directional)continue;light.transform.rotation=Quaternion.Euler(40f,200f,0f);light.shadows=LightShadows.Soft;light.shadowStrength=.7f;}
            var config=Load<ForestConfiguration>("Assets/LightingBlocks/Art/SpectralForest/Configuration/Forest.asset");
            // Ambient sits about 2x the spec's gradient: those values were meant as 0.2-0.5 of an HDR sky, and this scene has no sky light otherwise.
            config.fogStart=90f;config.fogEnd=260f;config.ambientSky=Hex("#2A2E5E");config.ambientEquator=Hex("#332C58");config.ambientGround=Hex("#121226");config.moonColor=Hex("#8FB4FF");config.moonIntensity=.6f;EditorUtility.SetDirty(config);
            // Keels hang under the islands with the moon behind them; a lavender edge glow and a little self-fill keep the rock silhouette readable against the void.
            var cliff=config.cliff;
            if(cliff!=null){if(cliff.HasProperty("_RimColor"))cliff.SetColor("_RimColor",new Color(.3f,.26f,.5f));if(cliff.HasProperty("_SoftFill"))cliff.SetFloat("_SoftFill",.12f);EditorUtility.SetDirty(cliff);}
            // --- Spirit Path pass: emissive accents, foliage rim readability, mist shader hookup. All null-safe so a missing material is skipped, never thrown. ---
            Material Mat(string name){var guid=AssetDatabase.FindAssets(name+" t:Material").FirstOrDefault(g=>System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))==name);return guid==null?null:AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));}
            void Emit(Material m,Color c){if(m!=null&&m.HasProperty("_EmissionColor")){m.SetColor("_EmissionColor",c);EditorUtility.SetDirty(m);}}
            // Wisdom Tree emission (Tier 3.5): #6A5AA8 x1.5 bark, #A38AD0 x1.2 leaves.
            Emit(Mat("Wisdom_Bark"),new Color(.40f,.34f,.62f));Emit(Mat("Wisdom_Leaves"),new Color(.20f,.16f,.30f));
            // ponytail: the canopy still reads pale lavender, not the concept's blue-violet. Darkening
            // Wisdom_Leaves _BaseColor here and in WisdomTree's per-slot block both failed to change
            // it, so the pale cards are some other renderer. Find that renderer before tuning again.
            // Glowing plants (Tier 4.8).
            Emit(Mat("LuminousPlants"),new Color(.45f,.30f,1.2f));
            // Foliage readability (Tier 3): forest trees are black silhouettes without the cliff's rim/fill treatment.
            var treeMats=new HashSet<Material>();
            foreach(var tree in config.trees){if(tree==null)continue;foreach(var mat in tree.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials))if(mat!=null)treeMats.Add(mat);}
            foreach(var mat in treeMats)
            {
                if(mat.HasProperty("_RimColor"))mat.SetColor("_RimColor",new Color(.16f,.14f,.32f));
                if(mat.HasProperty("_SoftFill"))mat.SetFloat("_SoftFill",.05f);
                EditorUtility.SetDirty(mat);
            }
            // Crystal saturation: mirror TwilightBuilder's tint*2.2 HDR _BaseColor so a rebuild agrees with the look pass.
            var crystalHex=new[]{"#4AC7F5","#BE78E7","#FFC66F"};for(int t=0;t<3;t++){var tint=Hex(crystalHex[t]);var crystal=Load<Material>("Assets/LightingBlocks/Art/SpectralForest/Twilight/Materials/Crystal"+t+".mat");if(crystal!=null){crystal.SetColor("_BaseColor",new Color(tint.r*2.2f,tint.g*2.2f,tint.b*2.2f,1));EditorUtility.SetDirty(crystal);}}
            // Mist (Tier 5, shader contract with another agent).
            var mist=Load<Material>("Assets/LightingBlocks/Shaders/M_PlayerFog.mat");
            if(mist!=null)
            {
                if(mist.HasProperty("_MistColor"))mist.SetColor("_MistColor",new Color(.36f,.30f,.62f,1f));
                if(mist.HasProperty("_MistTop"))mist.SetFloat("_MistTop",-2f);
                if(mist.HasProperty("_MistDepth"))mist.SetFloat("_MistDepth",28f);
                if(mist.HasProperty("_MistStrength"))mist.SetFloat("_MistStrength",.85f);
                if(mist.HasProperty("_VoidMist"))mist.SetFloat("_VoidMist",.45f);
                EditorUtility.SetDirty(mist);
            }
            // --- end Spirit Path pass ---
            // Play applies these from the config at runtime; mirroring them keeps the Scene view honest.
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=config.haze;RenderSettings.fogStartDistance=config.fogStart;RenderSettings.fogEndDistance=config.fogEnd;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=config.ambientSky;RenderSettings.ambientEquatorColor=config.ambientEquator;RenderSettings.ambientGroundColor=config.ambientGround;
            // The authored sky shader may still be in flight, so fall back to the built-in procedural sky.
            var custom=Shader.Find("LightingBlocks/Night Sky");var shader=custom!=null?custom:Shader.Find("Skybox/Procedural");
            var material=Load<Material>(SkyPath);if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,SkyPath);}
            material.shader=shader;
            void Set(string name,float value){if(material.HasProperty(name))material.SetFloat(name,value);}
            void SetColor(string name,Color value){if(material.HasProperty(name))material.SetColor(name,value);}
            if(custom!=null)
            // Bigger, brighter, fewer stars survive the 1.2px Gaussian DoF that erased 1-pixel points; nebula adds a faint lavender wash near the horizon.
            {SetColor("_SkyTop",Hex("#0B0D24"));SetColor("_SkyHorizon",Hex("#1A1B3D"));SetColor("_SkyBottom",Hex("#07081A"));Set("_StarDensity",.5f);Set("_StarBrightness",9f);Set("_StarCells",140f);Set("_StarSize",6f);SetColor("_StarTint",new Color(.85f,.9f,1f));SetColor("_Nebula",Hex("#6A5AA8"));}
            else
            {Set("_SunDisk",0f);Set("_Exposure",.2f);Set("_AtmosphereThickness",.5f);SetColor("_SkyTint",Hex("#1A1B3D"));SetColor("_GroundColor",Hex("#0A0A14"));}
            RenderSettings.skybox=material;RenderSettings.defaultReflectionMode=DefaultReflectionMode.Skybox;EditorUtility.SetDirty(material);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
