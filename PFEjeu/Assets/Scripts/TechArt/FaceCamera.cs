using UnityEngine;

namespace Core.TechArt
{
    /// <summary>
    /// Oriente un texte/panneau monde vers la caméra principale (rotation sur Y
    /// uniquement) pour qu'il reste lisible quel que soit l'angle d'approche.
    /// </summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        private Camera _cam;

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 away = transform.position - _cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return;

            // Un TextMeshPro est lisible quand sa face avant regarde -Z : on le fait
            // donc pointer dans la direction opposée à la caméra.
            transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
