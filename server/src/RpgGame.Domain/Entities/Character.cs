namespace RpgGame.Domain.Entities;

public class BaseStats
{
    public int Str { get; set; }
    public int Dex { get; set; }
    public int Con { get; set; }
    public int Int { get; set; }
    public int Wis { get; set; }
    public int Cha { get; set; }
}

public class Character
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public BaseStats BaseStats { get; set; } = new();
    // Race, Class, Level, BaseStats, MaxHp and CurrentHp are D&D-era fields, unused by combat
    // since 3B.1. Their fate is part of the deferred stats & progression decision.
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }

    /// <summary>Combat profile preset key (e.g. "bruiser"); supplies every value combat uses.</summary>
    public string CombatProfile { get; set; } = "skirmisher";
}
