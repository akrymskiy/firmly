namespace Firmly.Core;

public enum FirmwareSettingKind
{
    Enumeration,
    String,
    Integer,
    OrderedList,
}

public enum FirmwareChangeAccess
{
    ReadOnly,
    Allowed,
    AdministratorRequired,
}

/// <summary>A single vendor BIOS/UEFI setting as reported by the firmware.</summary>
/// <param name="PossibleValues">Allowed values for enumerations, or the elements of an ordered list; empty otherwise.</param>
public sealed record FirmwareSetting(
    string Name,
    FirmwareSettingKind Kind,
    string CurrentValue,
    IReadOnlyList<string> PossibleValues,
    bool IsReadOnly);
