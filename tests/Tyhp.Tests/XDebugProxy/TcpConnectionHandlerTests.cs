using System.Text;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.Network;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class TcpConnectionHandlerTests
{
    [Fact]
    public async Task ReadWriteCommand_RoundTripsThroughMemoryStream()
    {
        using var stream = new MemoryStream();
        var handler = new TcpConnectionHandler(stream);
        DbgpCommand original = DbgpMessageParser.ParseCommand("run -i 7");

        await handler.WriteCommandAsync(original);
        stream.Position = 0;

        DbgpCommand read = await handler.ReadCommandAsync();

        read.CommandName.Should().Be("run");
        read.TransactionId.Should().Be("7");
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public async Task ReadWriteResponse_RoundTripsInitPacket()
    {
        using var stream = new MemoryStream();
        var handler = new TcpConnectionHandler(stream);
        DbgpResponse original = DbgpMessageParser.ParseResponse(
            DbgpResponseTests.Frame(DbgpResponseTests.SampleInitXml));

        await handler.WriteResponseAsync(original);
        stream.Position = 0;

        DbgpResponse read = await handler.ReadResponseAsync();

        read.IsInit.Should().BeTrue();
        read.GetAttribute("idekey").Should().Be("TYHP");
        read.GetAttribute("fileuri").Should().Be("file:///project/build/index.php");
    }

    [Fact]
    public async Task StaticHelpers_MatchInstanceMethods()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand("status -i 3");
        using var stream = new MemoryStream();

        await TcpConnectionHandler.WriteCommandAsync(stream, command);
        stream.Position = 0;
        DbgpCommand read = await TcpConnectionHandler.ReadCommandAsync(stream);

        read.CommandName.Should().Be("status");
        read.TransactionId.Should().Be("3");
    }

    [Fact]
    public async Task WriteCommand_UsesConfiguredBufferSize_AndStillParses()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 200));
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"eval -i 1 -- {Convert.ToBase64String(payload)}");
        using var stream = new MemoryStream();
        var handler = new TcpConnectionHandler(stream, bufferSize: 16);

        await handler.WriteCommandAsync(command);
        stream.Position = 0;

        DbgpCommand read = await handler.ReadCommandAsync();
        read.Data.Should().Equal(payload);
    }

    [Fact]
    public void DefaultBufferSize_Is8192()
    {
        TcpConnectionHandler.DefaultBufferSize.Should().Be(8192);
        using var stream = new MemoryStream();
        var handler = new TcpConnectionHandler(stream);
        handler.BufferSize.Should().Be(8192);
    }
}
