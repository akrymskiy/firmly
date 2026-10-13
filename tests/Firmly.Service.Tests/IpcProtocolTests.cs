using System.Buffers.Binary;
using System.Text;
using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.Service.Tests;

public class IpcProtocolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Request_RoundTrips()
    {
        var stream = new MemoryStream();
        var request = new IpcRequest(IpcCommand.SetSetting, "Battery Health Manager", "Maximize", "pw");

        await IpcProtocol.WriteRequestAsync(stream, request, Ct);
        stream.Position = 0;

        Assert.Equal(request, await IpcProtocol.ReadRequestAsync(stream, Ct));
    }

    [Fact]
    public async Task Response_WithSettings_RoundTrips()
    {
        var stream = new MemoryStream();
        var setting = new FirmwareSetting("Battery Health Manager", FirmwareSettingKind.Enumeration, "Maximize", ["Maximize", "Minimize"], false);

        await IpcProtocol.WriteResponseAsync(stream, new IpcResponse(Settings: [new SettingInfo(setting, FirmwareChangeAccess.Allowed)]), Ct);
        stream.Position = 0;
        var result = Assert.Single((await IpcProtocol.ReadResponseAsync(stream, Ct)).Settings!);

        Assert.Equal(FirmwareChangeAccess.Allowed, result.Access);
        Assert.Equal(setting.Name, result.Setting.Name);
        Assert.Equal(setting.PossibleValues, result.Setting.PossibleValues);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(IpcProtocol.MaxMessageBytes + 1)]
    public async Task Read_RejectsInvalidLength(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);

        await Assert.ThrowsAsync<InvalidDataException>(() => IpcProtocol.ReadRequestAsync(new MemoryStream(header), Ct));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"command\":\"FormatDisk\"}")]
    [InlineData("null")]
    public async Task Read_RejectsMalformedJson(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(body.Length));
        stream.Write(body);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => IpcProtocol.ReadRequestAsync(stream, Ct));
    }

    [Fact]
    public void Request_ToString_OmitsPassword()
    {
        Assert.DoesNotContain("secret", new IpcRequest(IpcCommand.SetSetting, "X", "Y", "secret").ToString());
    }
}
