namespace Firmly.Core;

/// <summary>Who is asking for a change; Firmly.Service fills this in from the caller's Windows token.</summary>
public sealed record FirmwareCaller(bool IsAdministrator)
{
    public static FirmwareCaller Administrator { get; } = new(IsAdministrator: true);

    public static FirmwareCaller StandardUser { get; } = new(IsAdministrator: false);
}
