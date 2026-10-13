using System.Security.Claims;
using System.Security.Principal;

namespace Firmly.Core;

/// <summary>Who is asking for a change; Firmly.Service fills this in from the caller's Windows token.</summary>
/// <param name="IsAdministrator">
/// Member of the local Administrators group, elevated or not. A non-elevated (UAC-filtered) token still lists
/// the group, as deny-only, so administrators can make admin-only changes without a UAC prompt.
/// </param>
public sealed record FirmwareCaller(bool IsAdministrator)
{
    private static readonly string AdministratorsSid =
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;

    public static FirmwareCaller Administrator { get; } = new(IsAdministrator: true);

    public static FirmwareCaller StandardUser { get; } = new(IsAdministrator: false);

    public static FirmwareCaller FromIdentity(WindowsIdentity identity) => FromClaims(identity.Claims);

    public static FirmwareCaller FromClaims(IEnumerable<Claim> claims) =>
        new(claims.Any(c => c.Value == AdministratorsSid && c.Type is ClaimTypes.GroupSid or ClaimTypes.DenyOnlySid));
}
