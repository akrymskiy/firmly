namespace Firmly.Core;

/// <summary>The firmware rejected a setting change.</summary>
/// <param name="vendorCode">Raw status code returned by the vendor interface.</param>
public sealed class FirmwareWriteException(string message, uint vendorCode) : Exception(message)
{
    public uint VendorCode { get; } = vendorCode;
}
