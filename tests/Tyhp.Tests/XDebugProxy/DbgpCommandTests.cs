using System.Text;
using Tyhp.XDebugProxy.Dbgp;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class DbgpCommandTests
{
    private const string SampleBreakpointSet =
        "breakpoint_set -i 1 -t line -f file:///project/src/App.tyhp -n 42";

    [Fact]
    public void ParseCommand_BreakpointSet_ExtractsFilenameLineAndTransactionId()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand(SampleBreakpointSet);

        command.CommandName.Should().Be(DbgpConstants.Commands.BreakpointSet);
        command.TransactionId.Should().Be("1");
        command.Filename.Should().Be("file:///project/src/App.tyhp");
        command.LineNumber.Should().Be("42");
        command.GetArgument("-t").Should().Be(DbgpConstants.BreakpointTypes.Line);
        command.Arguments.Should().NotContainKey("-i");
        command.Data.Should().BeNull();
        command.RawText.Should().Be(SampleBreakpointSet);
    }

    [Fact]
    public void ParseCommand_ByteArrayWithTrailingNull_StripsTerminator()
    {
        byte[] raw = DbgpMessageParser.Utf8.GetBytes(SampleBreakpointSet + "\0");
        DbgpCommand command = DbgpMessageParser.ParseCommand(raw);

        command.TransactionId.Should().Be("1");
        command.Filename.Should().Be("file:///project/src/App.tyhp");
        command.RawText.Should().Be(SampleBreakpointSet);
    }

    [Fact]
    public void SerializeCommand_ModifiedBreakpointSet_WritesValidWireFormat()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand(SampleBreakpointSet);
        command.Filename = "file:///project/build/App.php";
        command.LineNumber = "67";

        byte[] wire = DbgpMessageParser.SerializeCommand(command);

        wire.Should().EndWith(new[] { DbgpConstants.NullByte });
        DbgpCommand roundTrip = DbgpMessageParser.ParseCommand(wire);
        roundTrip.CommandName.Should().Be(DbgpConstants.Commands.BreakpointSet);
        roundTrip.TransactionId.Should().Be("1");
        roundTrip.Filename.Should().Be("file:///project/build/App.php");
        roundTrip.LineNumber.Should().Be("67");
        roundTrip.GetArgument("-t").Should().Be("line");
    }

    [Fact]
    public void ParseCommand_QuotedFilenameWithSpaces_UnquotesValue()
    {
        const string text =
            """breakpoint_set -i 2 -t line -f "file:///project/src/My File.tyhp" -n 10""";

        DbgpCommand command = DbgpMessageParser.ParseCommand(text);

        command.Filename.Should().Be("file:///project/src/My File.tyhp");
        command.LineNumber.Should().Be("10");
    }

    [Fact]
    public void ParseCommand_UnquotedFilenameWithSpaces_ConsumesUntilNextFlag()
    {
        const string text =
            "breakpoint_set -i 2 -t line -f file:///project/src/My File.tyhp -n 10";

        DbgpCommand command = DbgpMessageParser.ParseCommand(text);

        command.Filename.Should().Be("file:///project/src/My File.tyhp");
        command.LineNumber.Should().Be("10");
    }

    [Fact]
    public void ParseCommand_EscapedQuotesInsideQuotedValue_Decodes()
    {
        const string text = "eval -i 3 -f \"path/with \\\"quotes\\\".tyhp\"";

        DbgpCommand command = DbgpMessageParser.ParseCommand(text);

        command.Filename.Should().Be("path/with \"quotes\".tyhp");
    }

    [Fact]
    public void ParseCommand_MultibyteUtf8Filename_PreservesCharacters()
    {
        const string text =
            "breakpoint_set -i 9 -t line -f file:///project/src/日本語.tyhp -n 3";

        DbgpCommand command = DbgpMessageParser.ParseCommand(text);
        byte[] wire = DbgpMessageParser.SerializeCommand(command);
        DbgpCommand roundTrip = DbgpMessageParser.ParseCommand(wire);

        command.Filename.Should().Be("file:///project/src/日本語.tyhp");
        roundTrip.Filename.Should().Be(command.Filename);
        Encoding.UTF8.GetByteCount(command.Filename!).Should().BeGreaterThan(command.Filename!.Length);
    }

    [Fact]
    public void ParseCommand_Base64DataAfterDashDash_DecodesPayload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("$x = 1;");
        string base64 = Convert.ToBase64String(payload);
        string text = $"eval -i 5 -- {base64}";

        DbgpCommand command = DbgpMessageParser.ParseCommand(text);

        command.CommandName.Should().Be(DbgpConstants.Commands.Eval);
        command.TransactionId.Should().Be("5");
        command.Data.Should().Equal(payload);
    }

    [Fact]
    public void SerializeCommand_WithData_EmitsBase64AfterSeparator()
    {
        byte[] payload = Encoding.UTF8.GetBytes("property-value");
        var command = new DbgpCommand(DbgpConstants.Commands.PropertySet, "8", data: payload);

        byte[] wire = DbgpMessageParser.SerializeCommand(command);
        string text = DbgpMessageParser.Utf8.GetString(wire).TrimEnd('\0');

        text.Should().StartWith("property_set -i 8 -- ");
        text.Should().EndWith(Convert.ToBase64String(payload));
        DbgpMessageParser.ParseCommand(wire).Data.Should().Equal(payload);
    }

    [Fact]
    public void ParseCommand_EmbeddedNullInString_IsTrimmedAsTerminator()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand("status -i 4\0");
        command.CommandName.Should().Be(DbgpConstants.Commands.Status);
        command.TransactionId.Should().Be("4");
    }

    [Fact]
    public void ParseCommand_Empty_ThrowsDbgpProtocolException()
    {
        Action act = () => DbgpMessageParser.ParseCommand("");
        act.Should().Throw<DbgpProtocolException>().WithMessage("*empty*");
    }

    [Fact]
    public void ParseCommand_UnterminatedQuote_ThrowsDbgpProtocolException()
    {
        Action act = () => DbgpMessageParser.ParseCommand("""breakpoint_set -i 1 -f "unterminated""");
        act.Should().Throw<DbgpProtocolException>().WithMessage("*unterminated quoted*");
    }

    [Fact]
    public void ParseCommand_InvalidBase64_ThrowsDbgpProtocolException()
    {
        Action act = () => DbgpMessageParser.ParseCommand("eval -i 1 -- not_valid_base64!");
        act.Should().Throw<DbgpProtocolException>().WithMessage("*base64*");
    }

    [Fact]
    public void ParseCommand_UnexpectedToken_ThrowsDbgpProtocolException()
    {
        Action act = () => DbgpMessageParser.ParseCommand("run leftover");
        act.Should().Throw<DbgpProtocolException>().WithMessage("*Unexpected token*");
    }

    [Fact]
    public void GetArgument_TransactionIdFlag_ReturnsTransactionIdProperty()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand("run -i 5");

        command.GetArgument("-i").Should().Be("5");
        command.GetArgument("i").Should().Be("5");
    }

    [Fact]
    public void SetArgument_TransactionIdFlag_UpdatesTransactionIdNotArguments()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand("run -i 5");

        command.SetArgument("-i", "999");

        command.TransactionId.Should().Be("999");
        command.Arguments.Should().NotContainKey("-i");

        byte[] wire = DbgpMessageParser.SerializeCommand(command);
        DbgpMessageParser.ParseCommand(wire).TransactionId.Should().Be("999");
    }

    [Fact]
    public void Constructor_ArgumentsDictionaryContainingTransactionId_IsIgnored()
    {
        var arguments = new Dictionary<string, string> { ["-i"] = "smuggled", ["-f"] = "file.tyhp" };
        var command = new DbgpCommand(DbgpConstants.Commands.Run, "5", arguments);

        command.TransactionId.Should().Be("5");
        command.Arguments.Should().NotContainKey("-i");
        DbgpMessageParser.ParseCommand(DbgpMessageParser.SerializeCommand(command)).TransactionId.Should().Be("5");
    }

    [Fact]
    public void SerializeCommand_ArgumentsInsertedBeforeTransactionIdLookup_EmitsTransactionIdFirst()
    {
        var arguments = new Dictionary<string, string> { ["-n"] = "5", ["-t"] = "line" };
        var command = new DbgpCommand(DbgpConstants.Commands.BreakpointSet, "3", arguments);

        string text = DbgpMessageParser.Utf8.GetString(DbgpMessageParser.SerializeCommand(command)).TrimEnd('\0');

        text.Should().Be("breakpoint_set -i 3 -n 5 -t line");
    }

    [Fact]
    public void SerializeCommand_EmptyDataArray_RoundTripsAsEmptyNotNull()
    {
        var command = new DbgpCommand(DbgpConstants.Commands.Eval, "1", data: []);

        byte[] wire = DbgpMessageParser.SerializeCommand(command);
        DbgpCommand roundTrip = DbgpMessageParser.ParseCommand(wire);

        roundTrip.Data.Should().NotBeNull();
        roundTrip.Data.Should().BeEmpty();
    }

    [Fact]
    public void SerializeCommand_NullData_RoundTripsAsNull()
    {
        var command = new DbgpCommand(DbgpConstants.Commands.Run, "1");

        byte[] wire = DbgpMessageParser.SerializeCommand(command);
        DbgpCommand roundTrip = DbgpMessageParser.ParseCommand(wire);

        roundTrip.Data.Should().BeNull();
    }

    [Fact]
    public void ParseCommand_FilenameValueStartingWithDash_QuotesOnSerializeAndRoundTrips()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand("breakpoint_set -i 1 -t line -n 5");
        command.Filename = "-weird-file.tyhp";

        byte[] wire = DbgpMessageParser.SerializeCommand(command);
        string text = DbgpMessageParser.Utf8.GetString(wire).TrimEnd('\0');
        text.Should().Contain("\"-weird-file.tyhp\"");

        DbgpCommand roundTrip = DbgpMessageParser.ParseCommand(wire);
        roundTrip.Filename.Should().Be("-weird-file.tyhp");
    }

    [Fact]
    public void DbgpMessage_FromCommand_IsIdeToXDebug()
    {
        DbgpCommand command = DbgpMessageParser.ParseCommand("run -i 2");
        DbgpMessage message = DbgpMessage.FromCommand(command);

        message.IsCommand.Should().BeTrue();
        message.IsResponse.Should().BeFalse();
        message.Direction.Should().Be(DbgpMessageDirection.IdeToXDebug);
        message.Command.Should().BeSameAs(command);
        message.Response.Should().BeNull();
    }
}
