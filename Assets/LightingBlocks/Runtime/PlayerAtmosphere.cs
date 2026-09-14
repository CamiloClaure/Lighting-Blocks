using UnityEngine;
namespace LightingBlocks
{
    public sealed class PlayerAtmosphere : MonoBehaviour
    {
        public Transform soul;
        public Renderer halo;
        Vector3 home;
        void Start(){if(soul!=null)home=soul.localPosition;}
        void Update()
        {
            if(soul!=null)soul.localPosition=home+new Vector3(Mathf.Sin(Time.time*.9f)*.08f,Mathf.Sin(Time.time*1.6f)*.11f,0);
            if(halo!=null && Camera.main!=null)halo.transform.rotation=Camera.main.transform.rotation;
            Shader.SetGlobalVector("_FogCenter", transform.position);
        }
    }
}
