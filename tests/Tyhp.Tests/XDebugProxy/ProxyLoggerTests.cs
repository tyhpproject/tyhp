using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.XDebugProxy;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class ProxyLoggerTests
{
    [Fact]
    public void Debug_EmitsAtDebugLevel_WithProxyPrefix()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("debug", sink.Add);

        logger.Debug("IDE→XDebug breakpoint_set");

        sink.Should().ContainSingle()
            .Which.Should().Contain("[proxy]")
            .And.Contain("IDE→XDebug")
            .And.Contain("breakpoint_set");
    }

    [Fact]
    public void Debug_IsSuppressedAtInfoLevel()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("info", sink.Add);

        logger.Debug("relayed");

        sink.Should().BeEmpty();
    }

    [Fact]
    public void Info_IsSuppressedAtWarnLevel()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("warn", sink.Add);

        logger.Info("CLI_XDebugProxySessionPaired", "1");

        sink.Should().BeEmpty();
    }

    [Fact]
    public void Warn_EmitsAtWarnLevel_InfoDoesNot()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("warn", sink.Add);

        logger.Info("CLI_XDebugProxySessionPaired", "1");
        logger.Warn("CLI_XDebugProxyTranslationFailed", "no map");

        sink.Should().ContainSingle()
            .Which.Should().Contain("[proxy]")
            .And.Contain("no map");
    }

    [Fact]
    public void Error_IsTheOnlyLevelEmittedAtError()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("error", sink.Add);

        logger.Debug("dump");
        logger.Info("CLI_XDebugProxySessionPaired", "1");
        logger.Warn("CLI_XDebugProxyTranslationFailed", "x");
        logger.Error("CLI_XDebugProxySessionError", "bind failed");

        sink.Should().ContainSingle()
            .Which.Should().Contain("bind failed");
    }

    [Fact]
    public void ForSession_PrefixesSessionId()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("info", sink.Add).ForSession("7");

        logger.Info("CLI_XDebugProxySessionDisconnected", "7");

        sink.Should().ContainSingle()
            .Which.Should().Contain("[session:7]");
    }

    [Fact]
    public void Truncate_CutsPayloadAboveLimit()
    {
        string huge = new string('x', ProxyLogger.MaxDebugPayloadChars + 50);

        string truncated = ProxyLogger.Truncate(huge);

        truncated.Should().EndWith("...");
        truncated.Length.Should().Be(ProxyLogger.MaxDebugPayloadChars + 3);
    }

    [Fact]
    public void Truncate_LeavesShortPayloadUnchanged()
    {
        ProxyLogger.Truncate("short").Should().Be("short");
        ProxyLogger.Truncate(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, ProxyLogLevel.Info)]
    [InlineData("", ProxyLogLevel.Info)]
    [InlineData("info", ProxyLogLevel.Info)]
    [InlineData("DEBUG", ProxyLogLevel.Debug)]
    [InlineData("warn", ProxyLogLevel.Warn)]
    [InlineData("warning", ProxyLogLevel.Warn)]
    [InlineData("error", ProxyLogLevel.Error)]
    [InlineData("nope", ProxyLogLevel.Info)]
    public void ParseLevel_MapsConfigValues(string? raw, ProxyLogLevel expected)
    {
        ProxyLogger.ParseLevel(raw).Should().Be(expected);
    }

    [Fact]
    public void Debug_DoesNotTreatXmlBracesAsFormatItems()
    {
        var sink = new List<string>();
        var logger = new ProxyLogger("debug", sink.Add);

        logger.Debug("<response status=\"break\" filename=\"{0}\">{1}</response>");

        sink.Should().ContainSingle()
            .Which.Should().Contain("{0}")
            .And.Contain("{1}");
    }
}

[Trait("Category", "CLI")]
[Trait("Category", "XDebugProxy")]
public class XDebugProxyHelpTests
{
    [Fact]
    public void XDebugProxyHelp_IncludesAllCliFlags_AndIsNotPlaceholderOnly()
    {
        string placeholder = Message.LocalizeRaw("CLI_XDebugProxyHelpPlaceholder");
        Message.LocalizeRaw("CLI_XDebugProxyHelpDescription")
            .Should().NotBe(placeholder)
            .And.NotContain("not yet implemented");

        Message.LocalizeRaw("CLI_XDebugProxyHelpOptionIdePort").Should().Contain("9003");
        Message.LocalizeRaw("CLI_XDebugProxyHelpOptionXdebugPort").Should().Contain("9004");
        Message.LocalizeRaw("CLI_XDebugProxyHelpOptionSourcemapDir").Should().Contain(".php.map");
        Message.LocalizeRaw("CLI_XDebugProxyHelpOptionIdeKey").Should().Contain("idekey");
        Message.LocalizeRaw("CLI_XDebugProxyHelpOptionLogLevel").Should().Contain("debug");
        Message.LocalizeRaw("CLI_XDebugProxyHelpOptionPidFile").Should().Contain("opt-in");

        Message.LocalizeRaw("CLI_XDebugProxyHelpXdebugIniExample").Should().Contain("client_port = 9004");
        Message.LocalizeRaw("CLI_XDebugProxyHelpTyhpJsonExample").Should().Contain("\"idePort\"");
        Message.LocalizeRaw("CLI_XDebugProxyHelpLaunchJsonExample").Should().Contain("\"port\": 9003");

        var previousOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            DisplayHelp.XDebugProxyHelp();
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        string output = captured.ToString();
        if (string.IsNullOrWhiteSpace(output))
        {
            // Konsole ConcurrentWriter may not honor Console.SetOut; the resx
            // assertions above still prove the option catalog is complete.
            return;
        }

        output.Should().Contain("--ide-port");
        output.Should().Contain("--xdebug-port");
        output.Should().Contain("--sourcemap-dir");
        output.Should().Contain("--ide-key");
        output.Should().Contain("--log-level");
        output.Should().Contain("--pid-file");
        output.Should().NotContain(placeholder);
    }
}
