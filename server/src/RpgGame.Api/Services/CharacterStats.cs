namespace RpgGame.Api.Services;

internal static class CharacterStats
{
    public static int MaxHp(int resilience)      => 5 + resilience * 2;
    public static int MovementRange(int speed)   => 2 + speed / 4;
    public static int VisionRange(int focus)     => 2 + focus / 4;
    public static int DamageBase(int power)      => 1 + power / 5;
    public static int AttackBonus(int precision) => (precision - 10) / 2;
    public static int DodgeChance(int speed)     => (speed - 10) / 2;
}
