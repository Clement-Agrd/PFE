using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Core.InventorySystem;
using Core.LootSystem;
using Core.TowerDefense;
using static Core.PauseMenu.PauseUiKit;

namespace Core.PauseMenu
{
    /// <summary>
    /// Bestiaire : liste des ennemis à gauche (silhouette tant qu'ils n'ont pas été vaincus),
    /// fiche détaillée à droite (description, statistiques en barres, or et butin avec probabilités).
    /// </summary>
    public sealed class PauseBestiaryView
    {
        private sealed class ListEntry
        {
            public EnemyDefinition Enemy;
            public PauseButton Button;
            public Image Icon;
            public TMP_Text Kills;
        }

        private readonly List<ListEntry> _entries = new();
        private IReadOnlyList<EnemyDefinition> _enemies;
        private int _selected;
        private TMP_Text _counter;
        private RectTransform _detail;

        public RectTransform Root { get; private set; }
        public CanvasGroup Group { get; private set; }

        public void Build(Transform parent, IReadOnlyList<EnemyDefinition> enemies, Action onBack)
        {
            _enemies = enemies;

            Root = NewRect("BestiaryView", parent);
            Stretch(Root);
            Group = Root.gameObject.AddComponent<CanvasGroup>();

            RectTransform card = NewRect("Card", Root);
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 820f));
            var bg = card.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(28);
            bg.type = Image.Type.Sliced;
            bg.color = Card;
            card.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            VStack(card.gameObject, 22f, new RectOffset(48, 48, 40, 36));

            RectTransform header = NewRect("Header", card);
            Layout(header.gameObject, height: 64f);
            HStack(header.gameObject, 16f);
            TMP_Text title = NewText("Title", header, "BESTIAIRE", 52f, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            title.characterSpacing = 8f;
            Layout(title.gameObject, flexW: 1f);
            _counter = NewText("Counter", header, "", 28f, Accent, TextAlignmentOptions.Right, FontStyles.Bold);
            Layout(_counter.gameObject, 360f);

            Image line = NewImage("AccentLine", card, Accent, 2);
            Layout(line.gameObject, height: 4f);

            RectTransform body = NewRect("Body", card);
            Layout(body.gameObject, flexH: 1f, flexW: 1f);
            HStack(body.gameObject, 28f);

            // Colonne de gauche : liste
            RectTransform listCol = NewRect("ListColumn", body);
            Layout(listCol.gameObject, 470f, flexH: 1f);
            var listBg = listCol.gameObject.AddComponent<Image>();
            listBg.sprite = Rounded(20);
            listBg.type = Image.Type.Sliced;
            listBg.color = new Color(0f, 0f, 0f, 0.22f);
            RectTransform listContent = NewScroll(listCol, "List", 10f, new RectOffset(14, 14, 14, 14), out _);
            Stretch((RectTransform)listContent.parent.parent);

            for (int i = 0; i < _enemies.Count; i++) BuildListEntry(listContent, i);

            // Colonne de droite : fiche
            _detail = NewRect("Detail", body);
            Layout(_detail.gameObject, flexW: 1f, flexH: 1f);
            var detailBg = _detail.gameObject.AddComponent<Image>();
            detailBg.sprite = Rounded(20);
            detailBg.type = Image.Type.Sliced;
            detailBg.color = new Color(0f, 0f, 0f, 0.22f);

            RectTransform footer = NewRect("Footer", card);
            Layout(footer.gameObject, height: 64f);
            HStack(footer.gameObject, 16f, align: TextAnchor.MiddleRight);
            PauseButton back = NewButton(footer, "Retour", onBack, ButtonStyle.Primary, 28f);
            Layout(back.gameObject, 240f, 64f);
        }

        private void BuildListEntry(Transform list, int index)
        {
            EnemyDefinition enemy = _enemies[index];
            var entry = new ListEntry { Enemy = enemy };

            PauseButton button = NewButton(list, "", () => Select(index), ButtonStyle.Secondary, 28f);
            Layout(button.gameObject, height: 92f);
            entry.Button = button;
            button.Label.rectTransform.offsetMin = new Vector2(112f, 14f);
            button.Label.rectTransform.offsetMax = new Vector2(-16f, -14f);
            button.Label.alignment = TextAlignmentOptions.TopLeft;

            Image iconBg = NewImage("IconBg", button.transform, new Color(0f, 0f, 0f, 0.35f), 12);
            Anchor(iconBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(64f, 64f));
            entry.Icon = NewImage("Icon", iconBg.transform, Color.white);
            Stretch(entry.Icon.rectTransform, 6f);
            entry.Icon.preserveAspect = true;

            entry.Kills = NewText("Kills", button.transform, "", 22f, TextMuted, TextAlignmentOptions.BottomLeft);
            Stretch(entry.Kills.rectTransform);
            entry.Kills.rectTransform.offsetMin = new Vector2(112f, 14f);
            entry.Kills.rectTransform.offsetMax = new Vector2(-16f, -14f);

            _entries.Add(entry);
        }

