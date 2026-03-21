namespace RpgGame.Domain.Entities;

public class BaseStats
{
    public int Power { get; set; }
    public int Speed { get; set; }
    public int Precision { get; set; }
    public int Resilience { get; set; }
    public int Focus { get; set; }
    public int Influence { get; set; }
}

public class Character
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;   // cosmetic only
    public string Class { get; set; } = string.Empty;  // cosmetic only
    public int Level { get; set; } = 1;                // cosmetic only
    public BaseStats BaseStats { get; set; } = new();
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int MovementRange { get; set; }
    public int VisionRange { get; set; }
}
