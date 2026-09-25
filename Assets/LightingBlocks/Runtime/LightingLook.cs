using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace LightingBlocks
{
    public sealed class LightingLook : MonoBehaviour
    {
        VolumeProfile owned;RenderPipelineAsset previous;UniversalRenderPipelineAsset selected;
        void Start()
        {
            previous=QualitySettings.renderPipeline;selected=Resources.Load<UniversalRenderPipelineAsset>("LightingPipeline");if(selected!=null)QualitySettings.renderPipeline=selected;
            var camera=GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;RenderSettings.skybox=null;
            camera.allowDynamicResolution=false;
            var volume=gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;
            owned=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=owned;
            var bloom=owned.Add<Bloom>(true);bloom.intensity.Override(.10f);bloom.scatter.Override(.15f);bloom.threshold.Override(1.3f);
            owned.Add<DepthOfField>(true).mode.Override(DepthOfFieldMode.Off);
            owned.Add<MotionBlur>(true).intensity.Override(0);
        }
        void OnDestroy(){if(selected!=null&&QualitySettings.renderPipeline==selected)QualitySettings.renderPipeline=previous;if(owned!=null){foreach(var c in owned.components)Destroy(c);Destroy(owned);}}
    }
}
