using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>One marker placed by the user: a bone name and its world position on the mesh.</summary>
    [Serializable]
    internal struct PlacedMarker
    {
        public string BoneName;
        public Vector3 WorldPosition;
    }

    /// <summary>
    /// Markers placed so far for the skeleton being built, keyed by bone name.
    /// Positions are stored in world space so the future skeleton builder does not depend on
    /// the mesh's transform (works the same whatever the GameObject's position/rotation/scale).
    /// </summary>
    [Serializable]
    internal sealed class MarkerSet
    {
        [SerializeField] private List<PlacedMarker> _markers = new List<PlacedMarker>();

        public int Count => _markers.Count;

        public bool TryGetPosition(string boneName, out Vector3 worldPosition)
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                if (_markers[i].BoneName == boneName)
                {
                    worldPosition = _markers[i].WorldPosition;
                    return true;
                }
            }

            worldPosition = default;
            return false;
        }

        public void Set(string boneName, Vector3 worldPosition)
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                if (_markers[i].BoneName == boneName)
                {
                    PlacedMarker updated = _markers[i];
                    updated.WorldPosition = worldPosition;
                    _markers[i] = updated;
                    return;
                }
            }

            _markers.Add(new PlacedMarker { BoneName = boneName, WorldPosition = worldPosition });
        }

        public void Remove(string boneName)
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                if (_markers[i].BoneName == boneName)
                {
                    _markers.RemoveAt(i);
                    return;
                }
            }
        }

        public void Clear()
        {
            _markers.Clear();
        }

        /// <summary>
        /// First required (non-optional) bone in <see cref="BoneTemplate"/> order with no marker yet,
        /// or null once every required bone has one. Drives the "click the next bone" auto-advance.
        /// </summary>
        public string GetNextUnplacedRequiredBone()
        {
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                if (!bone.IsOptional && !TryGetPosition(bone.Name, out _))
                {
                    return bone.Name;
                }
            }

            return null;
        }

        /// <summary>True once every required bone (optional ones excluded) has a marker.</summary>
        public bool HasAllRequiredMarkers()
        {
            return GetNextUnplacedRequiredBone() == null;
        }
    }
}
