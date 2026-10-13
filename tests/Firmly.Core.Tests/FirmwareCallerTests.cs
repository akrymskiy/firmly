using System.Security.Claims;
using Firmly.Core;

namespace Firmly.Core.Tests;

public class FirmwareCallerTests
{
    private const string Administrators = "S-1-5-32-544";

    [Fact]
    public void ElevatedAdministrator_IsAdministrator()
    {
        Assert.True(FirmwareCaller.FromClaims([new Claim(ClaimTypes.GroupSid, Administrators)]).IsAdministrator);
    }

    [Fact]
    public void NonElevatedAdministrator_IsAdministrator()
    {
        // UAC-filtered tokens keep Administrators as a deny-only group.
        Assert.True(FirmwareCaller.FromClaims([new Claim(ClaimTypes.DenyOnlySid, Administrators)]).IsAdministrator);
    }

    [Fact]
    public void StandardUser_IsNotAdministrator()
    {
        Claim[] claims = [new(ClaimTypes.GroupSid, "S-1-5-32-545"), new(ClaimTypes.PrimarySid, "S-1-5-21-1-2-3-1001")];

        Assert.False(FirmwareCaller.FromClaims(claims).IsAdministrator);
    }

    [Fact]
    public void AdministratorsSidInOtherClaimType_DoesNotCount()
    {
        Assert.False(FirmwareCaller.FromClaims([new Claim(ClaimTypes.Name, Administrators)]).IsAdministrator);
    }
}
