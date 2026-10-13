using Firmly.Core;

namespace Firmly.Core.Tests;

public class FirmwareProviderResolverTests
{
    [Fact]
    public void Resolve_ReturnsFirstSupportedProvider()
    {
        var unsupported = new FakeProvider("A", supported: false);
        var supported = new FakeProvider("B", supported: true);
        var alsoSupported = new FakeProvider("C", supported: true);

        var resolver = new FirmwareProviderResolver([unsupported, supported, alsoSupported]);

        Assert.Same(supported, resolver.Resolve());
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNoProviderIsSupported()
    {
        var resolver = new FirmwareProviderResolver([new FakeProvider("A", supported: false)]);

        Assert.Null(resolver.Resolve());
    }
}
