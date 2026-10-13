using CommunityToolkit.Mvvm.ComponentModel;
using Firmly.Core;
using Windows.Devices.Power;
using Windows.System.Power;

namespace Firmly.App.ViewModels;

/// <summary>
/// The battery's real charge, live from Windows. While the firmware's charge limit rescales what Windows shows,
/// the real figure is worked out with <see cref="BatteryCharge"/>.
/// </summary>
public partial class BatteryViewModel : ObservableObject
{
    private readonly Battery _battery = Battery.AggregateBattery;
    private double? _reportedChargeLimit;

    public BatteryViewModel()
    {
        Charge = "";
        Details = "";
        _battery.ReportUpdated += (_, _) => App.DispatcherQueue.TryEnqueue(Update);
        Update();
    }

    /// <summary>e.g. "Battery 72%".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReading))]
    public partial string Charge { get; set; }

    /// <summary>e.g. "charging · limited to 80% · Windows shows 90%".</summary>
    [ObservableProperty]
    public partial string Details { get; set; }

    public bool HasReading => Charge.Length > 0;

    /// <summary>Called whenever settings load, since the firmware's charge limit changes what Windows reports.</summary>
    public void SetReportedChargeLimit(double? limit)
    {
        _reportedChargeLimit = limit;
        Update();
    }

    private void Update()
    {
        var report = _battery.GetReport();
        var charge = BatteryCharge.FromReport(
            report.RemainingCapacityInMilliwattHours, report.FullChargeCapacityInMilliwattHours, _reportedChargeLimit);
        if (charge is null)
        {
            Charge = "";
            Details = "";
            return;
        }

        Charge = charge.AtLeast ? $"Battery {charge.Percent}% or more" : $"Battery {charge.Percent}%";

        List<string> details = [];
        if (report.Status == BatteryStatus.Charging)
            details.Add("charging");
        if (charge.LimitPercent is { } limit)
        {
            details.Add($"limited to {limit}%");
            details.Add($"Windows shows {charge.WindowsPercent}%");
        }
        Details = string.Join(" · ", details);
    }
}
