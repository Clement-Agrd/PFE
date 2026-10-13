using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;

namespace Core.PauseMenu
{
    /// <summary>
    /// Panneau d'options : onglets Vidéo / Audio / Jeu / Commandes. Chaque changement est appliqué
    /// immédiatement et sauvegardé (GameSettings). Construit entièrement en code.
    /// </summary>
    public sealed class PauseSettingsView
    {
        private readonly List<RectTransform> _pages = new();
        private readonly List<PauseButton> _tabs = new();
        private readonly List<Action> _refreshers = new();

        public RectTransform Root { get; private set; }
        public CanvasGroup Group { get; private set; }

        public void Build(Transform parent, Action onBack, InputActionAsset controls)
        {
            Root = NewRect("OptionsView", parent);
            Stretch(Root);
            Group = Root.gameObject.AddComponent<CanvasGroup>();

            RectTransform card = NewRect("Card", Root);
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1280f, 780f));
            var cardBg = card.gameObject.AddComponent<Image>();
            cardBg.sprite = Rounded(28);
            cardBg.type = Image.Type.Sliced;
            cardBg.color = Card;
            card.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            VStack(card.gameObject, 22f, new RectOffset(48, 48, 40, 36));

            // En-tête
            RectTransform header = NewRect("Header", card);
            Layout(header.gameObject, height: 64f);
            HStack(header.gameObject, 16f);
            TMP_Text title = NewText("Title", header, "OPTIONS", 52f, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            title.characterSpacing = 8f;
            Layout(title.gameObject, flexW: 1f);

            Image accent = NewImage("AccentLine", card, Accent, 2);
            Layout(accent.gameObject, height: 4f);

            // Onglets
            RectTransform tabRow = NewRect("Tabs", card);
            Layout(tabRow.gameObject, height: 64f);
            HStack(tabRow.gameObject, 12f);

            RectTransform pages = NewRect("Pages", card);
            Layout(pages.gameObject, flexH: 1f, flexW: 1f);

            AddPage(tabRow, pages, "Vidéo", BuildVideo);
            AddPage(tabRow, pages, "Audio", BuildAudio);
            AddPage(tabRow, pages, "Jeu", BuildGame);
            AddPage(tabRow, pages, "Commandes", content => BuildControls(content, controls));

            // Pied de page
            RectTransform footer = NewRect("Footer", card);
            Layout(footer.gameObject, height: 64f);
            HStack(footer.gameObject, 16f, align: TextAnchor.MiddleRight);

            PauseButton reset = NewButton(footer, "Réinitialiser", () => { GameSettings.ResetToDefaults(); RefreshAll(); },
                ButtonStyle.Secondary, 26f);
            reset.Label.alignment = TextAlignmentOptions.Center;
            Layout(reset.gameObject, 280f, 64f);
            PauseButton back = NewButton(footer, "Retour", onBack, ButtonStyle.Primary, 28f);
            back.Label.alignment = TextAlignmentOptions.Center;
            Layout(back.gameObject, 240f, 64f);

            ShowPage(0);
        }

