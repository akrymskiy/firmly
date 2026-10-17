using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using Windows.Security.Credentials;

namespace Firmly.App;

/// <summary>
/// Keeps the BIOS password for this Windows user, unlocked with Windows Hello (face, fingerprint or PIN).
/// <para>
/// The password is encrypted (AES-GCM) with a key derived from a Windows Hello signature over a random challenge. The
/// signing key never leaves the device's TPM, and Hello signs only after verifying the user, so without that check the
/// key can't be recreated: not by other users, other apps, or from a copy of the files. The signature is the same
/// every time (RSA PKCS#1 v1.5), which is what makes it usable as a key.
/// </para>
/// </summary>
internal static class BiosPasswordVault
{
    // Windows Hello keys are per app and user; the name only has to be unique within Firmly.
    private const string KeyName = "Firmly BIOS password";
    private const string SettingName = "BiosPasswordVault";
    private static readonly byte[] Purpose = "Firmly BIOS password key"u8.ToArray();
    private const int SaltSize = 32, NonceSize = 12, TagSize = 16;

    private static Windows.Foundation.Collections.IPropertySet Values =>
        Windows.Storage.ApplicationData.Current.LocalSettings.Values;

    /// <summary>A password is saved (it may still fail to unlock, e.g. after Windows Hello was reset).</summary>
    public static bool HasSaved => Values[SettingName] is string;

    /// <summary>Raised after saving or forgetting.</summary>
    public static event Action? Changed;

    /// <summary>Whether Windows Hello is set up for this user, so a password can be saved.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    /// <summary>Saves <paramref name="password"/>; Windows Hello asks the user to confirm.</summary>
    /// <returns>False when the user cancelled or Windows Hello isn't available.</returns>
    public static async Task<bool> SaveAsync(string password)
    {
        try
        {
            var created = await KeyCredentialManager.RequestCreateAsync(KeyName, KeyCredentialCreationOption.ReplaceExisting);
            if (created.Status != KeyCredentialStatus.Success)
                return false;

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            if (await DeriveKeyAsync(created.Credential, salt) is not { } key)
                return false;

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plain = Encoding.UTF8.GetBytes(password);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);

            Values[SettingName] = Convert.ToBase64String([.. salt, .. nonce, .. tag, .. cipher]);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    /// <summary>Unlocks the saved password; Windows Hello asks the user to confirm.</summary>
    /// <returns>Null when there's none, the user cancelled, or it can't be unlocked any more (then it's forgotten).</returns>
    public static async Task<string?> UnlockAsync()
    {
        if (Values[SettingName] is not string saved)
            return null;

        try
        {
            var opened = await KeyCredentialManager.OpenAsync(KeyName);
            if (opened.Status == KeyCredentialStatus.NotFound)
            {
                // Windows Hello was reset, or the key removed: what's saved can never be unlocked.
                await ForgetAsync();
                return null;
            }
            if (opened.Status != KeyCredentialStatus.Success)
                return null;

            var data = Convert.FromBase64String(saved);
            var salt = data.AsSpan(0, SaltSize).ToArray();
            if (await DeriveKeyAsync(opened.Credential, salt) is not { } key)
                return null;

            var nonce = data.AsSpan(SaltSize, NonceSize);
            var tag = data.AsSpan(SaltSize + NonceSize, TagSize);
            var cipher = data.AsSpan(SaltSize + NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);
            CryptographicOperations.ZeroMemory(key);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Damaged, or from another Windows Hello key: useless now.
            App.LogError(ex);
            await ForgetAsync();
            return null;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return null;
        }
    }

    /// <summary>Removes the saved password and its Windows Hello key.</summary>
    public static async Task ForgetAsync()
    {
        Values.Remove(SettingName);
        try
        {
            await KeyCredentialManager.DeleteAsync(KeyName);
        }
        catch (Exception ex)
        {
            // Already gone; nothing is left to unlock either way.
            System.Diagnostics.Debug.WriteLine(ex);
        }
        Changed?.Invoke();
    }

    // The signature over the salt, run through HKDF: an AES key that only this user's Windows Hello can produce.
    private static async Task<byte[]?> DeriveKeyAsync(KeyCredential credential, byte[] salt)
    {
        var signed = await credential.RequestSignAsync(salt.AsBuffer());
        if (signed.Status != KeyCredentialStatus.Success)
            return null;

        var signature = signed.Result.ToArray();
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, 32, salt, Purpose);
        CryptographicOperations.ZeroMemory(signature);
        return key;
    }
}
