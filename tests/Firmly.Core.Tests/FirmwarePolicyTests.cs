using Firmly.Core;

namespace Firmly.Core.Tests;

public class FirmwarePolicyTests
{
    private static readonly FirmwareSetting Battery = new(
        "Battery Health Manager", FirmwareSettingKind.Enumeration, "Maximize",
        ["Maximize", "Let HP Manage", "Minimize"], IsReadOnly: false);

    private readonly FirmwarePolicy _policy = new(readable: ["IoT IMEI"], writable: ["Battery Health Manager"]);

    [Fact]
    public void WritableSettings_AreAlsoReadable()
    {
        Assert.True(_policy.IsReadable("Battery Health Manager"));
        Assert.False(_policy.IsWritable("IoT IMEI"));
    }

    [Fact]
    public void FilterReadable_DropsSettingsNotOnAllowlist()
    {
        var imei = new FirmwareSetting("IoT IMEI", FirmwareSettingKind.String, "123", [], IsReadOnly: true);
        var serial = new FirmwareSetting("Serial Number", FirmwareSettingKind.String, "ABC", [], IsReadOnly: true);

        var result = _policy.FilterReadable([Battery, imei, serial]);

        Assert.Equal(["Battery Health Manager", "IoT IMEI"], result.Select(s => s.Name));
    }

    [Fact]
    public void EnsureCanWrite_AllowsValidValue()
    {
        _policy.EnsureCanWrite(Battery, "Minimize");
    }

    [Fact]
    public void EnsureCanWrite_RejectsSettingNotOnWriteAllowlist()
    {
        var secureBoot = new FirmwareSetting("Secure Boot", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false);

        Assert.Throws<FirmwarePolicyException>(() => _policy.EnsureCanWrite(secureBoot, "Disable"));
    }

    [Fact]
    public void EnsureCanWrite_RejectsValueTheFirmwareDoesNotOffer()
    {
        Assert.Throws<FirmwarePolicyException>(() => _policy.EnsureCanWrite(Battery, "Overcharge"));
    }

    [Fact]
    public void EnsureCanWrite_IsCaseSensitive()
    {
        Assert.Throws<FirmwarePolicyException>(() => _policy.EnsureCanWrite(Battery, "minimize"));
    }

    [Fact]
    public void EnsureCanWrite_RejectsReadOnlySetting()
    {
        Assert.Throws<FirmwarePolicyException>(() => _policy.EnsureCanWrite(Battery with { IsReadOnly = true }, "Minimize"));
    }

    [Fact]
    public void EnsureCanWrite_RejectsNonEnumerationSetting()
    {
        var policy = new FirmwarePolicy([], ["Asset Tag"]);
        var assetTag = new FirmwareSetting("Asset Tag", FirmwareSettingKind.String, "", [], false);

        Assert.Throws<FirmwarePolicyException>(() => policy.EnsureCanWrite(assetTag, "x"));
    }

    [Fact]
    public void AdminWritableSettings_AreWritableReadableAndRequireAdmin()
    {
        var policy = new FirmwarePolicy([], writable: ["Battery Health Manager"], adminWritable: ["Asset Tracking"]);

        Assert.True(policy.IsWritable("Asset Tracking"));
        Assert.True(policy.IsReadable("Asset Tracking"));
        Assert.True(policy.RequiresAdmin("Asset Tracking"));
        Assert.False(policy.RequiresAdmin("Battery Health Manager"));
    }

    [Fact]
    public void Constructor_RejectsSettingOnBothWriteLists()
    {
        Assert.Throws<ArgumentException>(() => new FirmwarePolicy([], ["Fast Charge"], ["Fast Charge"]));
    }
}
