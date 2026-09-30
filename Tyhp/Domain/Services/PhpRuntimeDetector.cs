using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Inspects a caller-supplied <c>--php</c> binary. Never searches PATH, Homebrew, or
    /// <c>PHP_BINARY</c>.
    /// </summary>
    public class PhpRuntimeDetector
    {
        /// <summary>
        /// Inspect <paramref name="explicitPath"/> only. Missing / not executable → 7501.
        /// When <paramref name="expectedMinor"/> is set, the binary's major.minor must match.
        /// </summary>
        public PhpRuntimeInfo? Inspect(
            string explicitPath,
            DiagnosticBag diagnostics,
            string? expectedMinor = null,
            int timeoutMs = 15_000,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);

            if (string.IsNullOrWhiteSpace(explicitPath))
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpNotFound, "generate_tyhpdef", 0, 0);
                return null;
            }

            var path = explicitPath.Trim();
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(path);
            }

            if (!File.Exists(path))
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpNotFound, "generate_tyhpdef", 0, 0);
                return null;
            }

            var versionResult = PhpProcessRunner.Run(
                path,
                ["-v"],
                timeoutMs,
                cancellationToken);
            if (versionResult.TimedOut || versionResult.ExitCode != 0
                || string.IsNullOrWhiteSpace(versionResult.StandardOutput))
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpNotFound, "generate_tyhpdef", 0, 0);
                return null;
            }

            var version = ParsePhpVersion(versionResult.StandardOutput);
            if (string.IsNullOrWhiteSpace(version))
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpNotFound, "generate_tyhpdef", 0, 0);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(expectedMinor))
            {
                var actualMinor = PhpRuntimeVersion.ToMinor(version);
                var expected = PhpRuntimeVersion.ToMinor(expectedMinor) ?? expectedMinor.Trim();
                if (!string.Equals(actualMinor, expected, StringComparison.Ordinal))
                {
                    diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefPhpVersionMismatch", expected, version));
                    return null;
                }
            }

            var modulesResult = PhpProcessRunner.Run(
                path,
                ["-m"],
                timeoutMs,
                cancellationToken);
            var extensions = ParseModules(modulesResult.StandardOutput);

            return new PhpRuntimeInfo
            {
                Path = path,
                Version = version,
                IniPath = null,
                LoadedExtensions = extensions,
                IsManaged = false,
                Provider = "user",
            };
        }

        internal static string? ParsePhpVersion(string phpVOutput)
        {
            foreach (var line in phpVOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                const string prefix = "PHP ";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = line[prefix.Length..].Trim();
                var end = 0;
                while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] is '.' or '-'))
                {
                    end++;
                }

                if (end > 0)
                {
                    return rest[..end].TrimEnd('-', '.');
                }
            }

            return null;
        }

        internal static IReadOnlyList<string> ParseModules(string phpMOutput)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inSection = false;
            foreach (var raw in (phpMOutput ?? "").Split(['\r', '\n']))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Contains("Module", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection)
                {
                    continue;
                }

                if (seen.Add(line))
                {
                    result.Add(line);
                }
            }

            return result;
        }
    }
}
