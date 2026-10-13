namespace Firmly.Core;

public enum FirmwareSettingKind
{
    Enumeration,
    String,
    Integer,
}

/// <summary>A single vendor BIOS/UEFI setting as reported by the firmware.</summary>
public sealed record FirmwareSetting(
    string Name,
    FirmwareSettingKind Kind,
    string CurrentValue,
    IReadOnlyList<string> PossibleValues,
    bool IsReadOnly);
