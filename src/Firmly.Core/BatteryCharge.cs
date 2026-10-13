namespace Firmly.Core;

/// <summary>
/// The battery's real charge, worked out from what Windows reports.
/// </summary>
/// <remarks>
/// While a firmware charge limit is on, some firmware rescales what Windows sees: it reports the full-charge
/// capacity as <c>limit × real capacity</c> and caps the remaining capacity there, so Windows shows 100% at the
/// limit. Measured on an HP EliteBook with Battery Health Manager on Maximize: real 59.5 of 66.1 Wh was reported
/// as 52.9 of 52.9 Wh. Below the limit the remaining capacity is real; above it, only "at least the limit" is known.
/// </remarks>
/// <param name="Percent">Real charge, or the limit itself when <see cref="AtLeast"/> is true.</param>
/// <param name="AtLeast">The real charge is at or above the limit, and the firmware hides how far above.</param>
/// <param name="WindowsPercent">What Windows shows.</param>
/// <param name="LimitPercent">The firmware's charge limit when it rescales readings; null when readings are real.</param>
public sealed record BatteryCharge(int Percent, bool AtLeast, int WindowsPercent, int? LimitPercent)
{
    /// <param name="remainingMwh">Remaining capacity as Windows reports it.</param>
    /// <param name="fullMwh">Full-charge capacity as Windows reports it.</param>
    /// <param name="reportedChargeLimit">Fraction (e.g. 0.8) at which the firmware rescales readings, or null.</param>
    /// <returns>Null when there is no usable reading, e.g. no battery.</returns>
    public static BatteryCharge? FromReport(int? remainingMwh, int? fullMwh, double? reportedChargeLimit)
    {
        if (remainingMwh is not { } remaining || fullMwh is not ({ } full and > 0))
            return null;

        var windowsPercent = ToPercent(remaining, full);
        if (reportedChargeLimit is not ({ } limit and > 0 and < 1))
            return new BatteryCharge(windowsPercent, AtLeast: false, windowsPercent, LimitPercent: null);

        var limitPercent = (int)Math.Round(limit * 100);
        return remaining >= full
            ? new BatteryCharge(limitPercent, AtLeast: true, windowsPercent, limitPercent)
            : new BatteryCharge(ToPercent(remaining, full / limit), AtLeast: false, windowsPercent, limitPercent);
    }

    private static int ToPercent(double part, double whole) => (int)Math.Round(Math.Clamp(part / whole, 0, 1) * 100);
}