        private void AddPage(Transform tabRow, Transform pages, string name, Action<RectTransform> build)
        {
            int index = _pages.Count;
            PauseButton tab = NewButton(tabRow, name, () => ShowPage(index), ButtonStyle.Tab, 26f);
            Layout(tab.gameObject, 210f, 64f);
            _tabs.Add(tab);

            RectTransform content = NewScroll(pages, "Page_" + name, 12f, new RectOffset(0, 12, 8, 8), out _);
            Stretch((RectTransform)content.parent.parent);
            build(content);
            _pages.Add((RectTransform)content.parent.parent);
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].gameObject.SetActive(i == index);
                _tabs[i].SetActive(i == index);
            }
        }

        public void OnShown()
        {
            RefreshAll();
            ShowPage(0);
        }

        private void RefreshAll()
        {
            foreach (Action r in _refreshers) r();
        }

        #region Lignes

        private static RectTransform NewRow(Transform parent, string label, string hint = null)
        {
            RectTransform row = NewRect("Row_" + label, parent);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(16);
            bg.type = Image.Type.Sliced;
            bg.color = CardRaised;
            Layout(row.gameObject, height: 78f);
            HStack(row.gameObject, 20f, new RectOffset(28, 28, 8, 8));

            RectTransform labels = NewRect("Labels", row);
            VStack(labels.gameObject, 0f, align: TextAnchor.MiddleLeft);
            Layout(labels.gameObject, flexW: 1f);
            TMP_Text l = NewText("Label", labels, label, 28f, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            Layout(l.gameObject, height: 34f);
            if (hint != null)
            {
                TMP_Text h = NewText("Hint", labels, hint, 20f, TextMuted);
                Layout(h.gameObject, height: 24f);
            }
            return row;
        }

        private RectTransform AddToggleRow(Transform page, string label, string hint, Func<bool> get, Action<bool> set)
        {
            RectTransform row = NewRow(page, label, hint);
            PauseToggle toggle = NewToggle(row, get(), v => { set(v); GameSettings.Save(); });
            _refreshers.Add(() => toggle.Set(get(), false));
            return row;
        }

        private void AddStepperRow(Transform page, string label, string hint, Func<string> text, Action<int> step)
        {
            RectTransform row = NewRow(page, label, hint);

            PauseButton prev = null, next = null;
            TMP_Text value = null;

            void Refresh() => value.text = text();

            prev = NewButton(row, "‹", () => { step(-1); GameSettings.Save(); Refresh(); }, ButtonStyle.Tab, 36f);
            Layout(prev.gameObject, 56f, 56f);
            value = NewText("Value", row, text(), 26f, Accent, TextAlignmentOptions.Center, FontStyles.Bold);
            Layout(value.gameObject, 250f, 56f);
            next = NewButton(row, "›", () => { step(1); GameSettings.Save(); Refresh(); }, ButtonStyle.Tab, 36f);
            Layout(next.gameObject, 56f, 56f);
            _refreshers.Add(Refresh);
        }

        private void AddSliderRow(Transform page, string label, string hint, float min, float max,
            Func<float> get, Action<float> set, Func<float, string> format)
        {
            RectTransform row = NewRow(page, label, hint);
            TMP_Text value = NewText("Value", row, format(get()), 26f, Accent, TextAlignmentOptions.Right, FontStyles.Bold);
            Layout(value.gameObject, 110f, 56f);
            Slider slider = NewSlider(row, min, max, get(), v => { set(v); value.text = format(v); });
            slider.onValueChanged.AddListener(_ => GameSettings.Save());
            _refreshers.Add(() => { slider.SetValueWithoutNotify(get()); value.text = format(get()); });
        }

        #endregion

        #region Pages

        private void BuildVideo(RectTransform page)
        {
            AddStepperRow(page, "Résolution", "Taille de la fenêtre / de l'écran",
                () => { Resolution r = GameSettings.Resolutions[GameSettings.ResolutionIndex]; return $"{r.width} × {r.height}"; },
                d =>
                {
                    int n = GameSettings.Resolutions.Length;
                    GameSettings.ResolutionIndex = (GameSettings.ResolutionIndex + d + n) % n;
                });

            AddToggleRow(page, "Plein écran", "Affichage sur tout l'écran", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);

            AddStepperRow(page, "Qualité graphique", "Ombres, textures, effets",
                () => { string[] n = QualitySettings.names; return n.Length > 0 ? n[Mathf.Clamp(GameSettings.Quality, 0, n.Length - 1)] : "-"; },
                d =>
                {
                    int n = QualitySettings.names.Length;
                    if (n > 0) GameSettings.Quality = (GameSettings.Quality + d + n) % n;
                });

            AddToggleRow(page, "Synchronisation verticale", "Évite le déchirement de l'image (limite les FPS)",
                () => GameSettings.VSync, v => GameSettings.VSync = v);

            AddStepperRow(page, "Limite d'images par seconde", "Sans effet quand la synchronisation verticale est active",
                () => GameSettings.FpsCap <= 0 ? "Illimitée" : $"{GameSettings.FpsCap} FPS",
                d =>
                {
                    int[] caps = GameSettings.FpsCaps;
                    int i = Mathf.Max(0, Array.IndexOf(caps, GameSettings.FpsCap));
                    GameSettings.FpsCap = caps[(i + d + caps.Length) % caps.Length];
                });

            AddToggleRow(page, "Afficher les FPS", "Compteur d'images par seconde en haut à gauche",
                () => GameSettings.ShowFps, v => GameSettings.ShowFps = v);
        }

        private void BuildAudio(RectTransform page)
        {
            AddSliderRow(page, "Volume général", "Musique, ambiance et bruitages",
                0f, 1f, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v,
                v => Mathf.RoundToInt(v * 100f) + " %");
        }

        private void BuildGame(RectTransform page)
        {
            AddSliderRow(page, "Sensibilité de la souris", "Vitesse de rotation de la caméra",
                0.1f, 3f, () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v,
                v => $"× {v:0.0#}");

            AddToggleRow(page, "Inverser l'axe vertical", "Haut / bas de la caméra inversés",
                () => GameSettings.InvertY, v => GameSettings.InvertY = v);
        }

        private static readonly Dictionary<string, string> ActionNames = new()
        {
            ["Move"] = "Se déplacer", ["Look"] = "Regarder", ["Jump"] = "Sauter", ["Roll"] = "Rouler",
            ["Attack"] = "Attaquer", ["Aim"] = "Viser", ["Sprint"] = "Sprinter",
            ["SelectMelee"] = "Arme de mêlée", ["SelectBow"] = "Arc", ["SelectMagic"] = "Magie",
            ["SelectAxe"] = "Hache", ["SelectPickaxe"] = "Pioche",
            ["ViewVillage"] = "Vue du village", ["ExitVillageView"] = "Quitter la vue du village",
            ["Interact"] = "Interagir"
        };

        private static string KeyLabel(string s) => s switch
        {
            "Delta" => "Souris",
            "LMB" => "Clic gauche",
            "RMB" => "Clic droit",
            "MMB" => "Clic molette",
            _ => s
        };

        private void BuildControls(RectTransform page, InputActionAsset controls)
        {
            AddSection(page, "Personnage");
            if (controls != null)
            {
                foreach (InputAction action in controls)
                {
                    var keys = new List<string>();
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        if (action.bindings[i].isPartOfComposite) continue;
                        string s = KeyLabel(action.GetBindingDisplayString(i));
                        if (!string.IsNullOrWhiteSpace(s) && !keys.Contains(s)) keys.Add(s);
                    }
                    if (keys.Count == 0) continue;
                    string name = ActionNames.TryGetValue(action.name, out string fr) ? fr : action.name;
                    AddControlRow(page, name, keys);
                }
            }

            AddSection(page, "Interface");
            AddControlRow(page, "Inventaire", new List<string> { "Tab" });
            AddControlRow(page, "Fermer une fenêtre", new List<string> { "E", "Échap" });
            AddControlRow(page, "Menu pause", new List<string> { "Échap" });
            AddControlRow(page, "Changer de case de la barre rapide", new List<string> { "Molette", "1 à 9" });
            AddControlRow(page, "Utiliser l'objet sélectionné", new List<string> { "F" });
            AddControlRow(page, "Roue des pièges", new List<string> { "Clic molette" });
            AddControlRow(page, "Consommer depuis l'inventaire", new List<string> { "Clic droit" });
        }

        private static void AddSection(Transform page, string title)
        {
            TMP_Text t = NewText("Section_" + title, page, title.ToUpperInvariant(), 22f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            t.characterSpacing = 6f;
            Layout(t.gameObject, height: 44f);
        }

        private static void AddControlRow(Transform page, string label, List<string> keys)
        {
            RectTransform row = NewRect("Control_" + label, page);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(14);
            bg.type = Image.Type.Sliced;
            bg.color = CardRaised;
            Layout(row.gameObject, height: 64f);
            HStack(row.gameObject, 10f, new RectOffset(28, 24, 8, 8));

            TMP_Text l = NewText("Label", row, label, 26f, TextMain);
            Layout(l.gameObject, flexW: 1f);

            foreach (string key in keys)
            {
                Image chip = NewImage("Key", row, new Color(1f, 1f, 1f, 0.09f), 10);
                HStack(chip.gameObject, 0f, new RectOffset(18, 18, 4, 4), TextAnchor.MiddleCenter);
                TMP_Text k = NewText("Text", chip.transform, key, 24f, Accent, TextAlignmentOptions.Center, FontStyles.Bold);
                k.textWrappingMode = TextWrappingModes.NoWrap;
                Layout(k.gameObject, height: 44f);
                Layout(chip.gameObject, height: 44f);
            }
        }

        #endregion
    }
}

