using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace LightingBlocks
{
    // A skill's emblem: its game-icons.net icon (Resources/UI/Icons, see CREDITS.txt) dressed by level.
    // The icon stays the same; each tier adds to the frame: grey when locked, bronze, silver with a soft
    // halo, then gold with a second ring and orbiting sparks at max level. Shared by the skill tree
    // nodes and the ability bar.
    public sealed class SkillBadge
    {
        public static readonly Color Dormant = new Color(.34f, .30f, .26f), Bronze = new Color(.74f, .47f, .24f),
            Silver = new Color(.86f, .88f, .95f), Gold = new Color(1f, .84f, .48f);
        public Image Halo { get; private set; }
        public Image Outer { get; private set; }
        public Image Icon { get; private set; }
        readonly Image[] sparks = new Image[4];
        float size;

        public static string IconName(string id)
        {
            switch (id)
            {
                case "speed": return "wingfoot";
                case "soulgain": return "sparkles";
                case "dash": return "sprint";
                case "grenade": return "meteor-impact";
                case "pull": return "magnet";
                default: return null; // root and the legacy powers keep their numerals
            }
        }
        // 0 locked, 3 at max; levels between spread over bronze (1) and silver (2).
        public static int Tier(int level, int cap) => level <= 0 ? 0 : level >= cap ? 3 : 1 + (level - 1) * 2 / Mathf.Max(1, cap - 1);
        public static Color TierColor(int tier) => tier <= 0 ? Dormant : tier == 1 ? Bronze : tier == 2 ? Silver : Gold;

        // Builds into a square parent of the given size; the halo goes behind the parent's other children.
        public static SkillBadge Build(RectTransform parent, string id, float size)
        {
            var badge = new SkillBadge { size = size };
            badge.Halo = Layer("Badge halo", parent, SkillTree.Dot(), size * 1.55f, Color.clear); badge.Halo.transform.SetAsFirstSibling();
            badge.Outer = Layer("Badge outer ring", parent, SkillTree.Ring(), size * 1.16f, Gold);
            badge.Icon = Layer("Badge icon", parent, GameHUD.IconSprite(IconName(id)), size * .58f, Color.white);
            for (int i = 0; i < badge.sparks.Length; i++) badge.sparks[i] = Layer("Badge spark " + i, parent, SkillTree.Dot(), size * .13f, Gold);
            return badge;
        }
        public static Image Layer(string name, RectTransform parent, Sprite sprite, float size, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = Vector2.one * size; rect.anchoredPosition = Vector2.zero;
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false; image.preserveAspect = true;
            return image;
        }

        // Dresses the badge for this level and returns the tier's frame colour for the caller's ring.
        public Color Apply(int level, int cap, float time)
        {
            int tier = Tier(level, cap); float pulse = .5f + .5f * Mathf.Sin(time * 2.2f);
            Icon.color = tier == 0 ? new Color(.55f, .50f, .45f, .55f) : tier == 1 ? new Color(.95f, .86f, .72f) : tier == 2 ? Color.white : new Color(1f, .93f, .70f);
            Halo.color = new Color(1f, .78f, .38f, tier == 2 ? .22f + .12f * pulse : tier == 3 ? .45f + .15f * pulse : 0);
            Outer.enabled = tier == 3;
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i].enabled = tier == 3;
                float angle = time * 1.6f + i * Mathf.PI * .5f;
                sparks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (size * .58f);
            }
            return TierColor(tier);
        }
    }

    // Lighting's in-game ability bar, bottom centre: Dash, Light Grenade and Spirit Pull, each with its
    // badge, key, level pips, a dark sweep while it recharges and a gold ring while the grenade charges.
    // A skill not learned yet shows grey with a padlock.
    public sealed class AbilityBar
    {
        public const float SlotSize = 72, Gap = 30;
        public sealed class Slot
        {
            public string id;
            public SkillBadge badge;
            public Image ring, cooldown, charge, padlock;
            public Image[] pips;
        }
        readonly List<Slot> slots = new List<Slot>();
        GameManager game;
        public IReadOnlyList<Slot> Slots => slots;

        public static AbilityBar Build(Transform parent, GameManager game, Font font)
        {
            var bar = new AbilityBar { game = game };
            var abilities = new[] { ("dash", "SHIFT"), ("grenade", "RMB"), ("pull", "F") };
            float width = abilities.Length * SlotSize + (abilities.Length - 1) * Gap;
            var root = new GameObject("Ability Bar", typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, 0); root.sizeDelta = new Vector2(width, SlotSize + 40); root.anchoredPosition = new Vector2(0, 20);
            for (int i = 0; i < abilities.Length; i++)
            {
                var (id, key) = abilities[i];
                var host = new GameObject("Ability " + id, typeof(RectTransform)).GetComponent<RectTransform>(); host.SetParent(root, false);
                host.anchorMin = host.anchorMax = host.pivot = new Vector2(0, 1); host.sizeDelta = Vector2.one * SlotSize; host.anchoredPosition = new Vector2(i * (SlotSize + Gap), 0);
                var slot = new Slot { id = id };
                SkillBadge.Layer("Backing", host, SkillTree.Disc(), SlotSize, new Color(.06f, .05f, .045f, .92f));
                slot.badge = SkillBadge.Build(host, id, SlotSize);
                slot.ring = SkillBadge.Layer("Ring", host, SkillTree.Ring(), SlotSize, SkillBadge.Dormant);
                slot.cooldown = SkillBadge.Layer("Cooldown", host, SkillTree.Disc(), SlotSize, new Color(0, 0, 0, .7f));
                slot.cooldown.type = Image.Type.Filled; slot.cooldown.fillMethod = Image.FillMethod.Radial360; slot.cooldown.fillOrigin = (int)Image.Origin360.Top; slot.cooldown.fillClockwise = false; slot.cooldown.fillAmount = 0;
                slot.charge = SkillBadge.Layer("Charge", host, SkillTree.Ring(), SlotSize * 1.1f, SkillBadge.Gold);
                slot.charge.type = Image.Type.Filled; slot.charge.fillMethod = Image.FillMethod.Radial360; slot.charge.fillOrigin = (int)Image.Origin360.Top; slot.charge.fillAmount = 0;
                slot.padlock = SkillBadge.Layer("Padlock", host, GameHUD.IconSprite("padlock"), 22, new Color(.95f, .90f, .82f, .9f));
                slot.padlock.rectTransform.anchoredPosition = new Vector2(SlotSize * .33f, -SlotSize * .33f);
                var caption = new GameObject("Key", typeof(RectTransform)).GetComponent<RectTransform>(); caption.SetParent(host, false);
                caption.anchorMin = caption.anchorMax = caption.pivot = new Vector2(.5f, 1); caption.sizeDelta = new Vector2(SlotSize + Gap, 18); caption.anchoredPosition = new Vector2(0, -SlotSize - 4);
                var text = caption.gameObject.AddComponent<Text>(); text.font = font; text.text = key; text.fontSize = 13; text.alignment = TextAnchor.MiddleCenter; text.color = new Color(.894f, .749f, .478f); text.raycastTarget = false;
                caption.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .9f);
                int cap = game.Cap(id); slot.pips = new Image[cap];
                for (int p = 0; p < cap; p++)
                {
                    slot.pips[p] = SkillBadge.Layer("Pip " + p, host, SkillTree.Dot(), 6, SkillBadge.Dormant);
                    slot.pips[p].rectTransform.anchoredPosition = new Vector2((p - (cap - 1) * .5f) * 9, -SlotSize * .5f - 30);
                }
                bar.slots.Add(slot);
            }
            return bar;
        }

        public void Refresh(float time)
        {
            if (game == null || game.player == null) return;
            var grenade = game.GetComponent<LightingGrenade>(); var pull = game.GetComponent<LightingPull>();
            foreach (var slot in slots)
            {
                int level = game.Level(slot.id), cap = game.Cap(slot.id);
                slot.ring.color = slot.badge.Apply(level, cap, time);
                slot.padlock.enabled = level <= 0;
                float recharge = 0, charge = 0; bool busy = false;
                if (slot.id == "dash") recharge = game.player.DashCooldownRemaining / BoxPlayer.DashCooldown;
                else if (slot.id == "grenade" && grenade != null) { busy = grenade.Busy; charge = grenade.Charging ? grenade.Charge : 0; }
                else if (slot.id == "pull" && pull != null) recharge = pull.CooldownLeft / LightingPull.Cooldown;
                slot.cooldown.fillAmount = busy ? 1 : Mathf.Clamp01(recharge);
                slot.cooldown.color = new Color(0, 0, 0, busy ? .4f : .7f);
                slot.charge.fillAmount = Mathf.Clamp01(charge);
                for (int p = 0; p < slot.pips.Length; p++) slot.pips[p].color = p < level ? SkillBadge.Gold : new Color(.26f, .23f, .20f);
            }
        }
    }
}
