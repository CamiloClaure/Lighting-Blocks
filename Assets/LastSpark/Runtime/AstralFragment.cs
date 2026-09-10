using UnityEngine;

namespace LightingBlocks
{
    public enum FragmentKind { Starlight, Life, Ancient }

    [RequireComponent(typeof(PullableBox))]
    public sealed class AstralFragment : MonoBehaviour
    {
        public string id;
        public FragmentKind kind;
        public PullableBox Box { get; private set; }
        public AstralAltar Altar { get; set; }
        LineRenderer influence;
        public bool CanLift => kind != FragmentKind.Ancient || LastSparkGame.Instance == null || LastSparkGame.Instance.strength > 0;
        public double BaseProduction => kind == FragmentKind.Ancient ? 8 : kind == FragmentKind.Starlight ? 1 : 0;
        public string Title => kind == FragmentKind.Ancient ? "Ancient Heart" : kind == FragmentKind.Life ? "Life Fragment" : "Starlight Fragment";
        void Awake()
        {
            Box = GetComponent<PullableBox>();
            if (kind == FragmentKind.Life)
            {
                var ring = new GameObject("Placeholder - Life influence radius"); ring.transform.SetParent(transform,false);
                influence = ring.AddComponent<LineRenderer>(); influence.useWorldSpace = true; influence.loop = true;
                influence.positionCount = 64; influence.startWidth = influence.endWidth = .045f;
                influence.sharedMaterial = GetComponent<Renderer>().sharedMaterial;
            }
        }
        void LateUpdate()
        {
            if (influence == null || LastSparkGame.Instance == null) return;
            influence.enabled = Box.Holder != null || LastSparkGame.Instance.player.NearbyBox == Box;
            if (!influence.enabled) return;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2 / 64;
                influence.SetPosition(i, new Vector3(transform.position.x + Mathf.Cos(angle) * LastSparkGame.Instance.balance.lifeRadius, .09f,
                    transform.position.z + Mathf.Sin(angle) * LastSparkGame.Instance.balance.lifeRadius));
            }
        }
        public void Released() { if (LastSparkGame.Instance != null) LastSparkGame.Instance.TrySeat(this); }
    }
}
