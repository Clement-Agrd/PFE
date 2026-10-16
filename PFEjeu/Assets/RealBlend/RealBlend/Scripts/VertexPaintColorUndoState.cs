using System.Collections.Generic;
using UnityEngine;

namespace RealBlend
{
    /// <summary>
    /// Lightweight per-target Undo payload for vertex painting. It stores only
    /// colors, avoiding complete Mesh and topology-heavy storage snapshots.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class VertexPaintColorUndoState : MonoBehaviour
    {
        private static readonly HashSet<VertexPaintColorUndoState> ActiveStateSet =
            new HashSet<VertexPaintColorUndoState>();

        [SerializeField, HideInInspector] private Color[] colors;
        [SerializeField, HideInInspector] private int revision;
        [System.NonSerialized] private bool hasPendingRevision;

        public Color[] Colors => colors;
        public int Revision => revision;
        public static IEnumerable<VertexPaintColorUndoState> ActiveStates =>
            ActiveStateSet;

        public void Initialize(Color[] currentColors, int currentRevision)
        {
            colors = currentColors != null
                ? (Color[])currentColors.Clone()
                : null;
            revision = currentRevision;
            hasPendingRevision = false;
        }

        /// <summary>
        /// Points the carrier at the live stroke buffer without copying it on
        /// every drag event. Unity's registered pre-stroke snapshot remains a
        /// deep serialized copy, while Redo can capture the latest live values.
        /// </summary>
        public void SetPending(Color[] liveColors)
        {
            colors = liveColors;
            if (!hasPendingRevision)
            {
                revision++;
                hasPendingRevision = true;
            }
        }

        public void Commit(Color[] finalColors)
        {
            colors = finalColors != null
                ? (Color[])finalColors.Clone()
                : null;
            if (!hasPendingRevision)
                revision++;
            hasPendingRevision = false;
        }

        private void OnEnable()
        {
            hideFlags |= HideFlags.HideInInspector | HideFlags.DontSaveInBuild;
            ActiveStateSet.Add(this);
        }

        private void OnDisable()
        {
            ActiveStateSet.Remove(this);
        }
    }
}
