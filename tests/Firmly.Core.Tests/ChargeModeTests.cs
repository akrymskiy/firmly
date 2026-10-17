using Firmly.Core;
using Firmly.Core.Vendors.Hp;

namespace Firmly.Core.Tests;

public class ChargeModeTests
{
    private const string Manage = "Let HP Manage My Battery Health";
    private const string Maximize = "Maximize Battery Health Management";
    private const string Minimize = "Minimize Battery Health Management";

    private static readonly EnumerationChargeModes Modes = new("Battery Health Manager",
    [
        new(Manage, "manage", ChargeModeKind.Adaptive),
        new(Maximize, "maximize", ChargeModeKind.Limited, 80),
        new(Minimize, "minimize", ChargeModeKind.Full),
    ]);

    private static readonly FirmwareCaller User = FirmwareCaller.StandardUser;

    private static FirmwareSetting HealthManager(string current, params string[] values) =>
        new("Battery Health Manager", FirmwareSettingKind.Enumeration, current, values.Length > 0 ? values : [Maximize, Manage, Minimize], false);

    private static FakeProvider CreateProvider(FirmwarePolicy? policy = null) => new(
        policy: policy ?? new FirmwarePolicy([], writable: ["Battery Health Manager"]),
        settings: [HealthManager(Maximize)])
    {
        ChargeModeSetting = Modes,
    };

    [Fact]
    public void From_ListsKnownModes_InTheirOwnOrder_WithCurrent()
    {
        var modes = Modes.From([HealthManager(Maximize)])!;

        Assert.Equal(["manage", "maximize", "minimize"], modes.Modes.Select(m => m.Id));
        Assert.Equal("maximize", modes.CurrentId);
        Assert.Equal((ChargeModeKind.Limited, Maximize, (int?)80), (modes.Current!.Kind, modes.Current.Label, modes.Current.LimitPercent));
        Assert.Equal([new SettingChange("Battery Health Manager", Minimize)], modes.Find("minimize")!.Changes);
    }

    [Fact]
    public void From_LeavesOutValuesTheFirmwareDoesNotOffer_AndIgnoresUnknownOnes()
    {
        var modes = Modes.From([HealthManager(Minimize, Minimize, "Some Future Mode", Maximize)])!;

        Assert.Equal(["maximize", "minimize"], modes.Modes.Select(m => m.Id));
    }

    [Fact]
    public void From_UnknownCurrentValue_HasNoCurrentMode()
    {
        var modes = Modes.From([HealthManager("Some Future Mode", Maximize, "Some Future Mode")])!;

        Assert.Null(modes.CurrentId);
        Assert.Null(modes.Current);
    }

    [Fact]
    public void From_WithoutTheSetting_IsNull()
    {
        Assert.Null(Modes.From([new("Fast Charge", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false)]));
    }

    [Fact]
    public void Hp_MapsBatteryHealthManager_AndRescalesOnlyWhenLimited()
    {
        var hp = new HpBiosProvider();

        var modes = hp.GetChargeModes([HealthManager(Maximize)])!;

        Assert.Equal(
            [("hp-let-hp-manage", ChargeModeKind.Adaptive), ("hp-maximize", ChargeModeKind.Limited), ("hp-minimize", ChargeModeKind.Full)],
            modes.Modes.Select(m => (m.Id, m.Kind)));
        Assert.Equal(0.8, hp.GetReportedChargeLimit([HealthManager(Maximize)]));
        Assert.Null(hp.GetReportedChargeLimit([HealthManager(Manage)]));
        Assert.Null(hp.GetReportedChargeLimit([HealthManager(Minimize)]));
    }

    [Fact]
    public void SetChargeMode_WritesTheModesSettings_AndReturnsReadBack()
    {
        var provider = CreateProvider();

        var modes = new FirmwareGateway(provider).SetChargeMode("minimize", User);

        Assert.Equal("minimize", modes.CurrentId);
        Assert.Equal([("Battery Health Manager", Minimize, (string?)null)], provider.Writes);
    }

    [Fact]
    public void SetChargeMode_UnknownMode_IsRefused()
    {
        var provider = CreateProvider();

        Assert.Throws<FirmwarePolicyException>(() => new FirmwareGateway(provider).SetChargeMode("turbo", User));
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void SetChargeMode_FollowsTheSettingsAccess()
    {
        var provider = CreateProvider(new FirmwarePolicy([], [], adminWritable: ["Battery Health Manager"]));
        var gateway = new FirmwareGateway(provider);

        Assert.Equal(FirmwareChangeAccess.AdministratorRequired, gateway.GetChargeModeAccess(gateway.GetChargeModes()!, gateway.GetSettings(), User));
        Assert.Throws<FirmwareAccessDeniedException>(() => gateway.SetChargeMode("minimize", User));
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void ChargeModes_OfANonWritableSetting_AreReadOnly()
    {
        var gateway = new FirmwareGateway(CreateProvider(new FirmwarePolicy(readable: ["Battery Health Manager"], writable: [])));

        Assert.Equal(FirmwareChangeAccess.ReadOnly, gateway.GetChargeModeAccess(gateway.GetChargeModes()!, gateway.GetSettings(), FirmwareCaller.Administrator));
    }

    [Fact]
    public void SetChargeMode_FirmwareIgnoringTheWrite_Throws()
    {
        var provider = CreateProvider();
        provider.IgnoreWrites = true;

        Assert.Throws<FirmwareWriteException>(() => new FirmwareGateway(provider).SetChargeMode("minimize", User));
    }
}
