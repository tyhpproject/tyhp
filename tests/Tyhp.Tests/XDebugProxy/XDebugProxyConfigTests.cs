using Microsoft.Extensions.Configuration;
using Tyhp.XDebugProxy.Config;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class XDebugProxyConfigTests
{
    [Fact]
    public void ApplyFrom_LeavesConstructorDefaults_WhenSectionMissing()
    {
        var config = new XDebugProxyConfig();
        config.ApplyFrom(new ConfigurationBuilder().Build());

        config.IdeListenPort.Should().Be(9003);
        config.XDebugListenPort.Should().Be(9004);
        config.IdeListenAddress.Should().Be("127.0.0.1");
        config.XDebugListenAddress.Should().Be("127.0.0.1");
        config.SourceMapDirectory.Should().BeNull();
        config.IdeKey.Should().BeNull();
        config.MaxSessions.Should().Be(10);
        config.LogLevel.Should().Be("info");
        config.AutoReloadSourceMaps.Should().BeTrue();
    }

    [Fact]
    public void ApplyFrom_CliKeysOverrideJsonSection()
    {
        var config = new XDebugProxyConfig();
        config.ApplyFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["xdebugProxy:idePort"] = "9003",
                ["xdebugProxy:xdebugPort"] = "9004",
                ["xdebugProxy:sourceMapDir"] = "json-maps",
                ["xdebugProxy:ideKey"] = "JSON",
                ["xdebugProxy:logLevel"] = "warn",
                ["ide-port"] = "9111",
                ["xdebug-port"] = "9222",
                ["sourcemap-dir"] = "cli-maps",
                ["ide-key"] = "CLI",
                ["log-level"] = "debug",
            })
            .Build());

        config.IdeListenPort.Should().Be(9111);
        config.XDebugListenPort.Should().Be(9222);
        config.SourceMapDirectory.Should().Be("cli-maps");
        config.IdeKey.Should().Be("CLI");
        config.LogLevel.Should().Be("debug");
    }

    [Fact]
    public void ApplyFrom_ParsesListenAddressesAndMaxSessionsFromJson()
    {
        var config = new XDebugProxyConfig();
        config.ApplyFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["xdebugProxy:ideListenAddress"] = "0.0.0.0",
                ["xdebugProxy:xdebugListenAddress"] = "localhost",
                ["xdebugProxy:maxSessions"] = "2",
                ["xdebugProxy:autoReloadSourceMaps"] = "false",
            })
            .Build());

        config.IdeListenAddress.Should().Be("0.0.0.0");
        config.XDebugListenAddress.Should().Be("localhost");
        config.MaxSessions.Should().Be(2);
        config.AutoReloadSourceMaps.Should().BeFalse();
    }
}
