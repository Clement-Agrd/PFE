using UnityEngine;
using TMPro;

namespace Core.Village.UI
{
    /// <summary>
    /// Une ligne du bilan d'expéditions (onglet "Village" de l'inventaire) :
    /// "Héros (Classe) → Mission" à gauche, état à droite. Purement informatif.
    /// </summary>
    public sealed class ExpeditionStatusRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text statusLabel;

        public void Set(string title, string status, Color statusColor)
        {
            if (titleLabel != null) titleLabel.text = title;
            if (statusLabel != null)
            {
                statusLabel.text = status;
                statusLabel.color = statusColor;
            }
        }
    }
}
