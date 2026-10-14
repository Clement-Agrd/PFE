using System;
using Core.StatsSystem;
using TMPro;
using UnityEngine;
using static Core.PauseMenu.PauseUiKit;

namespace Core.BarracksSystem
{
    /// <summary>Shared read-only comparison for hero assignment and barracks upgrades.</summary>
    public sealed class BarracksStatsPreview
    {
        private sealed class Row
        {
            public TMP_Text Current, Future, Delta;
        }

        private readonly EnumStats.StatTypes[] _types;
        private readonly Row[] _rows;
        private readonly TMP_Text _subtitle;
        public RectTransform Root { get; }
        public bool HasChanges { get; private set; }

        public BarracksStatsPreview(Transform parent, string name)
        {
            var card = NewImage(name, parent, CardRaised, 16);
            card.raycastTarget = true;
            Root = card.rectTransform;
            var title = NewText("Title", Root, "Statistiques des troupes", 29, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -20), new Vector2(630, 45));
            _subtitle = NewText("ComparisonTarget", Root, "", 21, TextMuted);
            Anchor(_subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -68), new Vector2(630, 58));
            var header = NewRect("ColumnHeaders", Root);
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
            header.pivot = new Vector2(.5f, 1); header.anchoredPosition = new Vector2(0, -135); header.sizeDelta = new Vector2(-52, 32);
            Column(header, "Actuel", .43f, .61f, Accent);
            Column(header, "Après", .61f, .80f, Accent);
            Column(header, "Écart", .80f, 1, Accent);
            var list = NewRect("Stats", Root);
            list.anchorMin = new Vector2(0, 1); list.anchorMax = Vector2.one;
            list.pivot = new Vector2(.5f, 1); list.anchoredPosition = new Vector2(0, -177); list.sizeDelta = new Vector2(-52, 460);
            VStack(list.gameObject, 6);
            _types = (EnumStats.StatTypes[])Enum.GetValues(typeof(EnumStats.StatTypes));
            _rows = new Row[_types.Length + 2];
            for (int i = 0; i < _types.Length; i++) _rows[i] = CreateRow(list, _types[i].ToString(), Label(_types[i]));
            _rows[_types.Length] = CreateRow(list, "SoldierCount", "Nombre de soldats");
            _rows[_types.Length + 1] = CreateRow(list, "RespawnTime", "Réapparition (s)");
            var legend = NewText("ComparisonLegend", Root, "Vert : hausse · Rouge : baisse · Gris : identique", 18, TextMuted, TextAlignmentOptions.Center);
            Anchor(legend.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 18), new Vector2(650, 32));
        }

        private static TMP_Text Column(Transform parent, string text, float min, float max, Color color)
        {
            var label = NewText(text, parent, text, 22, color, TextAlignmentOptions.Right);
            label.rectTransform.anchorMin = new Vector2(min, 0);
            label.rectTransform.anchorMax = new Vector2(max, 1);
            label.rectTransform.offsetMin = new Vector2(4, 0);
            label.rectTransform.offsetMax = new Vector2(-4, 0);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private static Row CreateRow(Transform parent, string name, string label)
        {
            var root = NewRect(name, parent);
            Layout(root.gameObject, height: 40);
            var statName = NewText("StatName", root, label, 22, TextMain);
            statName.rectTransform.anchorMin = Vector2.zero;
            statName.rectTransform.anchorMax = new Vector2(.43f, 1);
            statName.rectTransform.offsetMin = Vector2.zero;
            statName.rectTransform.offsetMax = Vector2.zero;
            statName.textWrappingMode = TextWrappingModes.NoWrap;
            return new Row { Current = Column(root, "Current", .43f, .61f, TextMuted),
                Future = Column(root, "Future", .61f, .8f, TextMain), Delta = Column(root, "Delta", .8f, 1, TextMuted) };
        }

        public void Refresh(Barracks barracks, int futureLevel, heros futureHero, string description)
        {
            if (barracks == null || barracks.Data == null) return;
            _subtitle.text = description;
            HasChanges = false;
            var data = barracks.Data;
            int current = barracks.CurrentLevel;
            for (int i = 0; i < _types.Length; i++)
                SetRow(_rows[i], data.GetSoldierStat(current, _types[i], barracks.AssignedHero), data.GetSoldierStat(futureLevel, _types[i], futureHero));
            SetRow(_rows[_types.Length], data.GetSoldierCount(current, barracks.AssignedHero), data.GetSoldierCount(futureLevel, futureHero));
            SetRow(_rows[_types.Length + 1], data.GetRespawnTime(current, barracks.AssignedHero), data.GetRespawnTime(futureLevel, futureHero));
        }

        private void SetRow(Row row, float current, float future)
        {
            float delta = future - current;
            bool unchanged = Mathf.Abs(delta) < .0001f;
            Color color = unchanged ? TextMuted : delta > 0 ? Good : Danger;
            row.Current.text = current.ToString("0.##");
            row.Future.text = future.ToString("0.##");
            row.Future.color = color;
            row.Delta.text = unchanged ? "—" : delta.ToString("+0.##;-0.##");
            row.Delta.color = color;
            HasChanges |= !unchanged;
        }

        private static string Label(EnumStats.StatTypes type) => type switch
        {
            EnumStats.StatTypes.Life => "PV",
            EnumStats.StatTypes.Stamina => "Endurance",
            EnumStats.StatTypes.PhysicDamage => "Dégâts physiques",
            EnumStats.StatTypes.MagicDamage => "Dégâts magiques",
            EnumStats.StatTypes.DefensePhysic => "Défense physique",
            EnumStats.StatTypes.DefenseMagic => "Défense magique",
            EnumStats.StatTypes.Speed => "Vitesse déplacement",
            EnumStats.StatTypes.AttackSpeed => "Vitesse attaque",
            _ => type.ToString()
        };
    }
}
