using KameronSushi.Application.Pos;
using Xunit;

namespace KameronSushi.Tests;

public sealed class PromotionRulesTests
{
    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    [InlineData(7, 3)]
    [InlineData(8, 4)]
    public void MaximumSelectionsPerOptionMatchesPromotionRules(int rolls, int expected)
    {
        Assert.Equal(expected, PromotionRules.GetMaximumSelectionsPerOption(rolls));
    }
}
