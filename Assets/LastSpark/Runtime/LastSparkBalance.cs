using UnityEngine;

namespace LightingBlocks
{
    [CreateAssetMenu(menuName = "Last Spark/Balance")]
    public sealed class LastSparkBalance : ScriptableObject
    {
        public float summonBase = 20;
        public float summonGrowth = 1.38f;
        public float lifeCost = 90;
        public float resonanceBase = 35;
        public float resonanceGrowth = 1.85f;
        public float resonanceMultiplier = 1.4f;
        public int resonanceCap = 10;
        public float strengthBase = 60;
        public float spiritCost = 220;
        public float spiritInterval = 12;
        public float bridgeCost = 300;
        public float finalCost = 1200;
        public float lifeRadius = 4.5f;
        public float lifeBonus = .5f;
        public float firstLightReward = 25;
        public float offlineHoursCap = 2;
        public float offlineMultiplier = .5f;
    }
}
