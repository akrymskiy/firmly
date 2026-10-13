namespace Firmly.Core;

/// <summary>Picks the first registered provider that supports the current machine.</summary>
public sealed class FirmwareProviderResolver(IEnumerable<IFirmwareProvider> providers)
{
    public IFirmwareProvider? Resolve() => providers.FirstOrDefault(p => p.IsSupported());
}
