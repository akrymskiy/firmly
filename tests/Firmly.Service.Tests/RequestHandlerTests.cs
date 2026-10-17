using Firmly.Core;
using Firmly.Core.Tests;
using Firmly.Ipc;

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

    private readonly ManualTimeProvider _time = new();
    private readonly ListLogger<RequestHandler> _logger = new();
    private bool _lockdown;

    private RequestHandler CreateHandler(IFirmwareProvider? provider = null) => new(
        _logger,
        new FirmwareProviderResolver([provider ?? _provider]),
        () => _lockdown,
        _time);

    [Fact]
    public async Task GetStatus_ReportsVendorCallerAndLockdown()
    {
        _lockdown = true;

        var status = (await CreateHandler().HandleAsync(new(IpcCommand.GetStatus), Admin, "test", Ct)).Status!;

        Assert.Equal(("Fake", true, true), (status.Vendor, status.IsAdministrator, status.RequireAdminForAllChanges));
    }

    [Fact]
    public async Task GetStatus_ReportsVersionsAndFeatures()
    {
        var status = (await CreateHandler().HandleAsync(new(IpcCommand.GetStatus), User, "test", Ct)).Status!;

        Assert.Equal(IpcConstants.ProtocolVersion, status.ProtocolVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+", status.ServiceVersion);
        Assert.Equal([IpcFeatures.Firmware], status.Features!);
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

    [Fact]
    public async Task Flood_IsRefusedPerCaller_AndRecovers()
    {
        var handler = CreateHandler();
        for (var i = 0; i < RequestHandler.RequestBurst; i++)
            Assert.Equal(IpcError.None, (await handler.HandleAsync(new(IpcCommand.GetStatus), User, "flooder", Ct)).Error);

        var refused = await handler.HandleAsync(new(IpcCommand.GetSettings), User, "flooder", Ct);
        Assert.Equal(IpcError.TooManyRequests, refused.Error);
        Assert.Equal(IpcError.None, (await handler.HandleAsync(new(IpcCommand.GetStatus), User, "someone else", Ct)).Error);

        _time.Advance(RequestHandler.RequestInterval);
        Assert.Equal(IpcError.None, (await handler.HandleAsync(new(IpcCommand.GetStatus), User, "flooder", Ct)).Error);
    }

    [Fact]
    public async Task RepeatedRefusals_AreLoggedOncePerMinute()
    {
        var handler = CreateHandler();

        for (var i = 0; i < 100; i++)
        {
            await handler.HandleAsync(new(IpcCommand.SetSetting, "Secure Boot", "Disable"), Admin, "looper", Ct);
            _time.Advance(RequestHandler.RequestInterval);
        }

        // 100 refusals over 20 seconds, plus the flood warning never firing: one line.
        Assert.Single(_logger.Warnings);

        _time.Advance(TimeSpan.FromMinutes(1));
        await handler.HandleAsync(new(IpcCommand.SetSetting, "Secure Boot", "Disable"), Admin, "looper", Ct);
        Assert.Contains("Repeated 99 times", _logger.Warnings[^1]);
    }

    [Fact]
    public async Task FloodWarning_IsLoggedOncePerMinute()
    {
        var handler = CreateHandler();

        for (var i = 0; i < RequestHandler.RequestBurst + 500; i++)
            await handler.HandleAsync(new(IpcCommand.GetStatus), User, "flooder", Ct);

        Assert.Contains("flooder", Assert.Single(_logger.Warnings));
    }

    [Fact]
    public async Task FirmwareWrites_AreCappedMachineWide()
    {
        var handler = CreateHandler();
        string[] values = ["Minimize", "Maximize"];

        for (var i = 0; i < WriteLimitedProvider.Burst; i++)
        {
            var response = await handler.HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", values[i % 2]), User, $"user{i}", Ct);
            Assert.Equal(IpcError.None, response.Error);
        }

        var refused = await handler.HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", "Minimize"), User, "another user", Ct);

        Assert.Equal(IpcError.TooManyRequests, refused.Error);
        Assert.Contains("30 seconds", refused.Message);
        Assert.Equal(WriteLimitedProvider.Burst, _provider.Writes.Count);

        _time.Advance(WriteLimitedProvider.Interval);
        Assert.Equal(IpcError.None, (await handler.HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", "Minimize"), User, "x", Ct)).Error);
    }

    [Fact]
    public async Task SettingToCurrentValue_DoesNotUseWriteBudget()
    {
        var handler = CreateHandler();

        for (var i = 0; i < WriteLimitedProvider.Burst + 5; i++)
            Assert.Equal(IpcError.None, (await handler.HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", "Maximize"), User, "test", Ct)).Error);

        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task SetSetting_OverlongName_IsBadRequest()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetSetting, new string('x', 10_000), "v"), User, "test", Ct);

        Assert.Equal(IpcError.BadRequest, response.Error);
        Assert.Empty(_logger.Warnings);
    }
}