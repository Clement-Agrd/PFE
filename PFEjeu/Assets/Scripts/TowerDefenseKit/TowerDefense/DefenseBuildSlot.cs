using System.Collections.Generic;
using Core.InteractionSystem;
using Core.InventorySystem;
using UnityEngine;
namespace Core.TowerDefense
{
    public sealed class DefenseBuildSlot : InteractableBase
    {
        [SerializeField] private DefenseConstructionUI construction;
        [SerializeField] private Transform buildAnchor;
        [SerializeField] private GameObject marker;
        private GameObject _occupant;
        private Collider[] _colliders;
        private bool[] _colliderStates;
        private bool _markerWasActive;
        internal Dictionary<ItemDefinition, int> Investment { get; } = new();
        public GameObject Occupant=>_occupant;
        public bool IsOccupied=>_occupant!=null;
        public Transform BuildAnchor=>buildAnchor!=null?buildAnchor:transform;
        public override string Prompt=>"Construire une défense";
        public override bool CanInteract(GameObject interactor)=>isActiveAndEnabled&&!IsOccupied&&construction!=null&&construction.CanBuildNow&&!Core.GameFreeze.IsPaused;
        public override void Interact(GameObject interactor){if(CanInteract(interactor))construction.Open(this);}
        public void OpenFromVillageView(){if(isActiveAndEnabled&&!IsOccupied&&construction!=null&&construction.CanBuildNow)construction.Open(this);}
        public bool BelongsTo(DefenseConstructionUI owner)=>construction==owner;
        public void Occupy(GameObject building)
        {
            _occupant=building;
            _markerWasActive = marker != null && marker.activeSelf;
            if (marker != null) marker.SetActive(false);
            _colliders = GetComponents<Collider>();
            _colliderStates = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++)
            {
                _colliderStates[i] = _colliders[i].enabled;
                _colliders[i].enabled = false;
            }
        }

        internal void Release()
        {
            _occupant = null;
            Investment.Clear();
            if (marker != null) marker.SetActive(_markerWasActive);
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = _colliderStates[i];
        }
    }
}
