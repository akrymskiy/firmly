using System.Security.AccessControl;
using System.Security.Principal;

namespace Firmly.Service;

/// <summary>
/// The service's folder for its own files, %ProgramData%\Firmly. Standard users can create folders under ProgramData,
/// so the folder must be one only SYSTEM and administrators control: else a user could plant a file there, or redirect
/// the service's writes through a link.
/// </summary>
internal static class TrustedFolder
{
    /// <summary>%ProgramData%\Firmly.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Firmly");

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>Creates the folder holding <paramref name="file"/> if needed, then checks it can be trusted.</summary>
    /// <param name="requireTrusted">False for tests, which use a temporary folder.</param>
    /// <exception cref="IOException">The folder isn't safe to trust.</exception>
    public static void Prepare(string file, bool requireTrusted)
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(file)!);
        if (!folder.Exists)
            Create(folder, requireTrusted);
        EnsureTrusted(file, requireTrusted);
    }

    /// <summary>Checks the folder holding <paramref name="file"/> can be trusted.</summary>
    /// <exception cref="IOException">The folder isn't safe to trust.</exception>
    public static void EnsureTrusted(string file, bool requireTrusted)
    {
        if (!requireTrusted)
            return;

        var folder = new DirectoryInfo(Path.GetDirectoryName(file)!);
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"'{folder.FullName}' is a link; refusing to use it.");

        var owner = folder.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (owner != LocalSystem && owner != Administrators)
            throw new IOException($"'{folder.FullName}' is owned by {owner}, not SYSTEM or Administrators; refusing to use it.");
    }

    /// <summary>Writes a copy and swaps it in, so a crash can't leave half a file.</summary>
    public static void WriteAtomically(string file, byte[] content)
    {
        var temp = file + ".tmp";
        File.WriteAllBytes(temp, content);
        File.Move(temp, file, overwrite: true);
    }

    private static void Create(DirectoryInfo folder, bool requireTrusted)
    {
        if (!requireTrusted)
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
}
