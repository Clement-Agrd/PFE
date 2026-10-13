using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;
using Core.StatsSystem;
using Core.Village;

namespace Core.InventorySystem.UI
{
    using StatType = EnumStats.StatTypes;

    /// <summary>
    /// Bulle de description d'un objet, affichée près du curseur au survol d'une case :
    /// nom, description, effets d'un consommable (soin, buffs, régénération + durées) et
    /// détail complet des améliorations de la Forge (niveau, bonus actuel, bonus par niveau,
    /// prochain palier, coût et stock disponible).
    /// </summary>
    public sealed class ItemDescriptionPanelUI : MonoBehaviour
    {
        public static ItemDescriptionPanelUI Instance { get; private set; }

        [SerializeField] private RectTransform root;
        [SerializeField] private TMP_Text body;
        [Tooltip("Décalage par rapport au curseur, en pixels.")]
        [SerializeField] private Vector2 cursorOffset = new(24f, -24f);
        [Tooltip("Laisser vide pour retrouver la Forge dans la scène.")]
        [SerializeField] private ForgeService forge;

        [Header("Couleurs")]
        [SerializeField] private Color titleColor = new(0.95f, 0.78f, 0.35f);
        [SerializeField] private Color goodColor = new(0.5f, 0.88f, 0.5f);
        [SerializeField] private Color badColor = new(0.95f, 0.45f, 0.4f);
        [SerializeField] private Color mutedColor = new(0.7f, 0.72f, 0.76f);

        private Canvas _canvas;
        private bool _visible;

        private void Awake()
        {
            Instance = this;
            _canvas = GetComponentInParent<Canvas>();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (_visible) FollowPointer();
        }

        public void Show(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || stack.Definition == null) { Hide(); return; }

            body.text = Build(stack);
            SetVisible(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            FollowPointer();
        }

        public void Hide() => SetVisible(false);

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (root != null) root.gameObject.SetActive(visible);
        }

        private void FollowPointer()
        {
            if (Mouse.current == null || _canvas == null) return;

            Vector2 screen = Mouse.current.position.ReadValue();
            var canvasRect = (RectTransform)_canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);

