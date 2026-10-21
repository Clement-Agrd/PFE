using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Outcome of <see cref="ClipHumanoidFixer.TryFixToHumanoid"/>, shown back to the user so an
    /// automated import-settings change is never silent.
    /// </summary>
    internal enum HumanoidFixResult
    {
        AlreadyHumanoid,
        FixedSuccessfully,
        NoSourceModelImporter,
        SourceModelNotHumanoidCompatible,
    }

    /// <summary>
    /// Attempts to switch a clip's source model (FBX) from "Generic" to "Humanoid" animation import,
    /// so it retargets onto ANY Humanoid Avatar via muscle data instead of needing bone names/paths
    /// that match the target skeleton exactly — which an auto-rigged skeleton almost never does.
    ///
    /// This edits the asset's IMPORT SETTINGS (the "Rig" tab in the Inspector). Unity's Undo system
    /// does NOT cover import settings, so a change made here cannot be undone with Ctrl+Z — only by
    /// flipping the setting back by hand, or automatically by this class's own rollback on failure.
    /// </summary>
    internal static class ClipHumanoidFixer
    {
        /// <summary>
        /// Tries to make <paramref name="clip"/> Humanoid. Leaves the asset untouched and reports why
        /// when it can't: either there is no model import settings to change (a native .anim clip
        /// authored directly in Unity), or Unity could not recognize the source hierarchy as a biped,
        /// in which case any partial change is rolled back automatically.
        /// </summary>
        public static HumanoidFixResult TryFixToHumanoid(AnimationClip clip)
        {
            if (clip == null)
            {
                return HumanoidFixResult.NoSourceModelImporter;
            }

            if (clip.isHumanMotion)
            {
                return HumanoidFixResult.AlreadyHumanoid;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath))
            {
                return HumanoidFixResult.NoSourceModelImporter;
            }

            // Only a model (FBX, etc.) imported through ModelImporter exposes an "Animation Type"
            // setting. A native .anim clip created directly in Unity has none to flip.
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter importer))
            {
                return HumanoidFixResult.NoSourceModelImporter;
            }

            ModelImporterAnimationType previousType = importer.animationType;
            ModelImporterAvatarSetup previousAvatarSetup = importer.avatarSetup;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            if (IsResultingAvatarValid(assetPath))
            {
                return HumanoidFixResult.FixedSuccessfully;
            }

            // The source hierarchy isn't a biped Unity can recognize: roll back rather than leaving
            // the asset re-imported as a broken Humanoid rig.
            importer.animationType = previousType;
            importer.avatarSetup = previousAvatarSetup;
            importer.SaveAndReimport();

            return HumanoidFixResult.SourceModelNotHumanoidCompatible;
        }

        private static bool IsResultingAvatarValid(string assetPath)
        {
            var prefab = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            if (prefab == null)
            {
                return false;
            }

            Animator animator = prefab.GetComponent<Animator>();
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        /// <summary>Human-readable explanation per outcome, for display in the preview window.</summary>
        public static string Describe(HumanoidFixResult result)
        {
            switch (result)
            {
                case HumanoidFixResult.AlreadyHumanoid:
                    return "Ce clip est déjà marqué Humanoid.";
                case HumanoidFixResult.FixedSuccessfully:
                    return "Corrigé : le clip source est maintenant importé en Humanoid avec un Avatar valide.";
                case HumanoidFixResult.NoSourceModelImporter:
                    return "Correction automatique impossible : ce clip ne vient pas d'un fichier modèle (FBX) importé. Change son réglage manuellement si besoin.";
                case HumanoidFixResult.SourceModelNotHumanoidCompatible:
                    return "Échec : Unity n'a pas pu construire un Avatar Humanoid valide depuis ce fichier (hiérarchie non reconnue comme un biped). Réglage remis à son état d'origine.";
                default:
                    return string.Empty;
            }
        }
    }
}
