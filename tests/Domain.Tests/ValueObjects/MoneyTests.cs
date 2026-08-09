using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.ValueObjects;

/// <summary>
/// Money math is cheap to test and load-bearing — every invoice total runs through it, and
/// the two ways a fixed-width money type goes wrong are losing fractions on the way in and
/// wrapping silently on the way up.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class MoneyTests
{
    public static TheoryData<decimal, long> DollarAmounts => new()
    {
        { 0m, 0L },
        { 1m, 100L },
        { 0.01m, 1L },
        { 19.99m, 1_999L },
        { -4.50m, -450L },
        { 1_234.56m, 123_456L },
    };

    public static TheoryData<decimal, decimal, long> Quantities => new()
    {
        { 19.99m, 3m, 5_997L },        // three parts at a normal price
        { 85m, 2.5m, 21_250L },        // two and a half hours of labour
        { 90m, 0.333m, 2_997L },       // a third of an hour, rounded to the cent
        { 10m, 0.005m, 5L },           // a half-cent rounds away from zero
        { -20m, 1m, -2_000L },         // a discount line
    };

    public static TheoryData<decimal, long> SubCentAmounts => new()
    {
        { 0.004m, 0L },
        { 0.005m, 1L },
        { -0.005m, -1L },
        { 1.9999m, 200L },
        { 0.994m, 99L },
    };

    [Theory]
    [MemberData(nameof(DollarAmounts))]
    public void FromDollarsConvertsToWholeCents(decimal dollars, long expectedCents)
    {
        var money = Money.FromDollars(dollars);

        Assert.Equal(expectedCents, money.Cents);
    }

    [Theory]
    [MemberData(nameof(SubCentAmounts))]
    public void FromDollarsRoundsToTheNearestCentRatherThanTruncating(decimal dollars, long expectedCents)
    {
        var money = Money.FromDollars(dollars);

        Assert.Equal(expectedCents, money.Cents);
    }

    [Fact]
    public void AddSumsTheUnderlyingCents()
    {
        var total = Money.FromDollars(19.99m).Add(Money.FromDollars(0.02m));

        Assert.Equal(new Money(2_001), total);
    }

    [Fact]
    public void AddAppliesNegativeAmountsAsCredits()
    {
        var net = Money.FromDollars(100m).Add(Money.FromDollars(-25.50m));

        Assert.Equal(Money.FromDollars(74.50m), net);
    }

    [Fact]
    public void AddThrowsRatherThanWrappingPastTheLargestAmount()
    {
        var max = new Money(long.MaxValue);

        Assert.Throws<OverflowException>(() => max.Add(new Money(1)));
    }

    [Fact]
    public void AddThrowsRatherThanWrappingPastTheSmallestAmount()
    {
        var min = new Money(long.MinValue);

        Assert.Throws<OverflowException>(() => min.Add(new Money(-1)));
    }

    [Theory]
    [MemberData(nameof(Quantities))]
    public void MultiplyScalesTheAmountAndRoundsToTheCent(decimal unitDollars, decimal quantity, long expectedCents)
    {
        var line = Money.FromDollars(unitDollars).Multiply(quantity);

        Assert.Equal(expectedCents, line.Cents);
    }

    [Fact]
    public void MultiplyingByNothingCostsNothing()
    {
        Assert.Equal(Money.Zero, Money.FromDollars(85m).Multiply(0m));
    }

    [Fact]
    public void MultiplyThrowsRatherThanWrappingPastTheLargestAmount()
    {
        var max = new Money(long.MaxValue);

        Assert.Throws<OverflowException>(() => max.Multiply(2m));
    }

    [Fact]
    public void FromDollarsThrowsWhenTheAmountDoesNotFitInCents()
    {
        // Comfortably inside decimal's range, well outside long's once scaled to cents.
        Assert.Throws<OverflowException>(() => Money.FromDollars(100_000_000_000_000_000m));
    }

    [Fact]
    public void FromDollarsThrowsWhenScalingItselfOverflows()
    {
        Assert.Throws<OverflowException>(() => Money.FromDollars(decimal.MaxValue));
    }
}