            // Bascule de l'autre côté du curseur quand on approche du bord de l'écran.
            Rect r = canvasRect.rect;
            bool flipX = local.x + cursorOffset.x + root.rect.width > r.xMax;
            bool flipY = local.y + cursorOffset.y - root.rect.height < r.yMin;
            root.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = local + new Vector2(flipX ? -cursorOffset.x : cursorOffset.x,
                                                         flipY ? -cursorOffset.y : cursorOffset.y);
        }

        #region Texte

        private string Build(ItemStack stack)
        {
            ItemDefinition item = stack.Definition;
            var sb = new StringBuilder();

            sb.Append("<size=130%><b>").Append(Colored(item.DisplayName, titleColor)).Append("</b></size>");
            if (stack.UpgradeLevel > 0) sb.Append(' ').Append(Colored($"+{stack.UpgradeLevel}", goodColor));
            sb.Append('\n').Append(Colored(CategoryLabel(item), mutedColor));

            if (!string.IsNullOrWhiteSpace(item.Description))
                sb.Append("\n\n").Append(item.Description);

            if (item.IsConsumable) AppendConsumable(sb, item, stack.Quantity);
            AppendUpgrades(sb, stack);

            return sb.ToString();
        }

        private void AppendConsumable(StringBuilder sb, ItemDefinition item, int quantity)
        {
            sb.Append("\n\n<b>Effets</b>");
            bool any = false;

            if (item.HealAmount > 0)
            {
                sb.Append('\n').Append(Colored($"+{item.HealAmount} PV", goodColor)).Append(" (instantané)");
                any = true;
            }

            foreach (ConsumableStatEffect e in item.StatEffects)
            {
                sb.Append('\n').Append(Line(e.type, e.value, e.modifierType)).Append($" pendant {e.duration:0.#} s");
                any = true;
            }

            if (item.RegenPercentPerSecond > 0f && item.RegenDuration > 0f)
            {
                float total = item.RegenPercentPerSecond * item.RegenDuration;
                sb.Append('\n').Append(Colored($"Régénère {item.RegenPercentPerSecond:0.##} % des PV max par seconde", goodColor))
                  .Append($" pendant {item.RegenDuration:0.#} s (soit {total:0.#} % au total)");
                any = true;
            }

            if (!any) sb.Append('\n').Append(Colored("Aucun effet.", mutedColor));
            if (quantity > 1) sb.Append('\n').Append(Colored($"En stock : {quantity}", mutedColor));
            sb.Append("\n\n").Append(Colored("Clic droit : consommer", mutedColor));
        }

        private void AppendUpgrades(StringBuilder sb, ItemStack stack)
        {
            ForgeService f = ResolveForge();
            ForgeDefinition def = f != null ? f.Definition : null;
            if (def == null || !def.IsUpgradable(stack.Definition)) return;

            ForgeCategoryRules rules = def.GetRules(stack.Definition.Category);
            int level = stack.UpgradeLevel;
            int max = def.AbsoluteMaxLevel;
            int unlocked = def.GetMaxItemLevel(f.ForgeLevel);

            sb.Append("\n\n<b>Améliorations</b> (Forge)");
            sb.Append($"\nNiveau : <b>+{level}</b> / +{max}");
            sb.Append(Colored($"   (Forge niv. {f.ForgeLevel} : jusqu'à +{unlocked})", mutedColor));

            if (rules.bonuses != null && rules.bonuses.Length > 0)
            {
                sb.Append("\n\nBonus actuel :");
                if (level == 0) sb.Append('\n').Append(Colored("Aucun (non amélioré)", mutedColor));
                foreach (UpgradeBonus b in rules.bonuses)
                    if (level > 0) sb.Append('\n').Append(Line(b.stat, b.valuePerLevel * level, b.modifierType));

                sb.Append("\n\nPar niveau :");
                foreach (UpgradeBonus b in rules.bonuses)
                    sb.Append('\n').Append(Line(b.stat, b.valuePerLevel, b.modifierType));

                if (!rules.appliesToPlayerStats)
                    sb.Append('\n').Append(Colored("Bonus appliqué par le piège lui-même, pas au joueur.", mutedColor));
            }

            if (level >= max)
            {
                sb.Append("\n\n").Append(Colored("Niveau maximum atteint.", goodColor));
                return;
            }

            int next = level + 1;
            sb.Append($"\n\n<b>Prochain niveau : +{next}</b>");
            if (rules.bonuses != null)
                foreach (UpgradeBonus b in rules.bonuses)
                    sb.Append('\n').Append(Line(b.stat, b.valuePerLevel * next, b.modifierType))
                      .Append(Colored($"  (actuel : {Value(b.valuePerLevel * level, b.modifierType)})", mutedColor));

            if (next > unlocked)
            {
                sb.Append('\n').Append(Colored($"Nécessite la Forge niveau {def.GetRequiredForgeLevel(next)}.", badColor));
            }

            Inventory hdv = f.HdvInventory;
            ResourceCost[] cost = def.GetCost(next);
            if (cost.Length > 0)
            {
                sb.Append("\nCoût :");
                foreach (ResourceCost c in cost)
                {
                    if (c.item == null) continue;
                    int owned = hdv != null ? hdv.Count(c.item) : 0;
                    Color col = owned >= c.amount ? goodColor : badColor;
                    sb.Append('\n').Append(Colored($"{c.amount} {c.item.DisplayName}", col))
                      .Append(Colored($"  (stock : {owned})", mutedColor));
                }
            }
        }

        private ForgeService ResolveForge()
        {
            if (forge == null) forge = FindFirstObjectByType<ForgeService>();
            return forge;
        }

        private string Line(StatType stat, float value, StatModifierType type)
        {
            Color c = value >= 0f ? goodColor : badColor;
            return Colored($"{Value(value, type)} {StatLabel(stat)}", c);
        }

        private static string Value(float value, StatModifierType type)
        {
            string sign = value >= 0f ? "+" : "-";
            float abs = Mathf.Abs(value);
            return type == StatModifierType.Flat ? $"{sign}{abs:0.##}" : $"{sign}{abs * 100f:0.#} %";
        }

        private static string StatLabel(StatType stat) => stat switch
        {
            StatType.Life => "PV max",
            StatType.Stamina => "Endurance",
            StatType.PhysicDamage => "Dégâts physiques",
            StatType.MagicDamage => "Dégâts magiques",
            StatType.DefensePhysic => "Défense physique",
            StatType.DefenseMagic => "Défense magique",
            StatType.Speed => "Vitesse",
            StatType.AttackSpeed => "Vitesse d'attaque",
            _ => stat.ToString()
        };

        private static string CategoryLabel(ItemDefinition item) => item.Category switch
        {
            ItemCategory.Weapon => "Arme",
            ItemCategory.Armor => item.ArmorSlot switch
            {
                ArmorSlotType.Head => "Armure — Casque",
                ArmorSlotType.Chest => "Armure — Plastron",
                ArmorSlotType.Legs => "Armure — Jambières",
                ArmorSlotType.Feet => "Armure — Bottes",
                _ => "Armure"
            },
            ItemCategory.Consumable => "Nourriture / Boisson",
            ItemCategory.Material => "Ressource",
            ItemCategory.Trap => "Piège",
            ItemCategory.Quest => "Quête",
            _ => "Objet"
        };

        private static string Colored(string text, Color color)
            => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        #endregion
    }
}
