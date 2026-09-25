using UnityEngine;
namespace LightingBlocks
{
    public sealed class PlayerAtmosphere : MonoBehaviour
    {
        public Transform soul;
        public Renderer halo;
        Vector3 home;
        void Start(){if(soul!=null)home=soul.localPosition;}
        void OnDisable() { Shader.SetGlobalColor("_LightingCharacterColor", Color.clear); }
        void Update()
        {
            if(soul!=null)soul.localPosition=home+new Vector3(Mathf.Sin(Time.time*.9f)*.08f,Mathf.Sin(Time.time*1.6f)*.11f,0);
            if(halo!=null && Camera.main!=null)halo.transform.rotation=Camera.main.transform.rotation;
            var game = GameManager.Instance;
            if (game != null && BridgeProfile.Current.isLighting && game.soulLight != null)
            {
                game.ApplyCharacterLight(); // Inspector tuning also updates during Play Mode.
                var light = game.soulLight;
                var p = light.transform.position;
                Shader.SetGlobalVector("_LightingCharacterPosition", new Vector4(p.x, p.y, p.z, light.range));
                var color = light.color.linear * (light.isActiveAndEnabled ? light.intensity * game.characterTileLight : 0);
                Shader.SetGlobalColor("_LightingCharacterColor", color);
            }
            else Shader.SetGlobalColor("_LightingCharacterColor", Color.clear);
            Shader.SetGlobalVector("_FogCenter", transform.position);
            if(GameManager.Instance!=null && GameManager.Instance.soulLight!=null)
                Shader.SetGlobalFloat("_SpectralRevealRadius",GameManager.Instance.soulLight.range);
        }
    }
}
