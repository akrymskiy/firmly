using Firmly.Core;
using Firmly.Core.Tests;
using Firmly.Ipc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Firmly.Service.Tests;

public class RequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly FirmwareCaller User = FirmwareCaller.StandardUser;
    private static readonly FirmwareCaller Admin = FirmwareCaller.Administrator;

    private readonly FakeProvider _provider = new(
        policy: new FirmwarePolicy(readable: ["Product Name"], writable: ["Battery Health Manager"], adminWritable: ["Asset Tracking"]),
        settings:
        [
            new("Battery Health Manager", FirmwareSettingKind.Enumeration, "Maximize", ["Maximize", "Minimize"], false),
            new("Asset Tracking", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
            new("Product Name", FirmwareSettingKind.String, "EliteBook", [], true),
            new("Secure Boot", FirmwareSettingKind.Enumeration, "Enable", ["Disable", "Enable"], false),
        ]);

    private bool _lockdown;

    private RequestHandler CreateHandler(IFirmwareProvider? provider = null) => new(
        NullLogger<RequestHandler>.Instance,
        new FirmwareProviderResolver([provider ?? _provider]),
        () => _lockdown);

    [Fact]
    public async Task GetStatus_ReportsVendorCallerAndLockdown()
    {
        _lockdown = true;

        var response = await CreateHandler().HandleAsync(new(IpcCommand.GetStatus), Admin, "test", Ct);

        Assert.Equal(new ServiceStatus("Fake", IsAdministrator: true, RequireAdminForAllChanges: true), response.Status);
    }

    [Fact]
    public async Task GetSettings_ReturnsAllowlistedSettingsWithCallerAccess()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct);

        Assert.Equal(
            [
                ("Battery Health Manager", FirmwareChangeAccess.Allowed),
                ("Asset Tracking", FirmwareChangeAccess.AdministratorRequired),
                ("Product Name", FirmwareChangeAccess.ReadOnly),
            ],
            response.Settings!.Select(s => (s.Setting.Name, s.Access)));
    }

    [Fact]
    public async Task GetSettings_IncludesReportedChargeLimit()
    {
        _provider.ReportedChargeLimit = 0.8;

        var response = await CreateHandler().HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct);

        Assert.Equal(0.8, response.ReportedChargeLimit);
    }

    [Fact]
    public async Task SetSetting_ReturnsReadBackSetting()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", "Minimize"), User, "test", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Equal("Minimize", response.Setting!.Setting.CurrentValue);
    }

    [Fact]
    public async Task SetSetting_AdminOnlySetting_FromStandardUser_IsAdministratorRequired()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, "Asset Tracking", "Disable"), User, "test", Ct);

        Assert.Equal(IpcError.AdministratorRequired, response.Error);
        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task SetSetting_Lockdown_AppliesToUserWritableSettings()
    {
        _lockdown = true;

        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", "Minimize"), User, "test", Ct);

        Assert.Equal(IpcError.AdministratorRequired, response.Error);
    }

    [Fact]
    public async Task SetSetting_OutsideAllowlist_IsNotAllowed()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, "Secure Boot", "Disable"), Admin, "test", Ct);

        Assert.Equal(IpcError.NotAllowed, response.Error);
        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task SetSetting_WithoutValue_IsBadRequest()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager"), User, "test", Ct);

        Assert.Equal(IpcError.BadRequest, response.Error);
    }

    [Fact]
    public async Task SetSetting_PassesBiosPasswordToFirmware()
    {
        await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", "Minimize", "pw"), User, "test", Ct);

        Assert.Equal("pw", Assert.Single(_provider.Writes).Password);
    }

    [Fact]
    public async Task UnsupportedMachine_ReportsNoVendor_AndRefusesSettings()
    {
        var handler = CreateHandler(new FakeProvider(supported: false));

        Assert.Null((await handler.HandleAsync(new(IpcCommand.GetStatus), User, "test", Ct)).Status!.Vendor);
        Assert.Equal(IpcError.Unsupported, (await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).Error);
    }
}
