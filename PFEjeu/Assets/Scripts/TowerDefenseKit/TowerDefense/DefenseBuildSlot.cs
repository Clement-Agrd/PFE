using Core.InteractionSystem;
using UnityEngine;
namespace Core.TowerDefense
{
    public sealed class DefenseBuildSlot : InteractableBase
    {
        [SerializeField] private DefenseConstructionUI construction;
        [SerializeField] private Transform buildAnchor;
        [SerializeField] private GameObject marker;
        private GameObject _occupant;
        public GameObject Occupant=>_occupant;
        public bool IsOccupied=>_occupant!=null;
        public Transform BuildAnchor=>buildAnchor!=null?buildAnchor:transform;
        public override string Prompt=>"Construire une défense";
        public override bool CanInteract(GameObject interactor)=>isActiveAndEnabled&&!IsOccupied&&construction!=null&&construction.CanBuildNow&&!Core.GameFreeze.IsPaused;
        public override void Interact(GameObject interactor){if(CanInteract(interactor))construction.Open(this);}
        public bool BelongsTo(DefenseConstructionUI owner)=>construction==owner;
        public void Occupy(GameObject building)
        {
            _occupant=building;
            if(marker!=null)marker.SetActive(false);
            foreach(var c in GetComponents<Collider>())c.enabled=false;
        }
    }
}
