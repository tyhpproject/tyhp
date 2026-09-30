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

        /// <summary>
        /// Published package root: <c>composer.json</c> and <c>package.tyhpdef</c>.
        /// Libraries additive-merge <c>extra.tyhp.package</c> onto that <c>composer.json</c>.
        /// Relative to the project root (the directory that
        /// contains <c>tyhp.json</c>). Defaults to <c>"."</c> (the project root).
        /// Compiled PHP still uses <see cref="Path"/>, which may be this directory or
        /// a subdirectory such as <c>src/</c>.
        /// </summary>
        public string PublishPath { get; set; } = ".";

        /// <summary>
        /// When true, <c>tyhp build</c> deletes <see cref="PublishPath"/> and recreates it
        /// before compiling. Default false. Refused when the publish path is the project
        /// root, a system directory, or overlaps source include paths.
        /// </summary>
        public bool PublishClean { get; set; }

        /// <summary>
        /// Extra files copied into <see cref="PublishPath"/> after a successful emit.
        /// Empty by default.
        /// </summary>
        public IReadOnlyList<PublishContentEntry> PublishContent { get; set; } = [];

        /// <summary>Prefix added to all namespaces in emitted PHP.</summary>
        public string? NamespacePrefix { get; set; }

        /// <summary>Whether emitted PHP includes source comments.</summary>
        public bool IncludeComments { get; set; } = true;

        /// <summary>Target PHP version (e.g. <c>8.4</c>).</summary>
        public string PhpVersion { get; set; } = "8.4";

        /// <summary>
        /// True when neither <c>output.phpVersion</c> nor the legacy top-level <c>phpVersion</c>
        /// key was present in configuration, so <see cref="PhpVersion"/> was defaulted to
        /// <see cref="Domain.Services.CompilationOptions.DefaultPhpVersionWhenUnset"/> rather than
        /// read from the project. Distinct from an explicitly present but unsupported value, which
        /// falls back to <c>8.4</c> with <see cref="MessageCode.ConfigInvalidPhpVersion"/> instead.
        /// The checker emits <see cref="MessageCode.CheckerPhpVersionDefaulted"/> (4306) once per
        /// compilation when this is set.
        /// </summary>
        public bool PhpVersionWasDefaulted { get; private set; }

        /// <summary>Whether to emit <c>declare(strict_types=1)</c> in output files.</summary>
        public bool StrictTypes { get; set; } = true;

        internal void ApplyFrom(
            IConfiguration configuration,
            Action<MessageCode, object[]>? warn = null)
        {
            this.Path = configuration["output:path"] ?? "build/";
            var publishPath = configuration["output:publishPath"];
            this.PublishPath = string.IsNullOrWhiteSpace(publishPath) ? "." : publishPath.Trim();
            if (configuration.GetSection("output:publishClean").Exists())
            {
                this.PublishClean = configuration["output:publishClean"].ParseBool();
            }

            this.PublishContent = ReadPublishContent(configuration, warn);
            this.NamespacePrefix = configuration["output:namespacePrefix"];

            if (configuration.GetSection("output:comments").Exists())
            {
                this.IncludeComments = configuration["output:comments"].ParseBool();
            }

            var phpVersion = configuration["output:phpVersion"] ?? configuration["phpVersion"];

            if (string.IsNullOrWhiteSpace(phpVersion))
            {
                phpVersion = Domain.Services.CompilationOptions.DefaultPhpVersionWhenUnset;
                this.PhpVersionWasDefaulted = true;
            }
            else if (!IsSupportedPhpVersion(phpVersion))
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

        private static IReadOnlyList<PublishContentEntry> ReadPublishContent(
            IConfiguration configuration,
            Action<MessageCode, object[]>? warn)
        {
            var section = configuration.GetSection("output:publishContent");
            if (!section.Exists())
            {
                return [];
            }

            if (!string.IsNullOrWhiteSpace(section.Value))
            {
                return
                [
                    new PublishContentEntry { Src = [section.Value.Trim()] },
                ];
            }

            var entries = new List<PublishContentEntry>();
            foreach (var child in section.GetChildren())
            {
                if (!int.TryParse(child.Key, out var index))
                {
                    continue;
                }

                var parsed = PublishContentEntry.TryParse(child, index, warn);
                if (parsed != null)
                {
                    entries.Add(parsed);
                }
            }

            return entries;
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
