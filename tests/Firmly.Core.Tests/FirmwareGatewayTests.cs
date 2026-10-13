using Firmly.Core;

namespace Firmly.Core.Tests;

public class FirmwareGatewayTests
{
    private static readonly FirmwarePolicy Policy = new(
        readable: ["Fast Charge"], writable: ["Battery Health Manager"], adminWritable: ["Asset Tracking"]);

    private static FakeProvider CreateProvider() => new(
        policy: Policy,
        settings:
        [
            new("Battery Health Manager", FirmwareSettingKind.Enumeration, "Maximize", ["Maximize", "Minimize"], false),
            new("Fast Charge", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
            new("Secure Boot", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
            new("Asset Tracking", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
        ]);

    private static readonly FirmwareCaller User = FirmwareCaller.StandardUser;
    private static readonly FirmwareCaller Admin = FirmwareCaller.Administrator;

    [Fact]
    public void GetSettings_ReturnsOnlyAllowlistedSettings()
    {
        var gateway = new FirmwareGateway(CreateProvider());

        Assert.Equal(["Battery Health Manager", "Fast Charge", "Asset Tracking"], gateway.GetSettings().Select(s => s.Name));
    }

    [Fact]
    public void SetSetting_WritesAndReturnsReadBackValue()
    {
        var provider = CreateProvider();
        var gateway = new FirmwareGateway(provider);

        var result = gateway.SetSetting("Battery Health Manager", "Minimize", User, "pw");

        Assert.Equal("Minimize", result.CurrentValue);
        Assert.Equal([("Battery Health Manager", "Minimize", (string?)"pw")], provider.Writes);
    }

    [Fact]
    public void SetSetting_SkipsWrite_WhenValueIsUnchanged()
    {
        var provider = CreateProvider();

        new FirmwareGateway(provider).SetSetting("Battery Health Manager", "Maximize", User);

        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void SetSetting_NeverWritesSettingOutsideAllowlist()
    {
        var provider = CreateProvider();
        var gateway = new FirmwareGateway(provider);

        var ex = Assert.Throws<FirmwarePolicyException>(() => gateway.SetSetting("Secure Boot", "Disable", Admin));
        Assert.Contains("allowlist", ex.Message);
        Assert.Throws<FirmwarePolicyException>(() => gateway.SetSetting("Fast Charge", "Disable", Admin));
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void SetSetting_Throws_WhenFirmwareDoesNotApplyChange()
    {
        var provider = CreateProvider();
        provider.IgnoreWrites = true;

        Assert.Throws<FirmwareWriteException>(() => new FirmwareGateway(provider).SetSetting("Battery Health Manager", "Minimize", User));
    }

    [Fact]
    public void SetSetting_AdminOnlySetting_RejectsStandardUserBeforeTouchingFirmware()
    {
        var provider = CreateProvider();
        var gateway = new FirmwareGateway(provider);

        var ex = Assert.Throws<FirmwareAccessDeniedException>(() => gateway.SetSetting("Asset Tracking", "Disable", User));
        Assert.Contains("administrator", ex.Message);
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void SetSetting_AdminOnlySetting_AllowsAdministrator()
    {
        var gateway = new FirmwareGateway(CreateProvider());

        Assert.Equal("Disable", gateway.SetSetting("Asset Tracking", "Disable", Admin).CurrentValue);
    }

    [Fact]
    public void RequireAdminForAllChanges_RestrictsUserWritableSettingsToAdministrators()
    {
        var provider = CreateProvider();
        var gateway = new FirmwareGateway(provider, requireAdminForAllChanges: true);

        var ex = Assert.Throws<FirmwareAccessDeniedException>(() => gateway.SetSetting("Battery Health Manager", "Minimize", User));
        Assert.Contains("policy", ex.Message);
        Assert.Empty(provider.Writes);
        Assert.Equal("Minimize", gateway.SetSetting("Battery Health Manager", "Minimize", Admin).CurrentValue);
    }

    [Theory]
    [InlineData("Battery Health Manager", false, true, true)]
    [InlineData("Asset Tracking", false, false, true)]
    [InlineData("Battery Health Manager", true, false, true)]
    [InlineData("Fast Charge", false, false, false)]
    [InlineData("Secure Boot", false, false, false)]
    public void CanChange_ReflectsAccessLevelAndLockdown(string name, bool lockdown, bool userCan, bool adminCan)
    {
        var gateway = new FirmwareGateway(CreateProvider(), lockdown);

        Assert.Equal(userCan, gateway.CanChange(name, User));
        Assert.Equal(adminCan, gateway.CanChange(name, Admin));
    }

    [Theory]
    [InlineData("Battery Health Manager", false, FirmwareChangeAccess.Allowed)]
    [InlineData("Battery Health Manager", true, FirmwareChangeAccess.AdministratorRequired)]
    [InlineData("Asset Tracking", false, FirmwareChangeAccess.AdministratorRequired)]
    [InlineData("Fast Charge", false, FirmwareChangeAccess.ReadOnly)]
    public void GetChangeAccess_ForStandardUser(string name, bool lockdown, FirmwareChangeAccess expected)
    {
        var gateway = new FirmwareGateway(CreateProvider(), lockdown);
        var setting = gateway.GetSettings().Single(s => s.Name == name);

        Assert.Equal(expected, gateway.GetChangeAccess(setting, User));
    }

    [Fact]
    public void GetChangeAccess_IsReadOnly_WhenFirmwareMarksSettingReadOnly()
    {
        var gateway = new FirmwareGateway(CreateProvider());
        var setting = gateway.GetSettings().Single(s => s.Name == "Battery Health Manager") with { IsReadOnly = true };

        Assert.Equal(FirmwareChangeAccess.ReadOnly, gateway.GetChangeAccess(setting, Admin));
    }
}
