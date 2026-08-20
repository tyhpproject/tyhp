using Tyhp.CLI;

namespace Tyhp.XDebugProxy
{
    /// <summary>
    /// Verbosity for <see cref="ProxyLogger"/>. Ordered from most to least verbose.
    /// </summary>
    public enum ProxyLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>
    /// Structured XDebug-proxy logger. User-facing info/warn/error lines are localized;
    /// debug dumps (command names, XML) are not.
    /// </summary>
    public sealed class ProxyLogger
    {
        /// <summary>Maximum characters of a debug payload (raw XML / command text) before truncation.</summary>
        public const int MaxDebugPayloadChars = 4096;

        private const string ProxyPrefix = "[proxy]";

        private readonly ProxyLogLevel _minLevel;
        private readonly Action<string>? _onLog;
        private readonly string _prefix;

        public ProxyLogger(string? logLevel, Action<string>? onLog = null)
            : this(ParseLevel(logLevel), onLog, ProxyPrefix)
        {
        }

        public ProxyLogger(ProxyLogLevel minLevel, Action<string>? onLog = null)
            : this(minLevel, onLog, ProxyPrefix)
        {
        }

        private ProxyLogger(ProxyLogLevel minLevel, Action<string>? onLog, string prefix)
        {
            this._minLevel = minLevel;
            this._onLog = onLog;
            this._prefix = prefix;
        }

        public ProxyLogLevel MinLevel => this._minLevel;

        /// <summary>Returns a logger that prefixes lines with <c>[session:{sessionId}]</c>.</summary>
        public ProxyLogger ForSession(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            return new ProxyLogger(this._minLevel, this._onLog, "[session:" + sessionId + "]");
        }

        public bool IsEnabled(ProxyLogLevel level) => level >= this._minLevel;

        /// <summary>
        /// Parse a <c>logLevel</c> config/CLI value. Unknown values default to
        /// <see cref="ProxyLogLevel.Info"/>.
        /// </summary>
        public static ProxyLogLevel ParseLevel(string? logLevel)
        {
            if (string.Equals(logLevel, "debug", StringComparison.OrdinalIgnoreCase))
            {
                return ProxyLogLevel.Debug;
            }

            if (string.Equals(logLevel, "warn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(logLevel, "warning", StringComparison.OrdinalIgnoreCase))
            {
                return ProxyLogLevel.Warn;
            }

            if (string.Equals(logLevel, "error", StringComparison.OrdinalIgnoreCase))
            {
                return ProxyLogLevel.Error;
            }

            return ProxyLogLevel.Info;
        }

        /// <summary>
        /// Truncate a debug payload so verbose XML dumps cannot flood the console.
        /// </summary>
        public static string Truncate(string? payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return string.Empty;
            }

            if (payload.Length <= MaxDebugPayloadChars)
            {
                return payload;
            }

            return payload[..MaxDebugPayloadChars] + "...";
        }

        /// <summary>Non-localized debug dump (command names, directions, truncated XML).</summary>
        public void Debug(string message)
        {
            if (!this.IsEnabled(ProxyLogLevel.Debug))
            {
                return;
            }

            this.Write(ProxyLogLevel.Debug, message);
        }

        public void Info(string resourceKey, params object[] args)
        {
            this.EmitLocalized(ProxyLogLevel.Info, resourceKey, args);
        }

        public void Warn(string resourceKey, params object[] args)
        {
            this.EmitLocalized(ProxyLogLevel.Warn, resourceKey, args);
        }

        public void Error(string resourceKey, params object[] args)
        {
            this.EmitLocalized(ProxyLogLevel.Error, resourceKey, args);
        }

        private void EmitLocalized(ProxyLogLevel level, string resourceKey, object[] args)
        {
            if (!this.IsEnabled(level))
            {
                return;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
            string body = Message.Localize(resourceKey, args);
            this.Write(level, body);
        }

        private void Write(ProxyLogLevel level, string body)
        {
            string line = this._prefix + " " + body;
            try
            {
                this._onLog?.Invoke(line);
            }
            catch
            {
            }

            try
            {
                switch (level)
                {
                    case ProxyLogLevel.Debug:
                        // "{0}" keeps braces in XML dumps from being treated as format items.
                        Message.Debug("{0}", line);
                        break;
                    case ProxyLogLevel.Info:
                        Message.Info("CLI_XDebugProxyLogLine", line);
                        break;
                    case ProxyLogLevel.Warn:
                        Message.Warn("CLI_XDebugProxyLogLine", line);
                        break;
                    case ProxyLogLevel.Error:
                        Message.Error("CLI_XDebugProxyLogLine", line);
                        break;
                }
            }
            catch
            {
            }
        }
    }
}
