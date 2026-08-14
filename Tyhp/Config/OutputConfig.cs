using Microsoft.Extensions.Configuration;
using Tyhp.Domain.Exceptions;
using Tyhp.Extensions;

namespace Tyhp.Config
{
    /// <summary>
    /// Output-specific configuration from <c>tyhp.json</c> <c>output.*</c> keys.
    /// </summary>
    public sealed class OutputConfig
    {
        private static readonly HashSet<string> SupportedPhpVersions = new(StringComparer.Ordinal)
        {
            "8.0", "8.1", "8.2", "8.3", "8.4", "8.5",
        };

        /// <summary>
        /// Supported <c>output.phpVersion</c> values, for CLI messages that list the valid choices.
        /// </summary>
        internal static IReadOnlyCollection<string> SupportedPhpVersionNames => SupportedPhpVersions;

        /// <summary>Output directory for compiled PHP files.</summary>
        public string Path { get; set; } = "build/";

        /// <summary>Prefix added to all namespaces in emitted PHP.</summary>
        public string? NamespacePrefix { get; set; }

        /// <summary>Whether emitted PHP includes source comments.</summary>
        public bool IncludeComments { get; set; } = true;

        /// <summary>Target PHP version (e.g. <c>8.4</c>).</summary>
        public string PhpVersion { get; set; } = "8.4";

        /// <summary>Whether to emit <c>declare(strict_types=1)</c> in output files.</summary>
        public bool StrictTypes { get; set; } = true;

        internal void ApplyFrom(
            IConfiguration configuration,
            Action<MessageCode, object[]>? warn = null)
        {
            this.Path = configuration["output:path"] ?? "build/";
            this.NamespacePrefix = configuration["output:namespacePrefix"];

            if (configuration.GetSection("output:comments").Exists())
            {
                this.IncludeComments = configuration["output:comments"].ParseBool();
            }

            var phpVersion = configuration["output:phpVersion"]
                ?? configuration["phpVersion"]
                ?? "8.4";

            if (!IsSupportedPhpVersion(phpVersion))
            {
                warn?.Invoke(MessageCode.ConfigInvalidPhpVersion, [phpVersion]);
                phpVersion = "8.4";
            }

            this.PhpVersion = phpVersion;

            if (configuration.GetSection("output:strictTypes").Exists())
            {
                this.StrictTypes = configuration["output:strictTypes"].ParseBool();
            }
        }

        internal static bool IsSupportedPhpVersion(string version)
        {
            if (SupportedPhpVersions.Contains(version))
            {
                return true;
            }

            var parts = version.Split('.', 2);
            if (parts.Length >= 2
                && Int32.TryParse(parts[0], out int major)
                && Int32.TryParse(parts[1], out int minor)
                && major == 8
                && minor >= 0
                && minor <= 5)
            {
                return true;
            }

            return false;
        }
    }
}
