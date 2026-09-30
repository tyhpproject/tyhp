using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class DbgpResponseTests
{
    public const string SampleBreakpointSetResponseXml =
        """
        <?xml version="1.0" encoding="iso-8859-1"?>
        <response xmlns="urn:debugger_protocol_v1" command="breakpoint_set" transaction_id="1" status="OK" id="12000"/>
        """;

    public const string SampleInitXml =
        """
        <?xml version="1.0" encoding="iso-8859-1"?>
        <init xmlns="urn:debugger_protocol_v1" fileuri="file:///project/build/index.php" language="PHP" protocol_version="1.0" appid="123" idekey="TYHP">
          <engine version="3.3.0"><![CDATA[Xdebug]]></engine>
        </init>
        """;

    public const string SampleStackGetXml =
        """
        <?xml version="1.0" encoding="iso-8859-1"?>
        <response xmlns="urn:debugger_protocol_v1" command="stack_get" transaction_id="3" status="break">
          <stack where="App\Calculator->add" level="0" type="file" filename="file:///project/build/index.php" lineno="67"/>
          <stack where="{main}" level="1" type="file" filename="file:///project/build/index.php" lineno="10"/>
        </response>
        """;

    [Fact]
    public void ParseResponse_SampleXml_ExposesStatusCommandAndAttributes()
    {
        DbgpResponse response = DbgpMessageParser.ParseResponse(Frame(SampleBreakpointSetResponseXml));

        response.IsInit.Should().BeFalse();
        response.IsResponseElement.Should().BeTrue();
        response.Command.Should().Be(DbgpConstants.Commands.BreakpointSet);
        response.TransactionId.Should().Be("1");
        response.Status.Should().Be("OK");
        response.GetAttribute("id").Should().Be("12000");
        response.RootElement.Name.NamespaceName.Should().Be(DbgpConstants.XmlNamespaceUri);
    }

    [Fact]
    public void ParseResponse_InitPacket_SetsIsInitAndReadsNamespacedChildren()
    {
        DbgpResponse response = DbgpMessageParser.ParseResponse(Frame(SampleInitXml));

        response.IsInit.Should().BeTrue();
        response.IsResponseElement.Should().BeFalse();
        response.Command.Should().BeNull();
        response.TransactionId.Should().BeNull();
        response.GetAttribute("fileuri").Should().Be("file:///project/build/index.php");
        response.GetAttribute("idekey").Should().Be("TYHP");

        XElement? engine = response.GetChild("engine");
        engine.Should().NotBeNull();
        engine!.Name.NamespaceName.Should().Be(DbgpConstants.XmlNamespaceUri);
        engine.Value.Should().Be("Xdebug");
        engine.Attribute("version")!.Value.Should().Be("3.3.0");
        response.GetChildValue("engine").Should().Be("Xdebug");
    }

    [Fact]
    public void ParseResponse_StackGet_ReturnsNamespacedChildren()
    {
        DbgpResponse response = DbgpMessageParser.ParseResponse(Frame(SampleStackGetXml));

        response.Command.Should().Be(DbgpConstants.Commands.StackGet);
        response.Status.Should().Be(DbgpConstants.Status.Break);
        List<XElement> frames = response.GetChildren("stack").ToList();
        frames.Should().HaveCount(2);
        frames[0].Attribute("lineno")!.Value.Should().Be("67");
        frames[1].Attribute("filename")!.Value.Should().Be("file:///project/build/index.php");
        response.GetChildAttribute("stack", "where").Should().Be(@"App\Calculator->add");
    }

    [Fact]
    public void SerializeResponse_ModifiedXml_WritesLengthNulXmlNul()
    {
        DbgpResponse response = DbgpMessageParser.ParseResponse(Frame(SampleBreakpointSetResponseXml));
        response.Status = "break";
        response.SetAttribute("id", "999");

        byte[] wire = DbgpMessageParser.SerializeResponse(response);

        wire.Should().Contain(DbgpConstants.NullByte);
        wire[^1].Should().Be(DbgpConstants.NullByte);

        int firstNull = Array.IndexOf(wire, DbgpConstants.NullByte);
        firstNull.Should().BeGreaterThan(0);
        int xmlLength = int.Parse(DbgpMessageParser.Utf8.GetString(wire, 0, firstNull));
        xmlLength.Should().Be(wire.Length - firstNull - 2);
        wire[firstNull + 1 + xmlLength].Should().Be(DbgpConstants.NullByte);

        DbgpResponse parsed = DbgpMessageParser.ParseResponse(wire);
        parsed.Status.Should().Be("break");
        parsed.GetAttribute("id").Should().Be("999");
        parsed.TransactionId.Should().Be("1");
        parsed.Command.Should().Be("breakpoint_set");
    }

    [Fact]
    public void SerializeResponse_InitWithUtf8FileUri_PreservesMultibyteCharacters()
    {
        DbgpResponse response = DbgpMessageParser.ParseResponse(Frame(SampleInitXml));
        response.SetAttribute("fileuri", "file:///project/src/日本語.tyhp");

        byte[] wire = DbgpMessageParser.SerializeResponse(response);
        DbgpResponse parsed = DbgpMessageParser.ParseResponse(wire);

        parsed.IsInit.Should().BeTrue();
        parsed.GetAttribute("fileuri").Should().Be("file:///project/src/日本語.tyhp");
        parsed.GetChildValue("engine").Should().Be("Xdebug");
    }

    [Fact]
    public void ParseResponse_MissingLengthNull_ThrowsDbgpProtocolException()
    {
        byte[] raw = DbgpMessageParser.Utf8.GetBytes("not-a-response");
        Action act = () => DbgpMessageParser.ParseResponse(raw);
        act.Should().Throw<DbgpProtocolException>().WithMessage("*length NUL*");
    }

    [Fact]
    public void ParseResponse_LengthMismatch_ThrowsDbgpProtocolException()
    {
        byte[] xml = DbgpMessageParser.Utf8.GetBytes("<response/>");
        byte[] raw = FrameWithLength(xml, declaredLength: xml.Length + 10);
        Action act = () => DbgpMessageParser.ParseResponse(raw);
        act.Should().Throw<DbgpProtocolException>().WithMessage("*truncated*");
    }

    [Fact]
    public void ParseResponse_MissingTrailingNull_ThrowsDbgpProtocolException()
    {
        byte[] xml = DbgpMessageParser.Utf8.GetBytes("<response xmlns=\"urn:debugger_protocol_v1\"/>");
        string length = xml.Length.ToString();
        byte[] lengthBytes = DbgpMessageParser.Utf8.GetBytes(length);
        var raw = new byte[lengthBytes.Length + 1 + xml.Length];
        Buffer.BlockCopy(lengthBytes, 0, raw, 0, lengthBytes.Length);
        raw[lengthBytes.Length] = 0;
        Buffer.BlockCopy(xml, 0, raw, lengthBytes.Length + 1, xml.Length);

        Action act = () => DbgpMessageParser.ParseResponse(raw);
        act.Should().Throw<DbgpProtocolException>().WithMessage("*trailing NUL*");
    }

    [Fact]
    public void ParseResponse_MalformedXml_ThrowsDbgpProtocolException()
    {
        byte[] raw = Frame("<response><unclosed>");
        Action act = () => DbgpMessageParser.ParseResponse(raw);
        act.Should().Throw<DbgpProtocolException>().WithMessage("*malformed*");
    }

    [Fact]
    public void ParseResponse_ExtraTrailingBytesAfterFramedMessage_ConsumesOnlyFirstMessage()
    {
        byte[] first = Frame(SampleBreakpointSetResponseXml);
        byte[] second = Frame(SampleInitXml);
        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);

        DbgpResponse response = DbgpMessageParser.ParseResponse(combined);

        response.IsInit.Should().BeFalse();
        response.Command.Should().Be(DbgpConstants.Commands.BreakpointSet);
    }

    [Fact]
    public void ParseResponse_NonNumericLength_ThrowsDbgpProtocolException()
    {
        byte[] raw = DbgpMessageParser.Utf8.GetBytes("abc\0<response/>\0");
        Action act = () => DbgpMessageParser.ParseResponse(raw);
        act.Should().Throw<DbgpProtocolException>().WithMessage("*length prefix*");
    }

    [Fact]
    public void DbgpMessage_FromResponse_IsXDebugToIde()
    {
        DbgpResponse response = DbgpMessageParser.ParseResponse(Frame(SampleInitXml));
        DbgpMessage message = DbgpMessage.FromResponse(response);

        message.IsResponse.Should().BeTrue();
        message.IsCommand.Should().BeFalse();
        message.Direction.Should().Be(DbgpMessageDirection.XDebugToIde);
        message.Response.Should().BeSameAs(response);
        message.Command.Should().BeNull();
    }

    internal static byte[] Frame(string xml)
    {
        return FrameWithLength(DbgpMessageParser.Utf8.GetBytes(xml), declaredLength: null);
    }

    private static byte[] FrameWithLength(byte[] xmlBytes, int? declaredLength)
    {
        int length = declaredLength ?? xmlBytes.Length;
        byte[] lengthBytes = DbgpMessageParser.Utf8.GetBytes(length.ToString());
        var framed = new byte[lengthBytes.Length + 1 + xmlBytes.Length + 1];
        Buffer.BlockCopy(lengthBytes, 0, framed, 0, lengthBytes.Length);
        framed[lengthBytes.Length] = DbgpConstants.NullByte;
        Buffer.BlockCopy(xmlBytes, 0, framed, lengthBytes.Length + 1, xmlBytes.Length);
        framed[^1] = DbgpConstants.NullByte;
        return framed;
    }
}
