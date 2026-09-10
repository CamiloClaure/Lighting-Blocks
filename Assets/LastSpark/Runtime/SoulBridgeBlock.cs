using UnityEngine;

namespace LightingBlocks
{
    public sealed class SoulBridgeBlock : MonoBehaviour
    {
        public string id;
        public int tier;
        public bool placed;
        public int gap, column, row;
        public bool rewarded;
        public bool Connected { get; set; }
        public PullableBox Box => GetComponent<PullableBox>();
        public int RequiredStrength => tier == 0 ? 0 : tier == 1 ? 1 : 3;
        public string Title => tier == 0 ? "Wandering Stone" : tier == 1 ? "Grave Stone" : "Ancient Obsidian";
        public float SoulValue => tier == 0 ? 1 : tier == 1 ? 3 : 8;
        public bool CanLift => !placed && SoulBridgeGame.Instance != null && SoulBridgeGame.Instance.strength >= RequiredStrength;
        public void Released() { if (SoulBridgeGame.Instance != null) SoulBridgeGame.Instance.TryPlace(this); }
    }
}
