namespace Firmly.Core;

/// <summary>The firmware rejected a setting change.</summary>
/// <param name="vendorCode">Raw status code returned by the vendor interface.</param>
/// <param name="passwordRejected">The BIOS administrator password was missing or wrong.</param>
public sealed class FirmwareWriteException(string message, uint vendorCode, bool passwordRejected = false) : Exception(message)
{
    public uint VendorCode { get; } = vendorCode;

    /// <summary>The BIOS administrator password was missing or wrong; trying again with the right one may work.</summary>
    public bool PasswordRejected { get; } = passwordRejected;
}
