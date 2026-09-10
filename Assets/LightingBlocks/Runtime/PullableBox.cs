using UnityEngine;

namespace LightingBlocks
{
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class PullableBox : MonoBehaviour
    {
        public BoxCollider Shape { get; private set; }
        public Rigidbody Body { get; private set; }
        public BoxPlayer Holder { get; private set; }
        Renderer surface;
        MaterialPropertyBlock properties;
        Color baseColor;

        void Awake()
        {
            Shape = GetComponent<BoxCollider>();
            Body = GetComponent<Rigidbody>();
            Body.isKinematic = true;
            Body.useGravity = false;
            surface = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            baseColor = surface.sharedMaterial.GetColor("_BaseColor");
        }

        public bool Grab(BoxPlayer player)
        {
            if (Holder != null) return false;
            Holder = player;
            return true;
        }

        public void Release() { Holder = null; Highlight(false); }

        public void Highlight(bool nearby)
        {
            if (surface == null) return;
            surface.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", Holder != null ? new Color(1f, .73f, .22f) : nearby ? new Color(.4f, 1f, .85f) : baseColor);
            surface.SetPropertyBlock(properties);
        }

        // Kinematic boxes move only with their holder; releasing never leaves momentum.
        public void Translate(Vector3 delta)
        {
            Vector3 next = Body.position + delta;
            Body.position = next;
            // Keep queries and rendered geometry in sync even between physics ticks.
            transform.position = next;
        }

        void OnDisable()
        {
            if (Holder != null) Holder.Release();
        }
    }
}
