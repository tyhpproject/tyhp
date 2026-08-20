using System.Text;
using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class DbgpMessageParserTests
{
    [Fact]
    public void Command_ParseSerializeParse_IsIdentical()
    {
        const string original =
            "breakpoint_set -i 1 -t line -f file:///project/src/App.tyhp -n 42";
        DbgpCommand first = DbgpMessageParser.ParseCommand(original);
        byte[] wire = DbgpMessageParser.SerializeCommand(first);
        DbgpCommand second = DbgpMessageParser.ParseCommand(wire);

        AssertCommandsEqual(first, second);
        wire[^1].Should().Be(DbgpConstants.NullByte);
    }

    [Fact]
    public void CommandWithDataAndUtf8_ParseSerializeParse_IsIdentical()
    {
        byte[] payload = Encoding.UTF8.GetBytes("echo '日本語';");
        string text =
            $"eval -i 12 -f file:///tmp/日本語.tyhp -- {Convert.ToBase64String(payload)}";

        DbgpCommand first = DbgpMessageParser.ParseCommand(text);
        DbgpCommand second = DbgpMessageParser.ParseCommand(DbgpMessageParser.SerializeCommand(first));

        AssertCommandsEqual(first, second);
        second.Data.Should().Equal(payload);
        second.Filename.Should().Be("file:///tmp/日本語.tyhp");
    }

    [Fact]
    public void CommandQuotedSpaces_ParseSerializeParse_PreservesFilename()
    {
        const string original =
            """breakpoint_set -i 4 -t line -f "file:///project/src/My File.tyhp" -n 8""";
        DbgpCommand first = DbgpMessageParser.ParseCommand(original);
        DbgpCommand second = DbgpMessageParser.ParseCommand(DbgpMessageParser.SerializeCommand(first));

        AssertCommandsEqual(first, second);
        second.Filename.Should().Be("file:///project/src/My File.tyhp");
    }

    [Fact]
    public void Response_ParseSerializeParse_IsIdentical()
    {
        DbgpResponse first = DbgpMessageParser.ParseResponse(
            DbgpResponseTests.Frame(DbgpResponseTests.SampleBreakpointSetResponseXml));
        byte[] wire = DbgpMessageParser.SerializeResponse(first);
        DbgpResponse second = DbgpMessageParser.ParseResponse(wire);

        AssertResponsesEqual(first, second);
        wire[^1].Should().Be(DbgpConstants.NullByte);
    }

    [Fact]
    public void Init_ParseSerializeParse_IsIdentical()
    {
        DbgpResponse first = DbgpMessageParser.ParseResponse(
            DbgpResponseTests.Frame(DbgpResponseTests.SampleInitXml));
        DbgpResponse second = DbgpMessageParser.ParseResponse(DbgpMessageParser.SerializeResponse(first));

        AssertResponsesEqual(first, second);
        second.IsInit.Should().BeTrue();
        second.GetChildValue("engine").Should().Be("Xdebug");
    }

    [Fact]
    public void StackGet_ParseSerializeParse_PreservesChildren()
    {
        DbgpResponse first = DbgpMessageParser.ParseResponse(
            DbgpResponseTests.Frame(DbgpResponseTests.SampleStackGetXml));
        first.GetChildren("stack").ElementAt(0).SetAttributeValue("filename", "file:///project/src/App.tyhp");
        first.GetChildren("stack").ElementAt(0).SetAttributeValue("lineno", "42");

        DbgpResponse second = DbgpMessageParser.ParseResponse(DbgpMessageParser.SerializeResponse(first));

        AssertResponsesEqual(first, second);
        second.GetChildren("stack").ElementAt(0).Attribute("filename")!.Value
            .Should().Be("file:///project/src/App.tyhp");
        second.GetChildren("stack").ElementAt(0).Attribute("lineno")!.Value.Should().Be("42");
    }

    [Fact]
    public async Task ReadCommandFromStream_MemoryStreamWithFullMessage_Parses()
    {
        byte[] wire = DbgpMessageParser.SerializeCommand(
            DbgpMessageParser.ParseCommand("run -i 7"));
        using var stream = new MemoryStream(wire);

        DbgpCommand command = await DbgpMessageParser.ReadCommandFromStream(stream);

        command.CommandName.Should().Be("run");
        command.TransactionId.Should().Be("7");
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public async Task ReadCommandFromStream_ChunkedReads_DoesNotOverReadNextMessage()
    {
        byte[] first = DbgpMessageParser.SerializeCommand(
            DbgpMessageParser.ParseCommand("run -i 1"));
        byte[] second = DbgpMessageParser.SerializeCommand(
            DbgpMessageParser.ParseCommand("stop -i 2"));
        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);

        using var stream = new ChunkedReadStream(combined, chunkSize: 1);

        DbgpCommand a = await DbgpMessageParser.ReadCommandFromStream(stream);
        DbgpCommand b = await DbgpMessageParser.ReadCommandFromStream(stream);

        a.CommandName.Should().Be("run");
        a.TransactionId.Should().Be("1");
        b.CommandName.Should().Be("stop");
        b.TransactionId.Should().Be("2");
    }

    [Fact]
    public async Task ReadResponseFromStream_MemoryStreamWithFullMessage_Parses()
    {
        byte[] wire = DbgpResponseTests.Frame(DbgpResponseTests.SampleBreakpointSetResponseXml);
        using var stream = new MemoryStream(wire);

        DbgpResponse response = await DbgpMessageParser.ReadResponseFromStream(stream);

        response.Command.Should().Be("breakpoint_set");
        response.TransactionId.Should().Be("1");
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public async Task ReadResponseFromStream_PartialWrites_ParsesWhenComplete()
    {
        byte[] wire = DbgpResponseTests.Frame(DbgpResponseTests.SampleInitXml);
        using var stream = new ChunkedReadStream(wire, chunkSize: 3);

        DbgpResponse response = await DbgpMessageParser.ReadResponseFromStream(stream);

        response.IsInit.Should().BeTrue();
        response.GetAttribute("idekey").Should().Be("TYHP");
        response.GetChildValue("engine").Should().Be("Xdebug");
    }

    [Fact]
    public async Task ReadCommandFromStream_UnexpectedEof_ThrowsDbgpProtocolException()
    {
        using var stream = new MemoryStream(DbgpMessageParser.Utf8.GetBytes("run -i 1"));
        Func<Task> act = () => DbgpMessageParser.ReadCommandFromStream(stream);
        await act.Should().ThrowAsync<DbgpProtocolException>().WithMessage("*end of stream*");
    }

    [Fact]
    public async Task ReadResponseFromStream_UnexpectedEofDuringXml_ThrowsDbgpProtocolException()
    {
        byte[] xml = DbgpMessageParser.Utf8.GetBytes("<response/>");
        byte[] lengthBytes = DbgpMessageParser.Utf8.GetBytes(xml.Length.ToString());
        var truncated = new byte[lengthBytes.Length + 1 + 3];
        Buffer.BlockCopy(lengthBytes, 0, truncated, 0, lengthBytes.Length);
        truncated[lengthBytes.Length] = 0;
        Buffer.BlockCopy(xml, 0, truncated, lengthBytes.Length + 1, 3);

        using var stream = new MemoryStream(truncated);
        Func<Task> act = () => DbgpMessageParser.ReadResponseFromStream(stream);
        await act.Should().ThrowAsync<DbgpProtocolException>().WithMessage("*end of stream*");
    }

    [Fact]
    public async Task ReadCommandFromStream_CancelledToken_ThrowsOperationCanceledException()
    {
        using var stream = new MemoryStream(DbgpMessageParser.SerializeCommand(
            DbgpMessageParser.ParseCommand("status -i 1")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => DbgpMessageParser.ReadCommandFromStream(stream, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ParseCommand_NullBytes_ThrowsArgumentNullException()
    {
        Action act = () => DbgpMessageParser.ParseCommand((byte[])null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static void AssertCommandsEqual(DbgpCommand expected, DbgpCommand actual)
    {
        actual.CommandName.Should().Be(expected.CommandName);
        actual.TransactionId.Should().Be(expected.TransactionId);
        actual.Arguments.Should().Equal(expected.Arguments);
        if (expected.Data is null)
        {
            actual.Data.Should().BeNull();
        }
        else
        {
            actual.Data.Should().Equal(expected.Data);
        }
    }

    private static void AssertResponsesEqual(DbgpResponse expected, DbgpResponse actual)
    {
        actual.IsInit.Should().Be(expected.IsInit);
        actual.RootLocalName.Should().Be(expected.RootLocalName);
        actual.TransactionId.Should().Be(expected.TransactionId);
        actual.Command.Should().Be(expected.Command);
        actual.Status.Should().Be(expected.Status);
        actual.RootElement.Name.Should().Be(expected.RootElement.Name);

        foreach (XAttribute attribute in expected.RootElement.Attributes())
        {
            actual.GetAttribute(attribute.Name.LocalName).Should().Be(attribute.Value);
        }

        List<XElement> expectedChildren = expected.RootElement.Elements().ToList();
        List<XElement> actualChildren = actual.RootElement.Elements().ToList();
        actualChildren.Should().HaveCount(expectedChildren.Count);
        for (int i = 0; i < expectedChildren.Count; i++)
        {
            actualChildren[i].Name.LocalName.Should().Be(expectedChildren[i].Name.LocalName);
            actualChildren[i].Value.Should().Be(expectedChildren[i].Value);
            foreach (XAttribute attribute in expectedChildren[i].Attributes())
            {
                actualChildren[i].Attribute(attribute.Name)!.Value.Should().Be(attribute.Value);
            }
        }
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _position;

        public ChunkedReadStream(byte[] data, int chunkSize)
        {
            this._data = data;
            this._chunkSize = chunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => this._data.Length;

        public override long Position
        {
            get => this._position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this._position >= this._data.Length)
            {
                return 0;
            }

            int toCopy = Math.Min(count, Math.Min(this._chunkSize, this._data.Length - this._position));
            Buffer.BlockCopy(this._data, this._position, buffer, offset, toCopy);
            this._position += toCopy;
            return toCopy;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
