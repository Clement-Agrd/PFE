using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.InventorySystem;

namespace Core.VillageEvents
{
    /// <summary>
    /// Panneau de debug IMGUI (F9) pour tester les événements de village sans jouer une nuit entière.
    /// À retirer ou désactiver pour une build finale.
    /// </summary>
    [RequireComponent(typeof(VillageEventManager))]
    public sealed class VillageEventDebugPanel : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.F9;
        [SerializeField, Min(1)] private int stockStep = 100;

        private VillageEventManager _mgr;
        private bool _open;
        private Vector2 _scroll;
        private bool _cursorTaken;

        private void Awake() => _mgr = GetComponent<VillageEventManager>();

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
                SetOpen(!_open);
        }

        private void OnDisable()
        {
            if (_open) SetOpen(false);
        }

        private void SetOpen(bool open)
        {
            _open = open;
            if (open && !_cursorTaken) { GameFreeze.RequestCursorUnlock(this); _cursorTaken = true; }
            else if (!open && _cursorTaken) { GameFreeze.ReleaseCursorUnlock(this); _cursorTaken = false; }
        }

        private void OnGUI()
        {
            if (!_open || _mgr == null) return;
            GUI.skin.label.richText = true;

            GUILayout.BeginArea(new Rect(20, 20, 420, Screen.height - 40), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>VILLAGE EVENTS — DEBUG (F9)</b>");
            VillageState s = _mgr.State;
            if (s != null)
            {
                GUILayout.Label($"Nuit rituel {s.RitualNight}/{s.RitualTotalNights} | Corruption {s.CorruptionTier}/{s.MaxCorruptionTier}");
                GUILayout.Label($"Ennemis : actif +{s.ActiveEnemyBonusPercent}% | prochaine nuit +{s.PendingEnemyBonusPercent}% | x{_mgr.EscalationMultiplier:F2}");
            }
            if (_mgr.Base != null)
                GUILayout.Label($"Remparts : {_mgr.Base.Health}/{_mgr.Base.MaxHealth}");

            GUILayout.Space(6);
            GUILayout.Label("<b>Forcer un événement</b>");
            foreach (VillageEventDefinition e in _mgr.Catalog)
                if (e != null && GUILayout.Button(e.Title)) { SetOpen(false); _mgr.DebugForce(e); }

            GUILayout.Space(6);
            GUILayout.Label("<b>Simulation</b>");
            if (GUILayout.Button("Simuler fin de nuit (tirage normal)")) { SetOpen(false); _mgr.DebugSimulateNightEnd(); }
            if (GUILayout.Button("Réinitialiser cooldowns / suites")) _mgr.DebugResetHistory();
            GUILayout.TextArea(_mgr.DebugEligibilityReport());

            GUILayout.Space(6);
            GUILayout.Label("<b>État du village</b>");
            if (s != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Rituel -1")) s.AdvanceRitual(-1);
                if (GUILayout.Button("Rituel +1")) s.AdvanceRitual(1);
                if (GUILayout.Button("Corruption -1")) s.AddCorruption(-1);
                if (GUILayout.Button("Corruption +1")) s.AddCorruption(1);
                GUILayout.EndHorizontal();
            }
            if (_mgr.Base != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Remparts -50%")) _mgr.Base.Repair(-_mgr.Base.MaxHealth / 2);
                if (GUILayout.Button("Remparts 100%")) _mgr.Base.ResetBase();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Stock HDV</b>");
            Inventory inv = _mgr.Village != null ? _mgr.Village.HdvInventory : null;
            if (inv == null) GUILayout.Label("(pas de stockage HDV)");
            else
            {
                foreach (ItemDefinition item in StockItems())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{item.DisplayName}: {inv.Count(item)}", GUILayout.Width(180));
                    if (GUILayout.Button($"+{stockStep}")) inv.Add(item, stockStep);
                    if (GUILayout.Button($"-{stockStep}")) inv.Remove(item, stockStep);
                    if (GUILayout.Button("0")) inv.Remove(item, inv.Count(item));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // Les ressources touchées par le catalogue (dédoublonnées).
        private IEnumerable<ItemDefinition> StockItems()
        {
            var seen = new HashSet<ItemDefinition>();
            foreach (VillageEventDefinition e in _mgr.Catalog)
            {
                if (e == null) continue;
                foreach (EventChoice c in e.Choices)
                {
                    if (c.effects == null) continue;
                    foreach (EventEffect fx in c.effects)
                        if (fx.type == EffectType.Resource && fx.item != null && seen.Add(fx.item))
                            yield return fx.item;
                }
                if (e.Conditions.stockItem != null && seen.Add(e.Conditions.stockItem))
                    yield return e.Conditions.stockItem;
            }
        }
    }
}
