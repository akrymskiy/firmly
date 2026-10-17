using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Firmly.Ipc;

namespace Firmly.Service;

/// <summary>A charge override as the service keeps it: <see cref="ChargeOverride"/> plus who chose it.</summary>
internal sealed record StoredChargeOverride(string ModeId, string RestoreModeId, DateTimeOffset Until, string SetBy)
{
    public ChargeOverride ToIpc() => new(ModeId, RestoreModeId, Until);
}

/// <summary>Keeps the charge override in a file, so the switch back still happens after a restart or reboot.</summary>
/// <param name="requireTrustedFolder">
/// Standard users can create folders under ProgramData. When true, the folder must be one only SYSTEM and
/// administrators control, so a user can't plant the file or redirect the service's writes through a link.
/// </param>
internal sealed class ChargeOverrideStore(string path, bool requireTrustedFolder = true)
{
    /// <summary>%ProgramData%\Firmly\charge-override.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Firmly", "charge-override.json");

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <returns>Null when no override is stored.</returns>
    /// <exception cref="IOException">The file can't be read, or isn't safe to trust.</exception>
    /// <exception cref="JsonException">The file is damaged.</exception>
    public StoredChargeOverride? Load()
    {
        if (!File.Exists(path))
            return null;

        EnsureTrusted(new DirectoryInfo(Folder));
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), StoreJsonContext.Default.StoredChargeOverride);
    }

    /// <param name="value">Null removes the stored override.</param>
    /// <exception cref="IOException">The file can't be written, or the folder isn't safe to trust.</exception>
    public void Save(StoredChargeOverride? value)
    {
        var folder = new DirectoryInfo(Folder);
        if (!folder.Exists)
            CreateFolder(folder);
        EnsureTrusted(folder);

        if (value is null)
        {
            File.Delete(path);
            return;
        }

        // Write a copy and swap it in, so a crash can't leave half a file.
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, StoreJsonContext.Default.StoredChargeOverride));
        File.Move(temp, path, overwrite: true);
    }

    private string Folder => Path.GetDirectoryName(path)!;

    private void CreateFolder(DirectoryInfo folder)
    {
        if (!requireTrustedFolder)
        {
            folder.Create();
            return;
        }

        // Only SYSTEM and administrators, and nothing inherited from ProgramData (where users may create files).
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        foreach (var sid in new[] { LocalSystem, Administrators })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        folder.Create(security);
    }

    private void EnsureTrusted(DirectoryInfo folder)
    {
        if (!requireTrustedFolder)
            return;

        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"'{folder.FullName}' is a link; refusing to use it.");

        var owner = folder.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (owner != LocalSystem && owner != Administrators)
            throw new IOException($"'{folder.FullName}' is owned by {owner}, not SYSTEM or Administrators; refusing to use it.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(StoredChargeOverride))]
internal sealed partial class StoreJsonContext : JsonSerializerContext
{
}
