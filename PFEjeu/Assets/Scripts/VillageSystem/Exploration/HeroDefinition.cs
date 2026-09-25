using UnityEngine;

namespace Core.Village.Exploration
{
    /// <summary>
    /// Identité d'un héros assignable à une expédition. Pas de gameplay de héros
    /// ici (stats, combat...) — juste de quoi peupler le roster du poste
    /// d'expédition en attendant le vrai système de héros.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHero", menuName = "Village/Exploration/Hero")]
    public sealed class HeroDefinition : ScriptableObject
    {
        [field: SerializeField] public string DisplayName { get; private set; } = "Héros";
        [field: SerializeField] public HeroClass Class { get; private set; }
        [field: SerializeField] public Sprite Icon { get; private set; }
    }
}