        public void OnShown()
        {
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _enemies.Count - 1));
            Refresh();
        }

        private void Select(int index)
        {
            _selected = index;
            Refresh();
        }

        public void Refresh()
        {
            int discovered = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                ListEntry e = _entries[i];
                bool known = BestiaryData.IsDiscovered(e.Enemy);
                if (known) discovered++;

                e.Button.Label.text = known ? e.Enemy.DisplayName : "???";
                e.Kills.text = known ? $"Éliminé {BestiaryData.Kills(e.Enemy)} fois" : "Non découvert";
                e.Icon.sprite = e.Enemy.Icon;
                e.Icon.enabled = e.Enemy.Icon != null;
                e.Icon.color = known ? Color.white : new Color(0f, 0f, 0f, 0.85f); // silhouette
                e.Button.SetActive(i == _selected);
            }

            _counter.text = $"Découverts  {discovered} / {_enemies.Count}";
            BuildDetail();
        }

        #region Fiche

        private void BuildDetail()
        {
            for (int i = _detail.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_detail.GetChild(i).gameObject);

            if (_enemies.Count == 0 || _selected >= _enemies.Count)
            {
                TMP_Text empty = NewText("Empty", _detail, "Aucun ennemi répertorié.", 30f, TextMuted, TextAlignmentOptions.Center);
                Stretch(empty.rectTransform);
                return;
            }

            EnemyDefinition enemy = _enemies[_selected];
            bool known = BestiaryData.IsDiscovered(enemy);

            RectTransform content = NewScroll(_detail, "DetailScroll", 18f, new RectOffset(36, 36, 30, 30), out _);
            Stretch((RectTransform)content.parent.parent);

            // Portrait + titre
            RectTransform top = NewRect("Top", content);
            Layout(top.gameObject, height: 190f);
            HStack(top.gameObject, 28f);

            Image frame = NewImage("Portrait", top, new Color(0f, 0f, 0f, 0.4f), 20);
            Layout(frame.gameObject, 190f, 190f);
            Image portrait = NewImage("Icon", frame.transform, known ? Color.white : new Color(0f, 0f, 0f, 0.85f));
            Stretch(portrait.rectTransform, 14f);
            portrait.preserveAspect = true;
            portrait.sprite = enemy.Icon;
            portrait.enabled = enemy.Icon != null;

            RectTransform titles = NewRect("Titles", top);
            VStack(titles.gameObject, 6f, align: TextAnchor.MiddleLeft);
            Layout(titles.gameObject, flexW: 1f);
            TMP_Text name = NewText("Name", titles, known ? enemy.DisplayName : "???", 54f, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            Layout(name.gameObject, height: 66f);
            TMP_Text kills = NewText("Kills", titles,
                known ? $"Éliminé {BestiaryData.Kills(enemy)} fois" : "Vous n'avez jamais affronté cet ennemi",
                26f, known ? Accent : TextMuted);
            Layout(kills.gameObject, height: 34f);

            // Description
            string desc = known
                ? (string.IsNullOrWhiteSpace(enemy.Description) ? "Aucune information supplémentaire." : enemy.Description)
                : "Vainquez cet ennemi pour percer ses secrets.";
            TMP_Text description = NewText("Description", content, desc, 26f, known ? TextMain : TextMuted);
            description.lineSpacing = 6f;

            if (!known) return;

            AddHeading(content, "STATISTIQUES");
            float maxHp = 1f, maxSpeed = 1f, maxDamage = 1f;
            foreach (EnemyDefinition e in _enemies)
            {
                maxHp = Mathf.Max(maxHp, e.MaxHealth);
                maxSpeed = Mathf.Max(maxSpeed, e.Speed);
                maxDamage = Mathf.Max(maxDamage, e.BaseDamage);
            }
            AddStatBar(content, "Points de vie", enemy.MaxHealth.ToString(), enemy.MaxHealth / maxHp, Good);
            AddStatBar(content, "Vitesse", enemy.Speed.ToString("0.#"), enemy.Speed / maxSpeed, new Color(0.45f, 0.75f, 0.95f));
            AddStatBar(content, "Dégâts à la base", enemy.BaseDamage.ToString(), enemy.BaseDamage / maxDamage, Danger);

            AddHeading(content, "RÉCOMPENSES");
            AddRewardRow(content, null, "Or", $"{enemy.GoldReward}", null);

            var drops = new List<(ItemDefinition item, string chance, string amount)>();
            if (enemy.LootTable != null) CollectDrops(enemy.LootTable, 1f, drops, 0);
            if (drops.Count == 0)
            {
                TMP_Text none = NewText("NoLoot", content, "Aucun butin connu.", 24f, TextMuted);
                Layout(none.gameObject, height: 32f);
            }
            foreach (var d in drops) AddRewardRow(content, d.item.Icon, d.item.DisplayName, d.amount, d.chance);
        }

        private static void CollectDrops(LootTable table, float chanceFactor,
            List<(ItemDefinition, string, string)> output, int depth)
        {
            if (table == null || depth > 4) return;

            float totalWeight = 0f;
            foreach (LootEntry e in table.Entries) if (!e.Guaranteed) totalWeight += e.Weight;

            foreach (LootEntry e in table.Entries)
            {
                float chance = e.Guaranteed ? e.DropChance : (totalWeight > 0f ? e.Weight / totalWeight : 0f);
                chance *= chanceFactor;

                if (e.SubTable != null) { CollectDrops(e.SubTable, chance, output, depth + 1); continue; }
                if (e.Item == null) continue;

                string amount = e.MinAmount == e.MaxAmount ? $"×{e.MinAmount}" : $"×{e.MinAmount}-{e.MaxAmount}";
                string pct = chance >= 0.995f ? "Garanti" : $"{chance * 100f:0.#} %";
                output.Add((e.Item, pct, amount));
            }
        }

        private static void AddHeading(Transform parent, string text)
        {
            TMP_Text t = NewText("Heading_" + text, parent, text, 22f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            t.characterSpacing = 6f;
            Layout(t.gameObject, height: 40f);
        }

        private static void AddStatBar(Transform parent, string label, string value, float fill01, Color color)
        {
            RectTransform row = NewRect("Stat_" + label, parent);
            Layout(row.gameObject, height: 48f);
            HStack(row.gameObject, 20f);

            TMP_Text l = NewText("Label", row, label, 26f, TextMain);
            Layout(l.gameObject, 250f);

            RectTransform track = NewRect("Track", row);
            Layout(track.gameObject, flexW: 1f, height: 18f);
            Image trackBg = track.gameObject.AddComponent<Image>();
            trackBg.sprite = Rounded(9);
            trackBg.type = Image.Type.Sliced;
            trackBg.color = new Color(1f, 1f, 1f, 0.1f);
            trackBg.raycastTarget = false;

            Image fill = NewImage("Fill", track, color, 9);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Max(0.06f, fill01)), 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            TMP_Text v = NewText("Value", row, value, 26f, color, TextAlignmentOptions.Right, FontStyles.Bold);
            Layout(v.gameObject, 90f);
        }

        private static void AddRewardRow(Transform parent, Sprite icon, string label, string amount, string chance)
        {
            RectTransform row = NewRect("Reward_" + label, parent);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(14);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            Layout(row.gameObject, height: 64f);
            HStack(row.gameObject, 16f, new RectOffset(16, 24, 6, 6));

            Image iconImg = NewImage("Icon", row, icon != null ? Color.white : Accent, 8);
            iconImg.sprite = icon != null ? icon : Rounded(8);
            iconImg.type = icon != null ? Image.Type.Simple : Image.Type.Sliced;
            iconImg.preserveAspect = true;
            Layout(iconImg.gameObject, 48f, 48f);

            TMP_Text l = NewText("Label", row, label, 26f, TextMain);
            Layout(l.gameObject, flexW: 1f);
            TMP_Text a = NewText("Amount", row, amount, 26f, Accent, TextAlignmentOptions.Right, FontStyles.Bold);
            Layout(a.gameObject, 110f);
            if (chance != null)
            {
                TMP_Text c = NewText("Chance", row, chance, 24f, TextMuted, TextAlignmentOptions.Right);
                Layout(c.gameObject, 120f);
            }
        }

        #endregion
    }
}
