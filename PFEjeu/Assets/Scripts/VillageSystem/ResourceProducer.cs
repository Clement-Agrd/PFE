using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.InventorySystem;
using Core.WaveSystem;

namespace Core.Village
{
    [RequireComponent(typeof(Building))]
    public sealed class ResourceProducer : MonoBehaviour
    {
        [SerializeField] private InventoryHolder targetStorage;
        [Tooltip("Si assigné, produit une fois par défense gagnée au lieu d'une production chronométrée.")]
        [SerializeField] private WaveSpawner defenseSpawner;
        private Building _building;
        private readonly Dictionary<int,float> _timers = new();
        private readonly Dictionary<ItemDefinition,int> _pending = new();
        private readonly List<(ItemDefinition item,int amount)> _lastDefense = new();
        public bool ProducesPerDefense => defenseSpawner != null;
        public IReadOnlyList<(ItemDefinition item,int amount)> LastDefenseProduction => _lastDefense;
        public IReadOnlyDictionary<ItemDefinition,int> PendingProduction => _pending;
        public event System.Action<ItemDefinition,int> OnProduced;
        private void Awake() => _building = GetComponent<Building>();
        private void OnEnable()
        {
            if (_building == null) _building = GetComponent<Building>();
            _timers.Clear();
            _building.OnLevelChanged += ResetTimers;
            if (defenseSpawner != null) defenseSpawner.OnDefenseCompleted += ProduceDefense;
        }
        private void OnDisable()
        {
            if (_building != null) _building.OnLevelChanged -= ResetTimers;
            if (defenseSpawner != null) defenseSpawner.OnDefenseCompleted -= ProduceDefense;
        }
        private void Update()
        {
            if (ProducesPerDefense || !_building.IsAvailable) return;
            var data = _building.Definition?.GetLevelData(_building.CurrentLevel);
            if (data?.productions == null) return;
            for(int i=0;i<data.productions.Length;i++)
            {
                var p=data.productions[i];
                if(p.item==null||p.amount<=0||p.interval<=0)continue;
                float timer=_timers.GetValueOrDefault(i)+Time.deltaTime;
                if(timer>=p.interval){timer-=p.interval;Queue(p.item,p.amount);Deliver(false);}
                _timers[i]=timer;
            }
        }
        private void ResetTimers(int _) => _timers.Clear();
        private void Queue(ItemDefinition item,int amount) => _pending[item]=_pending.GetValueOrDefault(item)+amount;
        private void ProduceDefense()
        {
            _lastDefense.Clear();
            if(_building.IsAvailable)
            {
                var data=_building.Definition?.GetLevelData(_building.CurrentLevel);
                if(data?.productions!=null)
                    foreach(var p in data.productions)
                        if(p.item!=null&&p.amount>0)Queue(p.item,p.amount);
            }
            Deliver(true);
        }
        private void Deliver(bool record)
        {
            if(targetStorage==null||targetStorage.Inventory==null)return;
            foreach(var pair in _pending.ToArray())
            {
                int left=targetStorage.Inventory.Add(pair.Key,pair.Value);
                int delivered=pair.Value-left;
                if(left>0)_pending[pair.Key]=left;else _pending.Remove(pair.Key);
                if(delivered<=0)continue;
                if(record)_lastDefense.Add((pair.Key,delivered));
                OnProduced?.Invoke(pair.Key,delivered);
            }
        }
    }
}
