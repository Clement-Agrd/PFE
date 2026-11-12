using Core.BarracksSystem;
using Core.Village;
using UnityEngine;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;

namespace Core.TowerDefense
{
    public sealed class TrainingTowerDebug : TrainingPanel
    {
        [SerializeField] private Building townHall;
        [SerializeField] private HeroRoster roster;
        private int _heroLevel = 1;

        public override string Prompt => "Ouvrir les réglages d’essai";

        protected override void BuildPanel(Transform card, GameObject actor)
        {
            Layout(NewText("Title", card, "Réglages d’essai", 34, Accent).gameObject, height: 55);
            var hallLabel = NewText("TownHall", card, HallLabel(), 24, TextMain);
            Layout(hallLabel.gameObject, height: 40);
            var upgrade = NewButton(card, "Monter l’hôtel de ville d’un niveau", () =>
            {
                if (townHall == null || townHall.IsMaxLevel) return;
                townHall.SetLevel(townHall.CurrentLevel + 1);
                hallLabel.text = HallLabel();
            });
            var upgradeButton = upgrade.GetComponent<Button>();
            upgradeButton.interactable = townHall != null && townHall.Definition != null && !townHall.IsMaxLevel;
            Layout(upgrade.gameObject, height: 55);

            var heroLabel = NewText("HeroLevel", card, $"Niveau du héros : {_heroLevel}", 24, Accent);
            Layout(heroLabel.gameObject, height: 40);
            var level = NewSlider(card, 1, 5, _heroLevel, value =>
            {
                _heroLevel = Mathf.RoundToInt(value);
                heroLabel.text = $"Niveau du héros : {_heroLevel}";
            });
            level.wholeNumbers = true;
            Layout(level.gameObject, height: 40);

            var message = NewText("Recruitment", card, "Choisis une classe à recruter.", 22, TextMain);
            Layout(message.gameObject, height: 50);
            AddClasses(card, "Caserne", message, ClasseHeros.Tank, ClasseHeros.Degats, ClasseHeros.Invocateur);
            AddClasses(card, "Tour d’archer", message, ClasseHeros.ArcLong, ClasseHeros.ArcCourt, ClasseHeros.Arbalete);
            AddClasses(card, "Tour de chaman", message, ClasseHeros.ChamanFeu, ClasseHeros.ChamanGlace, ClasseHeros.ChamanVie);
        }

        private string HallLabel() => townHall != null && townHall.Definition != null
            ? $"Hôtel de ville · Niveau {townHall.CurrentLevel} / {townHall.Definition.MaxLevel}"
            : "Hôtel de ville indisponible.";

        private void AddClasses(Transform card, string title, TMPro.TMP_Text message, params ClasseHeros[] classes)
        {
            Layout(NewText(title, card, title, 22, Accent).gameObject, height: 27);
            var row = NewRect(title + " · Classes", card);
            Layout(row.gameObject, height: 52);
            HStack(row.gameObject, 10);
            foreach (var type in classes)
            {
                var button = NewButton(row, heros.ClassName(type), () =>
                {
                    var hero = roster != null ? roster.Recruit(type, _heroLevel) : null;
                    message.text = hero != null ? $"{hero.ClasseName} niveau {hero.Level} recruté." : "Recrutement indisponible.";
                }, fontSize: 21);
                Layout(button.gameObject, flexW: 1);
                button.GetComponent<Button>().interactable = roster != null;
            }
        }
    }
}
