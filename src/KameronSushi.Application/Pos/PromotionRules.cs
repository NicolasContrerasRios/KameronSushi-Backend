namespace KameronSushi.Application.Pos;

public static class PromotionRules
{
    public static int GetMaximumSelectionsPerOption(int requiredRolls)
    {
        if (requiredRolls <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredRolls));

        // 40/50 cortes: 2; 60/70: 3; 80: 4.
        return Math.Max(1, requiredRolls / 2);
    }
}
