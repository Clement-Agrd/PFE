using Core.HealthSystem;
using Core.PoolingSystem;
using UnityEngine;

/// <summary>
/// Un projectile tiré par une Tower. Avance vers sa cible, lui inflige des
/// dégâts à l'impact, puis retourne au pool (ou est détruit si pas poolé).
///
/// Point important : la détection d'impact tient compte de la vitesse de
/// déplacement de CETTE frame, pas seulement d'une distance fixe. Sans ça,
/// un projectile très rapide (ou un framerate bas, donc un grand deltaTime)
/// pourrait "sauter" par-dessus sa cible en un seul déplacement sans jamais
/// passer sous le seuil de distance — et continuerait indéfiniment tout droit.
/// </summary>
public class TowerProjectil : MonoBehaviour
{
    // La cible visée (fournie par Tower.Shoot() via SetTarget).
    private Transform target;
    // Le type de dégâts à infliger (utilisé par le Health de la cible pour les résistances).
    private DamageType damageType;

    private bool dealsDamage = true;
    
    private EnumProjectilSetting.projectileMovement projectileMovement;

    [Header("projectil classic data")]
    [Min(0f)]
    [SerializeField] private float speed = 10f;

    [Tooltip("Distance à laquelle le projectile considère avoir touché sa cible.")]
    [SerializeField] private float hitDistance = 1f;
    
    [Header("projectil en cloche datat")]
    [SerializeField] private float arcHeight = 2f;
    [Min(0.01f)][SerializeField] private float travelDuration = 2f;
    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float elapsedTime;
    
    
    
    // position

    private int damage;

    // Référence au composant de pooling (récupérée une fois, dans Awake).
    private PooledObject _pooled;

    // Verrou anti double-exécution : empêche d'infliger les dégâts ou de
    // relâcher l'objet au pool deux fois pour le même tir (par exemple si
    // deux conditions de sortie se déclenchent la même frame).
    private bool _hasDespawned;

    private void Awake()
    {
        // TryGetComponent ne lève jamais d'exception, même si le composant est absent.
        TryGetComponent(out _pooled);
    }

    private void OnEnable()
    {
        // TRÈS IMPORTANT pour le pooling : cet objet est réutilisé (pas recréé)
        // à chaque tir. Sans cette remise à zéro, un projectile recyclé
        // garderait l'état de son tir précédent (ex. _hasDespawned resté à
        // true → il ne ferait plus jamais rien de la partie).
        _hasDespawned = false;
        target = null;
    }

    /// <summary>Appelé par Tower juste après avoir spawné ce projectile.</summary>
    public void SetTarget(
        Transform newTarget, 
        DamageType newDamageType, 
        int newDamage, 
        EnumProjectilSetting.projectileMovement movement,
        Vector3 impactPosition,
        bool damageEnable)
    {
        target = newTarget;
        damageType = newDamageType;
        damage = newDamage;
        projectileMovement = movement;
        
        startPosition = transform.position;
        targetPosition = impactPosition;
        elapsedTime = 0f;
        dealsDamage = damageEnable;
    }

    private void Update()
    {
        // Déjà despawné cette frame (ou une frame précédente) : plus rien à faire.
        if (_hasDespawned) return;

        Vector3 previousPosition = transform.position;

        switch (projectileMovement)
        {
            case EnumProjectilSetting.projectileMovement.Direct :
                
                // La cible a disparu (morte et retournée à son pool, désactivée...) :
                // on ne reste pas planté à viser du vide, on disparaît proprement.
                if (target == null || !target.gameObject.activeInHierarchy)
                {
                    Despawn();
                    return;
                }

                // La distance que le projectile va parcourir CETTE frame.
                float step = speed * Time.deltaTime;
                float distance = Vector3.Distance(transform.position, target.position);

                // Le seuil d'impact effectif est le plus grand entre hitDistance (réglage
                // manuel) et step (le pas de cette frame). Ça garantit qu'un projectile
                // rapide ne peut jamais dépasser sa cible sans que l'impact soit détecté.
                float effectiveHitDistance = Mathf.Max(hitDistance, step);

                if (distance <= effectiveHitDistance)
                {
                    HitTarget();
                    return;
                }

                transform.position = Vector3.MoveTowards(transform.position, target.position, step);
                
                break;
            
            case EnumProjectilSetting.projectileMovement.Arcing :
                
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / travelDuration);
                Vector3 currentPosition = Vector3.Lerp(startPosition, targetPosition, t);
                float height = 4f * arcHeight * t * (1f - t);
                Debug.Log($"Arc Height : {arcHeight} | Hauteur calculée : {height}");
                Vector3 arcPosition = new Vector3(currentPosition.x, currentPosition.y + height, currentPosition.z);

                transform.position = arcPosition;

                if (elapsedTime >= travelDuration)
                {
                    HitTarget();
                }
                
                break;
        }

        // Oriente la flèche (axe +Z) dans le sens du déplacement, pour suivre la trajectoire en cloche.
        Vector3 moveDirection = transform.position - previousPosition;
        if (!_hasDespawned && moveDirection.sqrMagnitude > 1e-8f)
            transform.rotation = Quaternion.LookRotation(moveDirection);
    }

    /// <summary>Inflige les dégâts à la cible (si elle a un Health), puis despawn.</summary>
    private void HitTarget()
    {
        if (dealsDamage && target != null && target.TryGetComponent(out Health health))
            health.TakeDamage(new DamageInfo(damage, damageType));

        Despawn();
    }

    /// <summary>
    /// Fait disparaître le projectile : retourne au pool s'il en a un
    /// (réutilisable plus tard), sinon Destroy() en secours.
    /// </summary>
    private void Despawn()
    {
        if (_hasDespawned) return; // sécurité anti double-appel
        _hasDespawned = true;

        target = null;

        if (_pooled != null) _pooled.Release();
        else Destroy(gameObject);
    }
}