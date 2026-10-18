using System.Text.Json;

namespace Firmly.Service;

/// <param name="Count">Wrong BIOS passwords in a row.</param>
/// <param name="BootTime">When Windows started, to tell this boot from an earlier one.</param>
internal sealed record StoredPasswordFailures(int Count, DateTimeOffset BootTime);

/// <summary>
/// Keeps the count of wrong BIOS passwords in a row for this boot, so restarting the service doesn't forget that the
/// firmware is close to locking, or locked. A restart of the PC clears the firmware's count, and so this one.
/// </summary>
/// <param name="bootTime">When Windows started; <see cref="CurrentBootTime"/> outside tests.</param>
/// <param name="requireTrustedFolder">See <see cref="TrustedFolder"/>; false for tests.</param>
internal sealed class PasswordFailureStore(string path, DateTimeOffset bootTime, bool requireTrustedFolder = true)
{
    /// <summary>%ProgramData%\Firmly\bios-password-failures.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(TrustedFolder.DefaultPath, "bios-password-failures.json");

    /// <summary>When Windows started, give or take the clock being adjusted since.</summary>
    public static DateTimeOffset CurrentBootTime => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    // The boot time is worked out from the clock, which may be adjusted a little meanwhile. Two boots are always further
    // apart than this: the first one lasted at least as long as it took to get the password wrong.
    private static readonly TimeSpan SameBootTolerance = TimeSpan.FromSeconds(30);

    /// <returns>The count for this boot; 0 when none is stored, or it's from an earlier boot.</returns>
    /// <exception cref="IOException">The file can't be read, or isn't safe to trust.</exception>
    /// <exception cref="JsonException">The file is damaged.</exception>
    public int Load()
    {
        if (!File.Exists(path))
            return 0;

        TrustedFolder.EnsureTrusted(path, requireTrustedFolder);
        var stored = JsonSerializer.Deserialize(File.ReadAllBytes(path), StoreJsonContext.Default.StoredPasswordFailures);
        return stored is not null && (stored.BootTime - bootTime).Duration() < SameBootTolerance ? stored.Count : 0;
    }

    /// <param name="count">0 removes the file.</param>
    /// <exception cref="IOException">The file can't be written, or the folder isn't safe to trust.</exception>
    public void Save(int count)
    {
        TrustedFolder.Prepare(path, requireTrustedFolder);

        if (count == 0)
        {
            File.Delete(path);
            return;
        }

        TrustedFolder.WriteAtomically(path,
            JsonSerializer.SerializeToUtf8Bytes(new StoredPasswordFailures(count, bootTime), StoreJsonContext.Default.StoredPasswordFailures));
    }
}
