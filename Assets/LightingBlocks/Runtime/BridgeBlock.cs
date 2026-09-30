using UnityEngine;

namespace LightingBlocks
{
    public sealed class BridgeBlock : MonoBehaviour
    {
        public string id;
        public int tier;
        public bool placed;
        public int gap, column, row;
        public bool rewarded;
        public bool Connected { get; set; }
        // Occupancy lighting only. Runtime-only: never saved (do not touch BlockSave / ExportSave).
        // 0 = dark, 1 = fully lit.
        [System.NonSerialized] public float lit;
        // LightingFog.CullTiles: every renderer under the tile, and whether they are forced off.
        [System.NonSerialized] public Renderer[] renderers;
        [System.NonSerialized] public bool culled;
        // GameManager.Recalculate: the look this tile was last painted with; null = never painted.
        [System.NonSerialized] public (bool placed, bool connected, int gap, int row)? painted;
        public PullableBox Box => GetComponent<PullableBox>();
        public int RequiredStrength => BridgeProfile.Current.equalWeight ? 0 : tier == 0 ? 0 : tier == 1 ? 1 : 3;
        public static string TierName(int tier) => tier == 0 ? "Wandering Stone" : tier == 1 ? "Grave Stone" : "Ancient Obsidian";
        // Matches CrystalEdge<tier> emission in TwilightBuilder: blue, violet, amber.
        public static string TierColour(int tier) => tier == 0 ? "blue" : tier == 1 ? "violet" : "amber";
        public string Title => TierName(tier);
        public float SoulValue => tier == 0 ? 1 : tier == 1 ? 3 : 8;
        public bool CanLift => !placed && GameManager.Instance != null && GameManager.Instance.strength >= RequiredStrength;
        public void Released() { if (GameManager.Instance != null) GameManager.Instance.TryPlace(this); }
    }
}
