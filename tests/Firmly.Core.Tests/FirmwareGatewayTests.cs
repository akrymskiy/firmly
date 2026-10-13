using Firmly.Core;

namespace Firmly.Core.Tests;

public class FirmwareGatewayTests
{
    private static readonly FirmwarePolicy Policy = new(readable: ["Fast Charge"], writable: ["Battery Health Manager"]);

    private static FakeProvider CreateProvider() => new(
        policy: Policy,
        settings:
        [
            new("Battery Health Manager", FirmwareSettingKind.Enumeration, "Maximize", ["Maximize", "Minimize"], false),
            new("Fast Charge", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
            new("Secure Boot", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
        ]);

    [Fact]
    public void GetSettings_ReturnsOnlyAllowlistedSettings()
    {
        var gateway = new FirmwareGateway(CreateProvider());

        Assert.Equal(["Battery Health Manager", "Fast Charge"], gateway.GetSettings().Select(s => s.Name));
    }

    [Fact]
    public void SetSetting_WritesAndReturnsReadBackValue()
    {
        var provider = CreateProvider();
        var gateway = new FirmwareGateway(provider);

        var result = gateway.SetSetting("Battery Health Manager", "Minimize", "pw");

        Assert.Equal("Minimize", result.CurrentValue);
        Assert.Equal([("Battery Health Manager", "Minimize", (string?)"pw")], provider.Writes);
    }

    [Fact]
    public void SetSetting_SkipsWrite_WhenValueIsUnchanged()
    {
        var provider = CreateProvider();

        new FirmwareGateway(provider).SetSetting("Battery Health Manager", "Maximize");

        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void SetSetting_NeverWritesSettingOutsideAllowlist()
    {
        var provider = CreateProvider();
        var gateway = new FirmwareGateway(provider);

        Assert.Throws<FirmwarePolicyException>(() => gateway.SetSetting("Secure Boot", "Disable"));
        Assert.Throws<FirmwarePolicyException>(() => gateway.SetSetting("Fast Charge", "Disable"));
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void SetSetting_Throws_WhenFirmwareDoesNotApplyChange()
    {
        var provider = CreateProvider();
        provider.IgnoreWrites = true;

        Assert.Throws<FirmwareWriteException>(() => new FirmwareGateway(provider).SetSetting("Battery Health Manager", "Minimize"));
    }
}
