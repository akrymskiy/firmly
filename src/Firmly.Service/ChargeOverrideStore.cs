using System.Text.Json;
using System.Text.Json.Serialization;
using Firmly.Ipc;

namespace Firmly.Service;

/// <summary>A charge override as the service keeps it: <see cref="ChargeOverride"/> plus who chose it.</summary>
/// <param name="ProtectedBiosPassword">
/// The BIOS password to switch back with, encrypted (<see cref="IPasswordProtector"/>); only when the user chose so.
/// </param>
internal sealed record StoredChargeOverride(
    string ModeId, string RestoreModeId, DateTimeOffset Until, string SetBy, string? ProtectedBiosPassword = null)
{
    public ChargeOverride ToIpc(bool waitingForPassword) =>
        new(ModeId, RestoreModeId, Until, ProtectedBiosPassword is not null, waitingForPassword);

    // Never print even the encrypted password.
    public override string ToString() => $"{ModeId} until {Until:u}, then {RestoreModeId}, set by {SetBy}";
}

/// <summary>Keeps the charge override in a file, so the switch back still happens after a restart or reboot.</summary>
/// <param name="requireTrustedFolder">See <see cref="TrustedFolder"/>; false for tests.</param>
internal sealed class ChargeOverrideStore(string path, bool requireTrustedFolder = true)
{
    /// <summary>%ProgramData%\Firmly\charge-override.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(TrustedFolder.DefaultPath, "charge-override.json");

    /// <returns>Null when no override is stored.</returns>
    /// <exception cref="IOException">The file can't be read, or isn't safe to trust.</exception>
    /// <exception cref="JsonException">The file is damaged.</exception>
    public StoredChargeOverride? Load()
    {
        if (!File.Exists(path))
            return null;

        TrustedFolder.EnsureTrusted(path, requireTrustedFolder);
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), StoreJsonContext.Default.StoredChargeOverride);
    }

    /// <param name="value">Null removes the stored override.</param>
    /// <exception cref="IOException">The file can't be written, or the folder isn't safe to trust.</exception>
    public void Save(StoredChargeOverride? value)
    {
        TrustedFolder.Prepare(path, requireTrustedFolder);

        if (value is null)
        {
            File.Delete(path);
            return;
        }

        TrustedFolder.WriteAtomically(path, JsonSerializer.SerializeToUtf8Bytes(value, StoreJsonContext.Default.StoredChargeOverride));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(StoredChargeOverride))]
[JsonSerializable(typeof(StoredPasswordFailures))]
internal sealed partial class StoreJsonContext : JsonSerializerContext
{
}
