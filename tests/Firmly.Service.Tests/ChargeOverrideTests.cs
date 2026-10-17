using System.Security.Principal;
using Firmly.Core;
using Firmly.Core.Tests;
using Firmly.Ipc;

namespace Firmly.Service.Tests;

public sealed class ChargeOverrideTests : IDisposable
{
    private const string Manage = "Let HP Manage My Battery Health";
    private const string Maximize = "Maximize Battery Health Management";
    private const string Minimize = "Minimize Battery Health Management";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly FirmwareCaller User = FirmwareCaller.StandardUser;

    private readonly FakeProvider _provider = new(
        policy: new FirmwarePolicy(readable: [], writable: ["Battery Health Manager"]),
        settings: [new("Battery Health Manager", FirmwareSettingKind.Enumeration, Maximize, [Maximize, Manage, Minimize], false)])
    {
        ChargeModeSetting = new("Battery Health Manager",
        [
            new(Manage, "manage", ChargeModeKind.Adaptive),
            new(Maximize, "maximize", ChargeModeKind.Limited, 80),
            new(Minimize, "minimize", ChargeModeKind.Full),
        ]),
    };

    private readonly ManualTimeProvider _time = new();
    private readonly ListLogger<RequestHandler> _logger = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"firmly-test-{Guid.NewGuid():N}");
    private bool _lockdown;

    private string StorePath => Path.Combine(_folder, "charge-override.json");

    private RequestHandler CreateHandler() => new(
        _logger,
        new FirmwareProviderResolver([_provider]),
        () => _lockdown,
        _time,
        new ChargeOverrideStore(StorePath, requireTrustedFolder: false));

    private string CurrentValue => _provider.GetSettings()[0].CurrentValue;

    private static IpcRequest Override(string mode, string restore, DateTimeOffset until) =>
        new(IpcCommand.SetChargeOverride, mode, restore, Until: until);

    private static DateTimeOffset In(TimeSpan span) => ManualTimeProvider.Start + span;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task GetSettings_IncludesChargeModes_AndAccess()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct);

        Assert.Equal(["manage", "maximize", "minimize"], response.ChargeModes!.Modes.Select(m => m.Id));
        Assert.Equal("maximize", response.ChargeModes.CurrentId);
        Assert.Equal(FirmwareChangeAccess.Allowed, response.ChargeModeAccess);
        Assert.Null(response.ChargeOverride);
    }

    [Fact]
    public async Task SetChargeMode_Switches_AndAnswersWithTheNewState()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetChargeMode, "minimize"), User, "test", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Equal("minimize", response.ChargeModes!.CurrentId);
        Assert.Equal(Minimize, CurrentValue);
    }

    [Fact]
    public async Task SetChargeMode_UnknownMode_IsNotAllowed()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.SetChargeMode, "turbo"), User, "test", Ct);

        Assert.Equal(IpcError.NotAllowed, response.Error);
        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task SetChargeOverride_SwitchesNow_AndRemembersTheWayBack()
    {
        var until = In(TimeSpan.FromDays(3));

        var response = await CreateHandler().HandleAsync(Override("minimize", "maximize", until), User, "traveller", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Equal(Minimize, CurrentValue);
        Assert.Equal(new ChargeOverride("minimize", "maximize", until), response.ChargeOverride);
        Assert.Contains("traveller", File.ReadAllText(StorePath));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(32 * 24 * 60)]
    public async Task SetChargeOverride_EndOutsideTheNextMonth_IsBadRequest(int minutes)
    {
        var response = await CreateHandler().HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromMinutes(minutes))), User, "test", Ct);

        Assert.Equal(IpcError.BadRequest, response.Error);
        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task SetChargeOverride_BackToTheSameMode_IsBadRequest()
    {
        var response = await CreateHandler().HandleAsync(Override("minimize", "minimize", In(TimeSpan.FromDays(1))), User, "test", Ct);

        Assert.Equal(IpcError.BadRequest, response.Error);
    }

    [Fact]
    public async Task SetChargeOverride_UnknownModeToReturnTo_IsRefusedBeforeWriting()
    {
        var response = await CreateHandler().HandleAsync(Override("minimize", "turbo", In(TimeSpan.FromDays(1))), User, "test", Ct);

        Assert.Equal(IpcError.NotAllowed, response.Error);
        Assert.Empty(_provider.Writes);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task SetChargeOverride_UnderLockdown_NeedsAnAdministrator()
    {
        _lockdown = true;

        var response = await CreateHandler().HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);

        Assert.Equal(IpcError.AdministratorRequired, response.Error);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task Check_SwitchesBackOnlyOnceTheOverrideEnds()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(3))), User, "test", Ct);

        _time.Advance(TimeSpan.FromDays(3) - TimeSpan.FromMinutes(1));
        await handler.CheckChargeOverrideAsync(Ct);
        Assert.Equal(Minimize, CurrentValue);

        _time.Advance(TimeSpan.FromMinutes(1));
        await handler.CheckChargeOverrideAsync(Ct);
        Assert.Equal(Maximize, CurrentValue);
        Assert.Null((await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task Override_SurvivesAServiceRestart()
    {
        await CreateHandler().HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);

        _time.Advance(TimeSpan.FromDays(2));
        var restarted = CreateHandler();
        await restarted.CheckChargeOverrideAsync(Ct);

        Assert.Equal(Maximize, CurrentValue);
    }

    [Fact]
    public async Task ChoosingAnotherMode_EndsTheOverride()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);

        var response = await handler.HandleAsync(new(IpcCommand.SetChargeMode, "manage"), User, "test", Ct);
        _time.Advance(TimeSpan.FromDays(2));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Null(response.ChargeOverride);
        Assert.Equal(Manage, CurrentValue);
    }

    [Fact]
    public async Task ChangingTheRawSetting_EndsTheOverride_UnlessTheModeStaysTheSame()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);

        await handler.HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", Minimize), User, "test", Ct);
        Assert.NotNull((await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride);

        await handler.HandleAsync(new(IpcCommand.SetSetting, "Battery Health Manager", Manage), User, "test", Ct);
        Assert.Null((await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride);
    }

    [Fact]
    public async Task ModeChangedOutsideFirmly_EndsTheOverride_WithoutWriting()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);
        _provider.SetSetting("Battery Health Manager", Manage, null);
        _provider.Writes.Clear();

        _time.Advance(TimeSpan.FromDays(2));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Empty(_provider.Writes);
        Assert.Equal(Manage, CurrentValue);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task Cancel_SwitchesBackNow()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);

        var response = await handler.HandleAsync(new(IpcCommand.CancelChargeOverride), User, "test", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Equal("maximize", response.ChargeModes!.CurrentId);
        Assert.Null(response.ChargeOverride);
    }

    [Fact]
    public async Task Cancel_WithoutAnOverride_ChangesNothing()
    {
        var response = await CreateHandler().HandleAsync(new(IpcCommand.CancelChargeOverride), User, "test", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task FailedSwitchBack_KeepsTheOverride_AndRetriesLater()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override("minimize", "maximize", In(TimeSpan.FromDays(1))), User, "test", Ct);
        _provider.IgnoreWrites = true;
        _provider.Writes.Clear();

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);
        Assert.Single(_provider.Writes);
        Assert.Contains(_logger.Warnings, w => w.Contains("Couldn't switch the battery back"));

        // Not again until the retry interval has passed.
        _time.Advance(RequestHandler.RevertRetryInterval - TimeSpan.FromSeconds(1));
        await handler.CheckChargeOverrideAsync(Ct);
        Assert.Single(_provider.Writes);

        _provider.IgnoreWrites = false;
        _time.Advance(TimeSpan.FromSeconds(1));
        await handler.CheckChargeOverrideAsync(Ct);
        Assert.Equal(Maximize, CurrentValue);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task DamagedStoreFile_IsIgnored_AndLogged()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StorePath, "{ not json");

        var response = await CreateHandler().HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Null(response.ChargeOverride);
        Assert.Contains(_logger.Warnings, w => w.Contains("Couldn't read the saved charge override"));
    }

    [Fact]
    public void Store_RefusesAFolderOwnedByAnOrdinaryUser()
    {
        // Owned by the account running the tests, as a folder a user made under ProgramData would be. Set explicitly:
        // elevated on Windows Server (as in CI), new folders belong to Administrators instead.
        var folder = Directory.CreateDirectory(_folder);
        var security = folder.GetAccessControl();
        security.SetOwner(WindowsIdentity.GetCurrent().User!);
        folder.SetAccessControl(security);
        File.WriteAllText(StorePath, "{}");

        var store = new ChargeOverrideStore(StorePath);

        Assert.Throws<IOException>(() => store.Load());
        Assert.Throws<IOException>(() => store.Save(null));
    }
}
