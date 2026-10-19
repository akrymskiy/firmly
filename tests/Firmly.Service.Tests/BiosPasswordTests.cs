using System.Security.Cryptography;
using Firmly.Core;
using Firmly.Core.Tests;
using Firmly.Ipc;

namespace Firmly.Service.Tests;

public sealed class BiosPasswordTests : IDisposable
{
    private const string Maximize = "Maximize Battery Health Management";
    private const string Minimize = "Minimize Battery Health Management";
    private const string Password = "s3cret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly FirmwareCaller User = FirmwareCaller.StandardUser;

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
    };

    private readonly ManualTimeProvider _time = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"firmly-test-{Guid.NewGuid():N}");
    private bool _allowSaved = true;

    private string StorePath => Path.Combine(_folder, "charge-override.json");

    private string CurrentValue => _provider.GetSettings()[0].CurrentValue;

    private RequestHandler CreateHandler() => new(
        new ListLogger<RequestHandler>(),
        new FirmwareProviderResolver([_provider]),
        () => false,
        _time,
        new ChargeOverrideStore(StorePath, requireTrustedFolder: false),
        new ReversingProtector(),
        () => _allowSaved);

    private static IpcRequest Override(string? password, bool keep = false) => new(
        IpcCommand.SetChargeOverride, "minimize", "maximize", password, ManualTimeProvider.Start + TimeSpan.FromDays(1), keep);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Status_ListsTheFeature_AndThePolicy()
    {
        _allowSaved = false;
        var status = (await CreateHandler().HandleAsync(new(IpcCommand.GetStatus), User, "test", Ct)).Status!;

        Assert.Contains(IpcFeatures.BiosPassword, status.Features!);
        Assert.False(status.AllowSavedBiosPassword);
    }

    [Fact]
    public async Task Snapshot_SaysAPasswordIsSet()
    {
        Assert.True((await CreateHandler().HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).BiosPasswordSet);

        _provider.BiosPassword = null;
        Assert.False((await CreateHandler().HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).BiosPasswordSet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Change_WithoutTheRightPassword_AsksForIt(string? password)
    {
        var response = await CreateHandler().HandleAsync(
            new(IpcCommand.SetChargeMode, "minimize", BiosPassword: password), User, "test", Ct);

        Assert.Equal(IpcError.BiosPasswordRequired, response.Error);
        Assert.Equal(Maximize, CurrentValue);
    }

    [Fact]
    public async Task Change_WithThePassword_Works()
    {
        var response = await CreateHandler().HandleAsync(
            new(IpcCommand.SetChargeMode, "minimize", BiosPassword: Password), User, "test", Ct);

        Assert.Equal(IpcError.None, response.Error);
        Assert.Equal(Minimize, CurrentValue);
    }

    [Fact]
    public async Task OverrideEnds_WithoutAKeptPassword_WaitsForOne_WithoutTrying()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override(Password), User, "test", Ct);
        var writes = _provider.Writes.Count;

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Equal(writes, _provider.Writes.Count);
        Assert.Equal(Minimize, CurrentValue);
        var waiting = (await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride!;
        Assert.True(waiting.WaitingForPassword);
        Assert.False(waiting.KeepsBiosPassword);

        // "Switch back now" with the password.
        var response = await handler.HandleAsync(new(IpcCommand.CancelChargeOverride, BiosPassword: Password), User, "test", Ct);
        Assert.Equal(IpcError.None, response.Error);
        Assert.Null(response.ChargeOverride);
        Assert.Equal(Maximize, CurrentValue);
        // Whoever typed the password knows: nothing for the app to report.
        Assert.Null(response.LastOverrideEnd);
    }

    [Fact]
    public async Task OverrideEnds_WithAKeptPassword_SwitchesBackAlone_ThenForgetsIt()
    {
        var handler = CreateHandler();
        var started = await handler.HandleAsync(Override(Password, keep: true), User, "test", Ct);
        Assert.True(started.ChargeOverride!.KeepsBiosPassword);
        var stored = File.ReadAllText(StorePath);
        Assert.DoesNotContain(Password, stored);
        Assert.Contains(ReversingProtector.Reverse(Password), stored);

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Equal(Maximize, CurrentValue);
        Assert.Equal(Password, _provider.Writes[^1].Password);
        Assert.False(File.Exists(StorePath));
        var ended = (await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).LastOverrideEnd;
        Assert.Equal(new ChargeOverrideEnd("minimize", "maximize", ManualTimeProvider.Start + TimeSpan.FromDays(1)), ended);
    }

    [Fact]
    public async Task KeptPassword_RemovedInTheBiosSince_SwitchesBackWithoutIt()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override(Password, keep: true), User, "test", Ct);
        _provider.BiosPassword = null;

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Equal(Maximize, CurrentValue);
        Assert.Null(_provider.Writes[^1].Password);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task OverrideStartedWithoutAPassword_SetInTheBiosSince_WaitsForIt_WithoutTrying()
    {
        _provider.BiosPassword = null;
        var handler = CreateHandler();
        await handler.HandleAsync(Override(null), User, "test", Ct);
        _provider.BiosPassword = Password;
        var writes = _provider.Writes.Count;

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Equal(writes, _provider.Writes.Count);
        Assert.Equal(Minimize, CurrentValue);
        Assert.True((await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride!.WaitingForPassword);

        // "Switch back now", once Firmly has asked for the password.
        var response = await handler.HandleAsync(new(IpcCommand.CancelChargeOverride, BiosPassword: Password), User, "test", Ct);
        Assert.Equal(IpcError.None, response.Error);
        Assert.Equal(Maximize, CurrentValue);
    }

    [Fact]
    public async Task KeptPasswordThatStoppedWorking_IsForgotten_AndTriedOnce()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override(Password, keep: true), User, "test", Ct);
        _provider.BiosPassword = "changed in the BIOS setup";
        var writes = _provider.Writes.Count;

        _time.Advance(TimeSpan.FromDays(1));
        await handler.CheckChargeOverrideAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        await handler.CheckChargeOverrideAsync(Ct);

        Assert.Equal(writes + 1, _provider.Writes.Count);
        var waiting = (await handler.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride!;
        Assert.True(waiting.WaitingForPassword);
        Assert.False(waiting.KeepsBiosPassword);
        Assert.DoesNotContain(ReversingProtector.Reverse(Password), File.ReadAllText(StorePath));
    }

    [Fact]
    public async Task KeepingThePassword_ForbiddenByPolicy_IsRefusedBeforeWriting()
    {
        _allowSaved = false;
        var response = await CreateHandler().HandleAsync(Override(Password, keep: true), User, "test", Ct);

        Assert.Equal(IpcError.NotAllowed, response.Error);
        Assert.Empty(_provider.Writes);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task KeepingAWrongPassword_KeepsNothing()
    {
        var response = await CreateHandler().HandleAsync(Override("wrong", keep: true), User, "test", Ct);

        Assert.Equal(IpcError.BiosPasswordRequired, response.Error);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task KeptPasswordThatCantBeDecrypted_WaitsForOne()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Override(Password, keep: true), User, "test", Ct);
        File.WriteAllText(StorePath, File.ReadAllText(StorePath).Replace(ReversingProtector.Reverse(Password), "damaged"));
        var restarted = CreateHandler();

        _time.Advance(TimeSpan.FromDays(1));
        await restarted.CheckChargeOverrideAsync(Ct);

        Assert.Equal(Minimize, CurrentValue);
        Assert.True((await restarted.HandleAsync(new(IpcCommand.GetSettings), User, "test", Ct)).ChargeOverride!.WaitingForPassword);
    }

    // Stands in for DPAPI: reversible, and visibly not the password itself.
    private sealed class ReversingProtector : IPasswordProtector
    {
        public static string Reverse(string text) => "enc:" + new string(text.Reverse().ToArray());

        public string Protect(string password) => Reverse(password);

        public string Unprotect(string protectedPassword) =>
            protectedPassword.StartsWith("enc:", StringComparison.Ordinal)
                ? new string(protectedPassword[4..].Reverse().ToArray())
                : throw new CryptographicException("Not ours.");
    }
}
