using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests.Model;

/// <summary>
/// One job placed in one day. The three timestamps have to stay in order, because the
/// objective reads the gaps between them as waiting, working and lateness.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class StopTests
{
    [Fact]
    public void RefusesAStartBeforeTheTechnicianArrives()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Stop(JobId.New(), SmallCity.At(9), SmallCity.At(8), SmallCity.At(10), 12d));
    }

    [Fact]
    public void RefusesWorkThatFinishesBeforeItStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Stop(JobId.New(), SmallCity.At(9), SmallCity.At(9), SmallCity.At(8), 12d));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RefusesADriveThatIsNotARealNumberOfMinutes(double travelMin)
    {
        // NaN passes every `< 0` check ever written and then spreads through every cost the
        // search compares.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Stop(JobId.New(), SmallCity.At(9), SmallCity.At(9), SmallCity.At(10), travelMin));
    }

    [Fact]
    public void ReportsWaitingSeparatelyFromWorking()
    {
        // Arriving before the window opens is capacity the schedule is spending, and the
        // reason a tighter route is not always a better one.
        var stop = new Stop(JobId.New(), SmallCity.At(8, 40), SmallCity.At(9), SmallCity.At(10), 12d);

        Assert.Equal(TimeSpan.FromMinutes(20), stop.Wait);
        Assert.Equal(TimeSpan.FromHours(1), stop.Duration);
    }
}
