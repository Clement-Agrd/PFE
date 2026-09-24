using System;
using Core.HealthSystem;
using Core.PoolingSystem;
using Core.StatsSystem;
using StatType = Core.StatsSystem.EnumStats.StatTypes;
using UnityEngine;

/// <summary>
/// Une tour de défense : détecte les ennemis dans sa portée (via une SOTower)
/// et tire des projectiles à intervalle régulier. Gère aussi ses niveaux
/// (modèle 3D différent selon le niveau, avec un point de tir "FirePoint"
/// retrouvé dynamiquement sur le modèle instancié).
/// </summary>
public class Tower : MonoBehaviour
{
    [Header("Data")]
    // La fiche de données de cette tour (portée, cadence de tir, dégâts, modèles par niveau...).
    // Voir SOTower.cs pour le détail des champs.
    [SerializeField] private SOTower towerData;

    [Header("Level")]
    // Le niveau actuel de la tour. Initialisé à towerData.MinLevel au démarrage.
    [SerializeField] private int currentLevel;

    [Header("Model")]
    // Le parent sous lequel le modèle 3D du niveau actuel est instancié.
    [SerializeField] private Transform modelParent;
    // Le modèle actuellement affiché (détruit et remplacé à chaque changement de niveau).
    private GameObject currentModel;

    [Header("Projectile")]
    // Le point d'origine des tirs. Retrouvé automatiquement dans le modèle du
    // niveau courant (doit être un enfant nommé exactement "FirePoint").
    [SerializeField] private Transform firePoint;

    // Le gestionnaire de pooling. INDISPENSABLE pour que les projectiles soient
    // recyclés au lieu d'être détruits/recréés à chaque tir (perf). Si ce champ
    // est vide, la tour utilise Instantiate() en secours (fonctionne, mais sans
    // le bénéfice du pooling).
    [SerializeField] private PoolManager poolManager;

    // Temps écoulé depuis le dernier tir, comparé à la cadence de tir (FireRate).
    private float shootTimer;

    // Minuteur interne : la détection ne tourne pas à chaque frame mais toutes
    // les 0.1s, pour économiser les appels Physics.OverlapSphere (coûteux).
    private float timer;

    // Les layers physiques considérés comme des ennemis détectables.
    [SerializeField] private LayerMask enemyLayer;

    // L'ennemi actuellement visé (le plus proche trouvé lors de la dernière détection).
    private Collider currentTarget;
    
    private EntityStats stats;

    //--------Détection--------//

    private void Update()
    {
        // On ne cherche une cible que toutes les 0.1s (pas chaque frame) :
        // largement suffisant pour du gameplay, et bien moins coûteux en performance.
        timer += Time.deltaTime;
        if (timer < 0.1f) return;
        timer = 0f;

        // Cherche tous les colliders ennemis dans le rayon de détection de la tour.
        Collider[] targets = Physics.OverlapSphere(
            transform.position,
            towerData.DetectionRange,
            enemyLayer);

        // Cible simple : le premier trouvé. (Pour cibler le plus proche/le plus
        // avancé sur le chemin, il faudrait trier "targets" ici.)
        currentTarget = targets.Length > 0 ? targets[0] : null;

        if (currentTarget == null) return;

        // Le compteur de cadence avance par pas de 0.1s (le rythme de ce bloc),
        // pas par Time.deltaTime : cohérent avec la fréquence de détection ci-dessus.
        shootTimer += 0.1f;

        // FireRate = tirs par seconde → 1/FireRate = secondes entre deux tirs.
        if (shootTimer >= 1f / towerData.FireRate)
        {
            Shoot();
            shootTimer = 0f;
        }
    }

    // Dessine la portée de détection dans l'éditeur (visible quand la tour est sélectionnée).
    private void OnDrawGizmosSelected()
    {
        if (towerData == null) return;
        Gizmos.DrawWireSphere(transform.position, towerData.DetectionRange);
    }

    /// <summary>
    /// Fait apparaître un projectile (via le pool si possible) et lui donne
    /// sa cible + son type de dégâts. Toute la logique de vol/impact vit
    /// ensuite dans TowerProjectil, pas ici.
    /// </summary>
    private void Shoot()
    {
        if (currentTarget == null) return;
        if (towerData.ProjectilePrefab == null) return;
        if (firePoint == null) return; // le modèle du niveau actuel n'a pas de FirePoint valide

        // Passe par le PoolManager si assigné (recyclage), sinon Instantiate
        // classique en secours (le projectile sera alors Destroy() à l'impact
        // au lieu d'être remis dans un pool).
        GameObject arrow = poolManager != null
            ? poolManager.Spawn(towerData.ProjectilePrefab, firePoint.position, firePoint.rotation)
            : Instantiate(towerData.ProjectilePrefab, firePoint.position, firePoint.rotation);

        if (arrow == null) return;

        if (!arrow.TryGetComponent(out TowerProjectil projectile)) return;

        // Donne au projectile sa cible et le type de dégâts à infliger
        // (le type sert aux résistances éventuelles côté Health de la cible).

        StatType damageStat;
        switch (towerData.DamageType.Category)
        {
            case DamageCategory.Physical :
                damageStat = StatType.PhysicDamage;
                break;
            
            case DamageCategory.Magical:
                damageStat = StatType.MagicDamage;
                break;
            
            default:
                damageStat = StatType.PhysicDamage;
                break;
        }
        
        int finalDamage = Mathf.RoundToInt(stats.GetStat(damageStat));
        
        projectile.SetTarget(currentTarget.transform, towerData.DamageType, finalDamage);
    }

    //--------Niveau--------//

    public void Awake()
    {
        if (towerData == null) return;

        // Démarre toujours au niveau minimum défini dans la fiche de données.
        currentLevel = towerData.MinLevel;
        stats = GetComponent<EntityStats>();
        towerData.ApplyStatsTo(stats);
        UpdateModel();
    }

    /// <summary>Fait monter la tour d'un niveau (borné entre MinLevel et MaxLevel), et met à jour son modèle.</summary>
    public void LevelUp()
    {
        currentLevel = Mathf.Clamp(currentLevel + 1, towerData.MinLevel, towerData.MaxLevel);
        UpdateModel();
    }

    /// <summary>
    /// Remplace le modèle 3D affiché par celui correspondant au niveau actuel
    /// (towerData.models[currentLevel - 1]), et retrouve le FirePoint dessus.
    /// </summary>
    private void UpdateModel()
    {
        if (towerData.models == null || towerData.models.Length == 0) return;

        // Les niveaux sont 1-indexés (niveau 1, 2, 3...) mais le tableau est
        // 0-indexé : d'où le -1.
        int modelIndex = currentLevel - 1;
        if (modelIndex < 0 || modelIndex >= towerData.models.Length) return;

        GameObject model = towerData.models[modelIndex];
        if (model == null) return;
        if (modelParent == null) return;

        // Détruit l'ancien modèle avant d'instancier le nouveau (évite les doublons).
        if (currentModel != null)
            Destroy(currentModel);

        currentModel = Instantiate(model, modelParent);
        currentModel.transform.localPosition = Vector3.zero;

        // Le point de tir DOIT être un enfant du modèle nommé exactement "FirePoint",
        // sinon les tirs échoueront silencieusement (firePoint restera null).
        firePoint = currentModel.transform.Find("FirePoint");
    }
}