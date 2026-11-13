using Core.BarracksSystem;
using UnityEngine;
using Mode = EnumShootType.ShootMode;
using Stat = Core.StatsSystem.EnumStats.StatTypes;

public sealed class TowerStatsPreview
{
    public static readonly Vector2 CardSize = new(430f, 530f);
    private static readonly string[] IconNames = { "Physical", "Magic", "Heal", "Speed", "Range", "Impact", "Pierce" };
    private static Texture2D[] _icons;
    private readonly BarracksStatsPreview _comparison;

    public RectTransform Root => _comparison.Root;

    public TowerStatsPreview(Transform parent, string name)
    {
        if (_icons == null)
            _icons = System.Array.ConvertAll(IconNames, icon => Resources.Load<Texture2D>("TowerStats/" + icon));

        _comparison = new BarracksStatsPreview(parent, name, "Statistiques de la tour", new[]
        {
            "Physique", "Magique", "Soin / allié", "Attaques/s",
            "Portée (m)", "Impact (m)", "Armure (%)"
        }, _icons);
    }

    public void Refresh(TowerHeroStation station, int nextLevel, heros candidate, string title)
    {
        var current = station.GetProfile(station.AssignedHero);
        var future = station.GetProfile(candidate);
        var shoot = station.GetComponent<TowerShoot>();
        string nextHero = candidate != null ? $"{candidate.ClasseName} niv. {candidate.Level}" : "Sans héros";
        string effect = string.IsNullOrEmpty(future.EffectDescription) ? future.TowerName : future.EffectDescription;
        _comparison.Compare($"{title} · {nextHero}\n{effect}",
            Values(current, station.CurrentLevel, station.AssignedHero, shoot),
            Values(future, nextLevel, candidate, shoot));
    }

    private static float[] Values(SOTower profile, int buildingLevel, heros hero, TowerShoot shoot)
    {
        int heroLevel = hero != null ? hero.Level : 0;
        Mode mode = profile.ShootMode;
        float magic = profile.GetStat(buildingLevel, Stat.MagicDamage, heroLevel);
        return new[]
        {
            profile.GetStat(buildingLevel, Stat.PhysicDamage, heroLevel),
            mode == Mode.Heal ? 0f : magic,
            mode == Mode.Heal ? magic : 0f,
            profile.GetStat(buildingLevel, Stat.AttackSpeed, heroLevel),
            profile.GetRange(heroLevel),
            mode == Mode.Heal ? profile.GetRange(heroLevel) :
                mode is Mode.RainArrow or Mode.MagicPower ? shoot.GetImpactRadius(profile) : 0f,
            profile.ArmorPenetration * 100f
        };
    }
}
