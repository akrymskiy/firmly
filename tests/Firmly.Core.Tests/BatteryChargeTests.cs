using Firmly.Core;

namespace Firmly.Core.Tests;

public class BatteryChargeTests
{
    [Fact]
    public void WithoutLimit_RealChargeIsWhatWindowsShows()
    {
        // HP EliteBook on Let HP Manage: 59.5 of 66.1 Wh.
        Assert.Equal(new BatteryCharge(90, false, 90, null), BatteryCharge.FromReport(59529, 66081, null));
    }

    [Fact]
    public void AtLimit_RealChargeIsOnlyKnownToBeAtLeastTheLimit()
    {
        // Same battery on Maximize: reported as 52.9 of 52.9 Wh while really at 90%.
        Assert.Equal(new BatteryCharge(80, true, 100, 80), BatteryCharge.FromReport(52860, 52860, 0.8));
    }

    [Fact]
    public void BelowLimit_RealChargeUsesTheUnscaledCapacity()
    {
        // 46 Wh of a real 66.1 Wh battery is 70%, which Windows shows as 87% of the scaled 52.9 Wh.
        Assert.Equal(new BatteryCharge(70, false, 87, 80), BatteryCharge.FromReport(46000, 52860, 0.8));
    }

    [Theory]
    [InlineData(null, 66081)]
    [InlineData(50000, null)]
    [InlineData(50000, 0)]
    public void NoUsableReading_ReturnsNull(int? remaining, int? full)
    {
        Assert.Null(BatteryCharge.FromReport(remaining, full, null));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void OutOfRangeLimit_IsIgnored(double limit)
    {
        Assert.Equal(new BatteryCharge(90, false, 90, null), BatteryCharge.FromReport(59529, 66081, limit));
    }
}
