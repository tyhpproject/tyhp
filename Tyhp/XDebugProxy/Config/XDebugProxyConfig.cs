using System.Globalization;
using Microsoft.Extensions.Configuration;
using Tyhp.Extensions;

namespace Tyhp.XDebugProxy.Config
{
    /// <summary>
    /// Settings for the XDebug proxy TCP listeners and session pairing.
    /// Phase 5 binds this from <c>IConfiguration</c> / CLI; Phase 4 uses the POCO directly.
    /// </summary>
    public sealed class XDebugProxyConfig
    {
        /// <summary>IDE debug-adapter listen port. <c>0</c> binds an ephemeral port.</summary>
        public int IdeListenPort { get; set; } = 9003;

        /// <summary>XDebug engine listen port. <c>0</c> binds an ephemeral port.</summary>
        public int XDebugListenPort { get; set; } = 9004;

        /// <summary>Address the IDE listener binds to.</summary>
        public string IdeListenAddress { get; set; } = "127.0.0.1";

        /// <summary>Address the XDebug listener binds to.</summary>
        public string XDebugListenAddress { get; set; } = "127.0.0.1";

        /// <summary>Directory of <c>*.php.map</c> files. Phase 5 may default this to <c>output.path</c>.</summary>
        public string? SourceMapDirectory { get; set; }

        /// <summary>Root of original <c>.tyhp</c> sources, used to construct a <c>PathMapper</c>.</summary>
        public string? TyhpSourceRoot { get; set; }

        /// <summary>Root of compiled <c>.php</c> output, used to construct a <c>PathMapper</c>.</summary>
        public string? PhpOutputRoot { get; set; }

        /// <summary>
        /// When set, only XDebug <c>&lt;init&gt;</c> packets whose <c>idekey</c> matches are
        /// accepted. When null, every idekey is eligible.
        /// </summary>
        public string? IdeKey { get; set; }

        /// <summary>
        /// Maximum concurrent paired sessions. Pending IDE connections are capped at this
        /// number, and pending XDebug connections (including in-flight init reads) are
        /// capped independently at the same number. Extra sockets are closed, not queued.
        /// </summary>
        public int MaxSessions { get; set; } = 10;

        /// <summary>Log verbosity: debug, info, warn, or error (default info). Filtered by <c>ProxyLogger</c>.</summary>
        public string LogLevel { get; set; } = "info";

        /// <summary>When true, the shared <c>SourceMapStore</c> reloads maps on change.</summary>
        public bool AutoReloadSourceMaps { get; set; } = true;

        /// <summary>
        /// How long an unpaired IDE or XDebug connection may wait for a peer.
        /// <see langword="null"/> waits until the server stops.
        /// </summary>
        public TimeSpan? PairingTimeout { get; set; }

        /// <summary>
        /// Overlay CLI flags and <c>tyhp.json</c> <c>xdebugProxy.*</c> values onto this instance.
        /// Command-line keys win over the JSON section. Unspecified properties keep their
        /// current values (constructor defaults when called on a fresh instance).
        /// </summary>
        public void ApplyFrom(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (TryReadInt(configuration, "ide-port", "xdebugProxy:idePort", out int idePort))
            {
                this.IdeListenPort = idePort;
            }

            if (TryReadInt(configuration, "xdebug-port", "xdebugProxy:xdebugPort", out int xdebugPort))
            {
                this.XDebugListenPort = xdebugPort;
            }

            var ideAddress = configuration["xdebugProxy:ideListenAddress"];
            if (!String.IsNullOrWhiteSpace(ideAddress))
            {
                this.IdeListenAddress = ideAddress.Trim();
            }

            var xdebugAddress = configuration["xdebugProxy:xdebugListenAddress"];
            if (!String.IsNullOrWhiteSpace(xdebugAddress))
            {
                this.XDebugListenAddress = xdebugAddress.Trim();
            }

            var sourceMapDir = FirstNonEmpty(
                configuration["sourcemap-dir"],
                configuration["xdebugProxy:sourceMapDir"]);
            if (sourceMapDir is not null)
            {
                this.SourceMapDirectory = sourceMapDir;
            }

            var ideKey = FirstNonEmpty(
                configuration["ide-key"],
                configuration["xdebugProxy:ideKey"]);
            if (ideKey is not null)
            {
                this.IdeKey = ideKey;
            }

            if (TryReadInt(configuration, cliKey: null, "xdebugProxy:maxSessions", out int maxSessions))
            {
                this.MaxSessions = maxSessions;
            }

            var logLevel = FirstNonEmpty(
                configuration["log-level"],
                configuration["xdebugProxy:logLevel"]);
            if (logLevel is not null)
            {
                this.LogLevel = logLevel;
            }

            if (configuration.GetSection("xdebugProxy:autoReloadSourceMaps").Exists())
            {
                this.AutoReloadSourceMaps = configuration["xdebugProxy:autoReloadSourceMaps"].ParseBool();
            }
        }

        private static bool TryReadInt(
            IConfiguration configuration,
            string? cliKey,
            string jsonKey,
            out int value)
        {
            string? raw = null;
            if (cliKey is not null && configuration.GetSection(cliKey).Exists())
            {
                raw = configuration[cliKey];
            }
            else if (configuration.GetSection(jsonKey).Exists())
            {
                raw = configuration[jsonKey];
            }

            return Int32.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (value is not null && !String.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }
    }
}
