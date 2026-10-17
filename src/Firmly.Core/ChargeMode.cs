namespace Firmly.Core;

/// <summary>What a charge mode does, in Firmly's terms, whatever the vendor calls it.</summary>
public enum ChargeModeKind
{
    /// <summary>The firmware decides when to charge fully, e.g. from how the device is used.</summary>
    Adaptive,

    /// <summary>Stops charging at <see cref="ChargeMode.LimitPercent"/> to slow battery wear.</summary>
    Limited,

    /// <summary>Charges to 100%.</summary>
    Full,
}

/// <summary>One charging behaviour a device's firmware offers.</summary>
/// <param name="Id">Stable for the vendor, e.g. "hp-maximize"; the app sends it back to choose this mode.</param>
/// <param name="Label">The vendor's own name for the mode, as in its BIOS setup and documentation.</param>
/// <param name="LimitPercent">Where charging stops, for <see cref="ChargeModeKind.Limited"/>.</param>
/// <param name="Changes">The setting writes that select this mode, in order.</param>
public sealed record ChargeMode(string Id, ChargeModeKind Kind, string Label, int? LimitPercent, IReadOnlyList<SettingChange> Changes);

public sealed record SettingChange(string Name, string Value);

/// <summary>The charge modes a device offers, and which one is on.</summary>
/// <param name="CurrentId">Null when the firmware's settings match none of <paramref name="Modes"/>.</param>
public sealed record ChargeModes(IReadOnlyList<ChargeMode> Modes, string? CurrentId)
{
    public ChargeMode? Current => CurrentId is null ? null : Find(CurrentId);

    public ChargeMode? Find(string id) => Modes.FirstOrDefault(m => m.Id == id);

    /// <summary>Every setting any mode writes.</summary>
    public IEnumerable<string> SettingNames => Modes.SelectMany(m => m.Changes).Select(c => c.Name).Distinct();
}

/// <summary>A value of an enumeration setting that selects a charge mode; see <see cref="EnumerationChargeModes"/>.</summary>
public sealed record ChargeModeValue(string Value, string Id, ChargeModeKind Kind, int? LimitPercent = null);

/// <summary>
/// Charge modes chosen by a single enumeration setting, as on HP (Battery Health Manager). Values the firmware
/// doesn't offer are left out, and values Firmly doesn't know aren't offered as modes.
/// </summary>
public sealed class EnumerationChargeModes(string settingName, IReadOnlyList<ChargeModeValue> values)
{
    public string SettingName => settingName;

    /// <returns>Null when the setting is missing, or offers none of the known values.</returns>
    public ChargeModes? From(IReadOnlyList<FirmwareSetting> settings)
    {
        var setting = settings.FirstOrDefault(s => s.Name == settingName && s.Kind == FirmwareSettingKind.Enumeration);
        if (setting is null)
            return null;

        List<ChargeMode> modes =
        [
            .. values
                .Where(v => setting.PossibleValues.Contains(v.Value, StringComparer.Ordinal))
                .Select(v => new ChargeMode(v.Id, v.Kind, v.Value, v.LimitPercent, [new(settingName, v.Value)])),
        ];
        if (modes.Count == 0)
            return null;

        return new ChargeModes(modes, modes.FirstOrDefault(m => m.Label == setting.CurrentValue)?.Id);
    }
}
