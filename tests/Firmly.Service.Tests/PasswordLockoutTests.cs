using Firmly.Core;
using Firmly.Core.Tests;
using Firmly.Ipc;

namespace Firmly.Service.Tests;

public sealed class PasswordLockoutTests : IDisposable
{
    private const string Maximize = "Maximize Battery Health Management";
    private const string Minimize = "Minimize Battery Health Management";
    private const string Password = "s3cret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly FirmwareCaller User = FirmwareCaller.StandardUser;
    private static readonly DateTimeOffset Boot = new(2026, 10, 17, 19, 0, 0, TimeSpan.Zero);

    private readonly FakeProvider _provider = new(
        policy: new FirmwarePolicy(readable: [], writable: ["Battery Health Manager"]),
        settings: [new("Battery Health Manager", FirmwareSettingKind.Enumeration, Maximize, [Maximize, Minimize], false)])
    {
        ChargeModeSetting = new("Battery Health Manager",
        [
            new(Maximize, "maximize", ChargeModeKind.Limited, 80),
            new(Minimize, "minimize", ChargeModeKind.Full),
        ]),
        BiosPassword = Password,
        PasswordLockout = new(MaxConsecutiveFailures: 3),
    };

    private readonly ManualTimeProvider _time = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"firmly-test-{Guid.NewGuid():N}");

    private string CurrentValue => _provider.GetSettings()[0].CurrentValue;

    private RequestHandler CreateHandler(DateTimeOffset? boot = null) => new(
        new ListLogger<RequestHandler>(),
        new FirmwareProviderResolver([_provider]),
        () => false,
        _time,
        new ChargeOverrideStore(Path.Combine(_folder, "charge-override.json"), requireTrustedFolder: false),
        passwordFailures: new PasswordFailureStore(Path.Combine(_folder, "failures.json"), boot ?? Boot, requireTrustedFolder: false));

    private static Task<IpcResponse> Choose(RequestHandler handler, string mode, string? password) =>
        handler.HandleAsync(new(IpcCommand.SetChargeMode, mode, BiosPassword: password), User, "test", Ct);

    private static async Task<int?> TriesLeft(RequestHandler handler) =>
        (await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).BiosPasswordTriesLeft;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Status_ListsTheFeature()
    {
        var status = (await CreateHandler().HandleAsync(new(IpcCommand.GetStatus), User, "test", Ct)).Status!;
        Assert.Contains(IpcFeatures.BiosPasswordLockout, status.Features!);
    }

    [Fact]
    public async Task WrongPasswords_CountDown()
    {
        var handler = CreateHandler();
        Assert.Equal(3, await TriesLeft(handler));

        var first = await Choose(handler, "minimize", "wrong");
        var second = await Choose(handler, "minimize", "wrong");

        Assert.Equal(IpcError.BiosPasswordRequired, first.Error);
        Assert.Equal(2, first.BiosPasswordTriesLeft);
        Assert.Equal(1, second.BiosPasswordTriesLeft);
        Assert.Equal(1, await TriesLeft(handler));
    }

    [Fact]
    public async Task AtTheLimit_NothingIsSent_EvenTheRightPassword()
    {
        var handler = CreateHandler();
        for (var i = 0; i < 3; i++)
            await Choose(handler, "minimize", "wrong");
        var writes = _provider.Writes.Count;

        var response = await Choose(handler, "minimize", Password);

        Assert.Equal(IpcError.BiosPasswordRequired, response.Error);
        Assert.Equal(0, response.BiosPasswordTriesLeft);
        Assert.Equal(writes, _provider.Writes.Count);
        Assert.Equal(Maximize, CurrentValue);
    }

    [Fact]
    public async Task TheRightPassword_StartsTheCountOver()
    {
        var handler = CreateHandler();
        await Choose(handler, "minimize", "wrong");
        await Choose(handler, "minimize", "wrong");

        Assert.Equal(IpcError.None, (await Choose(handler, "minimize", Password)).Error);
        Assert.Equal(3, await TriesLeft(handler));

        // As measured on HP: two wrong, the right one, two wrong, the right one: never locked.
        await Choose(handler, "maximize", "wrong");
        await Choose(handler, "maximize", "wrong");
        Assert.Equal(IpcError.None, (await Choose(handler, "maximize", Password)).Error);
        Assert.Equal(Maximize, CurrentValue);
    }

    [Fact]
    public async Task NoPassword_WhileOneIsSet_IsRefusedWithoutSending_OrCounting()
    {
        var handler = CreateHandler();

        var response = await Choose(handler, "minimize", null);

        Assert.Equal(IpcError.BiosPasswordRequired, response.Error);
        Assert.Equal(3, response.BiosPasswordTriesLeft);
        Assert.Empty(_provider.Writes);
    }

    [Fact]
    public async Task TheCount_SurvivesAServiceRestart_ButNotARestartOfThePC()
    {
        var handler = CreateHandler();
        await Choose(handler, "minimize", "wrong");
        await Choose(handler, "minimize", "wrong");

        Assert.Equal(1, await TriesLeft(CreateHandler(Boot + TimeSpan.FromSeconds(2))));
        Assert.Equal(3, await TriesLeft(CreateHandler(Boot + TimeSpan.FromMinutes(10))));
    }

    [Fact]
    public async Task Locked_TheSwitchBack_WaitsWithoutSending()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(new(IpcCommand.SetChargeOverride, "minimize", "maximize", Password,
            ManualTimeProvider.Start + TimeSpan.FromDays(1), KeepBiosPassword: true), User, "test", Ct);
        for (var i = 0; i < 3; i++)
            await Choose(handler, "maximize", "wrong");
        var writes = _provider.Writes.Count;

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Equal(writes, _provider.Writes.Count);
        Assert.Equal(Minimize, CurrentValue);
        Assert.True((await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride!.KeepsBiosPassword);
    }

    [Fact]
    public async Task FirmwareWithoutAKnownLockout_IsNotCounted()
    {
        _provider.PasswordLockout = null;
        var handler = CreateHandler();

        for (var i = 0; i < 4; i++)
            Assert.Null((await Choose(handler, "minimize", i == 0 ? null : "wrong")).BiosPasswordTriesLeft);

        Assert.Equal(4, _provider.Writes.Count);
        Assert.Equal(IpcError.None, (await Choose(handler, "minimize", Password)).Error);
        Assert.Null(await TriesLeft(handler));
    }
}
