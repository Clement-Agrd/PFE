using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core.HealthSystem;
using Core.Village.Exploration;
using Core.WaveSystem;
using TMPro;
using UnityEngine;
using static Core.PauseMenu.PauseUiKit;

namespace Core.TowerDefense
{
    public sealed class VillageDefenseSession : MonoBehaviour
    {
        [SerializeField] private WaveSpawner spawner;
        [SerializeField] private Core.Village.VillageViewController villageView;
        [SerializeField] private Health gateHealth;
        [SerializeField] private ExplorationManager expeditions;
        [SerializeField] private Core.Village.ResourceProducer[] producers;
        [SerializeField] private Core.InventorySystem.UI.InventoryUI inventoryUI;
        [SerializeField] private Core.Village.UI.BuildingPanelUI buildingPanel;
        [SerializeField] private Core.Village.Exploration.UI.ExplorationPanelUI explorationUI;
        [SerializeField] private Core.TavernSystem.UI.TavernPanelUI tavernUI;
        [SerializeField] private Core.Village.UI.ForgePanelUI forgeUI;
        [SerializeField] private Core.BarracksSystem.BarracksConfirmationUI barracksUI;
        private GameObject _canvas, _report;
        private TMP_Text _hud, _title, _body;
        private Core.PauseMenu.PauseButton _continue, _restart;
        private bool _defending, _lost;
        private int _wave, _lastAlive = -1;
        public bool IsLost => _lost;
        public bool IsReportOpen => _report != null && _report.activeSelf;

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.OnWaveStarted += Begin;
                spawner.OnDefenseCompleted += Complete;
            }
            if (gateHealth != null)
            {
                gateHealth.OnDeath += Lose;
                gateHealth.OnHealthChanged += HealthChanged;
            }
        }
        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.OnWaveStarted -= Begin;
                spawner.OnDefenseCompleted -= Complete;
            }
            if (gateHealth != null)
            {
                gateHealth.OnDeath -= Lose;
                gateHealth.OnHealthChanged -= HealthChanged;
            }
            StopAllCoroutines();
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }
        private void Start() { BuildUI(); UpdateHud(); }
        private void OnDestroy() { if (_canvas != null) Destroy(_canvas); }
        private void HealthChanged(int current, int maximum) => UpdateHud();
        private void Update()
        {
            if (_hud != null && villageView != null) _hud.transform.parent.gameObject.SetActive(!villageView.IsInVillageView);
            if (spawner != null && _lastAlive != spawner.AliveCount) UpdateHud();
        }
        private void UpdateHud()
        {
            if (_hud == null || spawner == null || gateHealth == null) return;
            _lastAlive = spawner.AliveCount;
            _hud.text = $"Herse : {gateHealth.CurrentHealth} / {gateHealth.MaxHealth} PV" +
                (_lost ? "  ·  Défaite" : _defending ? $"  ·  Vague {_wave}  ·  {_lastAlive} ennemis" : "  ·  Village");
        }
        private void CloseOtherPanels()
        {
            if (inventoryUI != null) inventoryUI.SetOpen(false);
            if (buildingPanel != null) buildingPanel.Hide();
            if (explorationUI != null) explorationUI.Hide();
            if (tavernUI != null) tavernUI.Hide();
            if (forgeUI != null) forgeUI.Close();
            if (barracksUI != null) barracksUI.Cancel();
        }
        private void Begin(int wave)
        {
            if (_lost) return;
            CloseOtherPanels();
            CloseReport();
            _defending = true;
            _wave = wave;
            UpdateHud();
        }
        private void Complete()
        {
            if (_lost || !_defending) return;
            _defending = false;
            UpdateHud();
            // All expedition subscribers finish synchronously before this frame's report.
            StartCoroutine(ShowVictory());
        }
        private IEnumerator ShowVictory()
        {
            yield return null;
            if (_lost) yield break;
            var text = new StringBuilder();
            text.AppendLine($"Herse : {gateHealth.CurrentHealth} / {gateHealth.MaxHealth} PV\n");
            text.AppendLine("<b>Ressources récoltées : village et expéditions</b>");
            var totals = new Dictionary<Core.InventorySystem.ItemDefinition, int>();
            var results = expeditions != null ? expeditions.LastDefenseResults : System.Array.Empty<ExpeditionResult>();
            foreach (var result in results)
                foreach (var loot in result.Loot)
                {
                    int pending = result.PendingLoot.Where(p => p.item == loot.item).Sum(p => p.amount);
                    int delivered = Mathf.Max(0, loot.amount - pending);
                    if (delivered > 0) totals[loot.item] = totals.GetValueOrDefault(loot.item) + delivered;
                }
            if (producers != null)
                foreach (var producer in producers)
                    if (producer != null)
                        foreach (var loot in producer.LastDefenseProduction)
                            totals[loot.item] = totals.GetValueOrDefault(loot.item) + loot.amount;
            if (totals.Count == 0) text.AppendLine("Aucune ressource rapportée.");
            foreach (var pair in totals) text.AppendLine($"+{pair.Value} {pair.Key.DisplayName}");
            text.AppendLine("\n<b>Production du village</b>");
            if (producers != null)
                foreach (var producer in producers)
                    if (producer != null)
                    {
                        text.AppendLine(producer.name + " : " + (producer.LastDefenseProduction.Count == 0 ? "Aucune livraison" :
                            string.Join(", ", producer.LastDefenseProduction.Select(p => $"+{p.amount} {p.item.DisplayName}"))));
                        if (producer.PendingProduction.Count > 0) text.AppendLine("Stockage plein : production conservée en attente.");
                    }
            text.AppendLine("\n<b>Rapport des héros</b>");
            if (results.Count == 0) text.AppendLine("Aucune expédition terminée cette défense.");
            foreach (var result in results)
            {
                text.AppendLine($"{result.HeroName} · {result.Mission.DisplayName}\n{result.Status}");
                if (result.PendingLoot.Count > 0) text.AppendLine("Stockage plein : butin conservé en attente.");
                text.AppendLine();
            }
            text.AppendLine("La herse est ouverte. Prépare ton village puis retourne voir le capitaine pour lancer la prochaine vague.");
            ShowReport($"Vague {_wave} remportée", text.ToString(), false);
        }
        private void Lose()
        {
            if (_lost) return;
            _lost = true;
            _defending = false;
            StopAllCoroutines();
            if (spawner != null) spawner.FailDefense();
            CloseOtherPanels();
            UpdateHud();
            ShowReport("Défaite", "La herse a été détruite.\nLe village est tombé.\n\nLes expéditions de cette défense ne rapportent aucune récompense.", true);
        }
        private void ShowReport(string title, string body, bool lost)
        {
            if (_canvas == null) BuildUI();
            _title.text = title;
            _body.text = body;
            _continue.gameObject.SetActive(!lost);
            _restart.gameObject.SetActive(lost);
            _report.SetActive(true);
            _body.GetComponentInParent<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = 1;
            GameFreeze.RequestPause(this);
            GameFreeze.RequestCursorUnlock(this);
        }
        public void CloseReport()
        {
            if (_lost) return;
            if (_report != null) _report.SetActive(false);
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }
        private void Restart()
        {
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(gameObject.scene.path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(gameObject.scene.path);
#endif
        }
        private void BuildUI()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("VillageDefenseCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.GetComponent<Canvas>().sortingOrder = 650;
            var scaler = _canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            scaler.matchWidthOrHeight = .5f;
            var hud = NewImage("GateStatus", _canvas.transform, Card, 14);
            Anchor(hud.rectTransform, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-16), new Vector2(850,58));
            _hud = NewText("Status", hud.transform, "", 25, TextMain, TextAlignmentOptions.Center);
            Stretch(_hud.rectTransform, 6);
            var dim = NewImage("DefenseReport", _canvas.transform, Dim);
            Stretch(dim.rectTransform); dim.raycastTarget = true; _report = dim.gameObject;
            var card = NewImage("Card", dim.transform, Card, 20);
            Anchor(card.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(1240,900));
            _title = NewText("Title",card.transform,"",36,Accent,TextAlignmentOptions.Center,FontStyles.Bold);
            Anchor(_title.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-26),new Vector2(1120,64));
            var scroll = NewRect("ReportScroll",card.transform); Stretch(scroll,40);scroll.offsetMin=new Vector2(40,130);scroll.offsetMax=new Vector2(-40,-110);
            var view = NewImage("Viewport",scroll,new Color(1,1,1,.01f));Stretch(view.rectTransform);view.raycastTarget=true;view.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            _body=NewText("ReportText",view.transform,"",25,TextMain,TextAlignmentOptions.TopLeft);
            var content=_body.rectTransform;content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var sr=scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();sr.viewport=view.rectTransform;sr.content=content;sr.horizontal=false;sr.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;sr.scrollSensitivity=40;
            _continue=NewButton(card.transform,"Retour au village",CloseReport,ButtonStyle.Primary,28);
            Anchor((RectTransform)_continue.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,32),new Vector2(500,64));
            _restart=NewButton(card.transform,"Recommencer",Restart,ButtonStyle.Primary,28);
            Anchor((RectTransform)_restart.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,32),new Vector2(500,64));
            _report.SetActive(false);
        }
    }
}
