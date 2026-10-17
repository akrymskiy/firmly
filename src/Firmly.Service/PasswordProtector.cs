using System.Security.Cryptography;
using System.Text;

namespace Firmly.Service;

/// <summary>Encrypts the BIOS password the service keeps while a charge override runs.</summary>
internal interface IPasswordProtector
{
    string Protect(string password);

    /// <exception cref="CryptographicException">It can't be decrypted, e.g. it comes from another PC.</exception>
    string Unprotect(string protectedPassword);
}

/// <summary>
/// Windows DPAPI under the service's own account, LocalSystem: only code running as LocalSystem on this PC can decrypt
/// it, not other users, and not the same file copied elsewhere.
/// </summary>
internal sealed class DpapiPasswordProtector : IPasswordProtector
{
    private static readonly byte[] Purpose = "Firmly BIOS password"u8.ToArray();

    public string Protect(string password) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Purpose, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedPassword)
    {
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), Purpose, DataProtectionScope.CurrentUser));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The kept BIOS password is damaged.", ex);
        }
    }
}
