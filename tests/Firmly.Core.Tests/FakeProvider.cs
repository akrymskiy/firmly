using Firmly.Core;

namespace Firmly.Core.Tests;

internal sealed class FakeProvider(
    string vendor = "Fake",
    bool supported = true,
    FirmwarePolicy? policy = null,
    params FirmwareSetting[] settings) : IFirmwareProvider
{
    private readonly List<FirmwareSetting> _settings = [.. settings];

    public string Vendor => vendor;
    public FirmwarePolicy Policy { get; } = policy ?? new FirmwarePolicy([], []);
    public List<(string Name, string Value, string? Password)> Writes { get; } = [];

    /// <summary>When true, writes are accepted but the stored value does not change.</summary>
    public bool IgnoreWrites { get; set; }

    public double? ReportedChargeLimit { get; set; }

    /// <summary>Charge modes worked out from the settings; none unless a test sets this.</summary>
    public EnumerationChargeModes? ChargeModeSetting { get; set; }

    public bool IsSupported() => supported;

    public ChargeModes? GetChargeModes(IReadOnlyList<FirmwareSetting> settings) => ChargeModeSetting?.From(settings);

    public double? GetReportedChargeLimit(IReadOnlyList<FirmwareSetting> settings) => ReportedChargeLimit;

    public IReadOnlyList<FirmwareSetting> GetSettings() => _settings.ToList();

    public void SetSetting(string name, string value, string? biosPassword)
    {
        Writes.Add((name, value, biosPassword));
        if (IgnoreWrites)
            return;

        var index = _settings.FindIndex(s => s.Name == name);
        _settings[index] = _settings[index] with { CurrentValue = value };
    }
}
