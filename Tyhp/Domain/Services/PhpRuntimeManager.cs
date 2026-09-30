using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Downloads and caches Tyhp-managed PHP CLIs (one per minor 8.2–8.5) with an isolated ini.
    /// Does not search PATH, Homebrew installs, or <c>PHP_BINARY</c>.
    /// </summary>
    public class PhpRuntimeManager
    {
        internal const string ManifestResourceSuffix = "PhpRuntimeManifest.json";

        private readonly IPhpRuntimeTransport _transport;

        public PhpRuntimeManager()
            : this(new PhpRuntimeTransport())
        {
        }

        internal PhpRuntimeManager(IPhpRuntimeTransport transport)
        {
            this._transport = transport;
        }

        public virtual PhpRuntimeInfo? Ensure(
            string minor,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(diagnostics);

            var normalizedMinor = PhpRuntimeVersion.ToMinor(minor);
            if (normalizedMinor is null || !PhpRuntimeVersion.IsSupportedMinor(normalizedMinor))
            {
                diagnostics.AddError(
                    MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefUnsupportedMinor", minor ?? ""));
                return null;
            }

            string rid;
            try
            {
                rid = ResolveRid();
            }
            catch (NotSupportedException ex)
            {
                diagnostics.AddError(
                    MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefUnsupportedRid", ex.Message));
                return null;
            }

            var cacheRoot = ResolveCacheRoot(options.PhpRuntimeDir);
            var minorDir = Path.Combine(cacheRoot, rid, normalizedMinor);

            try
            {
                Directory.CreateDirectory(minorDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.AddError(
                    MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                    "generate_tyhpdef",
                    0,
                    0,
                    ex.Message);
                return null;
            }

            var cached = TryLoadCached(minorDir, normalizedMinor, options.ExtensionName, options.PhpProcessTimeoutMs, cancellationToken);
            if (cached is not null && options.NoPhpRuntimeUpdate)
            {
                return cached;
            }

            PhpRuntimeInfo? updated = null;
            try
            {
                updated = this.DownloadLatest(
                    rid,
                    normalizedMinor,
                    minorDir,
                    options,
                    diagnostics,
                    cancellationToken).GetAwaiter().GetResult();
            }
            catch (PhpRuntimeChecksumException)
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpRuntimeChecksumMismatch, "generate_tyhpdef", 0, 0);
                return cached;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or NotSupportedException)
            {
                if (cached is null)
                {
                    diagnostics.AddError(
                        MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                        "generate_tyhpdef",
                        0,
                        0,
                        ex.Message);
                    return null;
                }

                diagnostics.AddWarning(MessageCode.TyhpdefPhpRuntimeUpdateFailed, "generate_tyhpdef", 0, 0);
                Message.Warn("CLI_TyhpdefRuntimeUpdateFailedDetail", ex.Message);
                return cached;
            }

            if (updated is not null)
            {
                return updated;
            }

            if (cached is not null)
            {
                if (!options.NoPhpRuntimeUpdate)
                {
                    diagnostics.AddWarning(MessageCode.TyhpdefPhpRuntimeUpdateFailed, "generate_tyhpdef", 0, 0);
                }

                return cached;
            }

            diagnostics.AddError(
                MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                "generate_tyhpdef",
                0,
                0,
                Message.Localize("CLI_TyhpdefRuntimeDownloadEmpty"));
            return null;
        }

        internal static string ResolveRid()
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            if (OperatingSystem.IsWindows())
            {
                if (arch is System.Runtime.InteropServices.Architecture.X64)
                {
                    return "win-x64";
                }

                throw new NotSupportedException("win-" + arch);
            }

            if (OperatingSystem.IsMacOS())
            {
                return arch is System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            }

            if (OperatingSystem.IsLinux())
            {
                return arch is System.Runtime.InteropServices.Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            }

            throw new NotSupportedException(System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        }

        internal static string ResolveCacheRoot(string? overrideDir)
        {
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return Path.GetFullPath(overrideDir.Trim());
            }

            var env = Environment.GetEnvironmentVariable("TYHP_PHP_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                return Path.GetFullPath(env.Trim());
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tyhp",
                "php-runtimes");
        }

        internal static string LoadManifestJson()
        {
            var assembly = typeof(PhpRuntimeManager).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(ManifestResourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                throw new InvalidOperationException("PhpRuntimeManifest.json embedded resource was not found.");
            }

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("PhpRuntimeManifest.json stream was null.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private async Task<PhpRuntimeInfo?> DownloadLatest(
            string rid,
            string minor,
            string minorDir,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            if (rid.StartsWith("win", StringComparison.Ordinal))
            {
                return await this.TryWindowsAsync(rid, minor, minorDir, options, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
            }

            var staticPhp = await this.TryStaticPhpAsync(rid, minor, minorDir, options, diagnostics, cancellationToken)
                .ConfigureAwait(false);
            if (staticPhp is not null)
            {
                return staticPhp;
            }

            return await this.TryHomebrewAsync(rid, minor, minorDir, options, diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<PhpRuntimeInfo?> TryStaticPhpAsync(
            string rid,
            string minor,
            string minorDir,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            MapUnixRid(rid, out var os, out var arch);
            var listingUrls = new[]
            {
                "https://dl.static-php.dev/static-php-cli/bulk/",
                "https://dl.static-php.dev/static-php-cli/common/",
            };

            string? bestUrl = null;
            string? bestVersion = null;
            foreach (var listing in listingUrls)
            {
                var html = await this._transport.GetStringAsync(new Uri(listing), cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                var pattern = new Regex(
                    $@"php-({Regex.Escape(minor)}\.\d+)-cli-{Regex.Escape(os)}-{Regex.Escape(arch)}\.tar\.gz",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                foreach (Match match in pattern.Matches(html))
                {
                    var version = match.Groups[1].Value;
                    if (bestVersion is null || PhpRuntimeVersion.ComparePatch(version, bestVersion) > 0)
                    {
                        bestVersion = version;
                        bestUrl = listing.TrimEnd('/') + "/" + match.Value;
                    }
                }

                if (bestUrl is not null)
                {
                    break;
                }
            }

            if (bestUrl is null || bestVersion is null)
            {
                return null;
            }

            var sha = await this.TryFetchSha256Async(bestUrl, cancellationToken).ConfigureAwait(false);
            return await this.InstallArchiveAsync(
                    provider: "staticphp",
                    artifactUrl: bestUrl,
                    version: bestVersion,
                    minor: minor,
                    minorDir: minorDir,
                    expectedSha256: sha,
                    isZip: false,
                    options,
                    diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<PhpRuntimeInfo?> TryWindowsAsync(
            string rid,
            string minor,
            string minorDir,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            _ = rid;
            var indexes = new (string Index, string Base)[]
            {
                ("https://windows.php.net/downloads/releases/releases.json",
                    "https://windows.php.net/downloads/releases/"),
                ("https://windows.php.net/downloads/releases/archives/releases.json",
                    "https://windows.php.net/downloads/releases/archives/"),
            };

            foreach (var (indexUrl, downloadBase) in indexes)
            {
                var json = await this._transport.GetStringAsync(new Uri(indexUrl), cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                if (!TryPickWindowsNtsZip(json, minor, downloadBase, out var url, out var version, out var sha256))
                {
                    continue;
                }

                return await this.InstallArchiveAsync(
                        provider: "windows-php-net",
                        artifactUrl: url,
                        version: version,
                        minor: minor,
                        minorDir: minorDir,
                        expectedSha256: sha256,
                        isZip: true,
                        options,
                        diagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return null;
        }

        private async Task<PhpRuntimeInfo?> TryHomebrewAsync(
            string rid,
            string minor,
            string minorDir,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            if (rid.StartsWith("win", StringComparison.Ordinal))
            {
                return null;
            }

            var formulaUrl = $"https://formulae.brew.sh/api/formula/php@{minor}.json";
            var formula = await this._transport.GetStringAsync(new Uri(formulaUrl), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(formula))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(formula);
            if (!doc.RootElement.TryGetProperty("bottle", out var bottle)
                || !bottle.TryGetProperty("stable", out var stable)
                || !stable.TryGetProperty("files", out var files))
            {
                return null;
            }

            var tag = PickBottleTag(rid, files);
            if (tag is null || !files.TryGetProperty(tag, out var file))
            {
                return null;
            }

            var url = file.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            var sha = file.TryGetProperty("sha256", out var shaEl) ? shaEl.GetString() : null;
            var version = doc.RootElement.TryGetProperty("versions", out var versions)
                && versions.TryGetProperty("stable", out var stableVer)
                    ? stableVer.GetString()
                    : minor;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha))
            {
                return null;
            }

            var headers = await this.CreateGhcrHeadersAsync(url, cancellationToken).ConfigureAwait(false);
            var patchDir = Path.Combine(minorDir, SanitizeSegment(version ?? minor) + "-homebrew");
            Directory.CreateDirectory(patchDir);
            var archivePath = Path.Combine(patchDir, "php.bottle.tar.gz");
            try
            {
                var ok = await this._transport.DownloadToFileAsync(
                    new Uri(url),
                    archivePath,
                    sha,
                    cancellationToken,
                    headers).ConfigureAwait(false);
                if (!ok)
                {
                    return null;
                }
            }
            catch (PhpRuntimeChecksumException)
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpRuntimeChecksumMismatch, "generate_tyhpdef", 0, 0);
                return null;
            }

            var extractDir = Path.Combine(patchDir, "extract");
            ExtractTarGz(archivePath, extractDir);
            var phpPath = FindPhpBinary(extractDir);
            if (phpPath is null)
            {
                return null;
            }

            WriteAttribution(patchDir, "homebrew-bottle", url, version ?? minor, sha);
            var info = FinalizeInstall(phpPath, patchDir, version ?? minor, "homebrew-bottle", url, options);
            if (info is not null)
            {
                WriteCurrent(minorDir, Path.GetFileName(patchDir));
            }

            return info;
        }

        private async Task<PhpRuntimeInfo?> InstallArchiveAsync(
            string provider,
            string artifactUrl,
            string version,
            string minor,
            string minorDir,
            string? expectedSha256,
            bool isZip,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            var patchDir = Path.Combine(minorDir, SanitizeSegment(version));
            if (TryLoadFromPatchDir(patchDir, version, options.ExtensionName, options.PhpProcessTimeoutMs, cancellationToken, provider, artifactUrl) is { } existing)
            {
                WriteCurrent(minorDir, Path.GetFileName(patchDir));
                return existing;
            }

            Directory.CreateDirectory(patchDir);
            var archiveName = isZip ? "php.zip" : "php.tar.gz";
            var archivePath = Path.Combine(patchDir, archiveName);
            try
            {
                var ok = await this._transport.DownloadToFileAsync(
                    new Uri(artifactUrl),
                    archivePath,
                    expectedSha256,
                    cancellationToken).ConfigureAwait(false);
                if (!ok)
                {
                    return null;
                }
            }
            catch (PhpRuntimeChecksumException)
            {
                diagnostics.AddError(MessageCode.TyhpdefPhpRuntimeChecksumMismatch, "generate_tyhpdef", 0, 0);
                return null;
            }

            var extractDir = Path.Combine(patchDir, "extract");
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            Directory.CreateDirectory(extractDir);
            if (isZip)
            {
                ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
            }
            else
            {
                ExtractTarGz(archivePath, extractDir);
            }

            var phpPath = FindPhpBinary(extractDir);
            if (phpPath is null)
            {
                return null;
            }

            if (!OperatingSystem.IsWindows())
            {
                TryChmodExecute(phpPath);
            }

            WriteAttribution(patchDir, provider, artifactUrl, version, expectedSha256);
            var info = FinalizeInstall(phpPath, patchDir, version, provider, artifactUrl, options);
            if (info is not null)
            {
                WriteCurrent(minorDir, Path.GetFileName(patchDir));
            }

            return info;
        }

        private static PhpRuntimeInfo? FinalizeInstall(
            string phpPath,
            string patchDir,
            string version,
            string provider,
            string artifactUrl,
            TyhpdefGenerationOptions options)
        {
            var extensionDir = FindExtensionDir(Path.GetDirectoryName(phpPath) ?? patchDir);
            var iniPath = Path.Combine(patchDir, "tyhp.ini");
            WriteTyhpIni(iniPath, extensionDir, options.ExtensionName);
            var candidate = new PhpRuntimeInfo
            {
                Path = phpPath,
                Version = version,
                IniPath = iniPath,
                LoadedExtensions = [],
                IsManaged = true,
                Provider = provider,
                ArtifactUrl = artifactUrl,
                ExtensionDir = extensionDir,
            };

            var probed = Probe(candidate, options.ExtensionName, options.PhpProcessTimeoutMs, CancellationToken.None);
            return probed;
        }

        private static PhpRuntimeInfo? TryLoadCached(
            string minorDir,
            string minor,
            string? extensionName,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(minorDir))
            {
                return null;
            }

            var currentFile = Path.Combine(minorDir, "current.txt");
            var ordered = new List<string>();
            if (File.Exists(currentFile))
            {
                var current = File.ReadAllText(currentFile).Trim();
                if (current.Length > 0)
                {
                    ordered.Add(Path.Combine(minorDir, current));
                }
            }

            foreach (var dir in Directory.GetDirectories(minorDir).OrderByDescending(d => d, StringComparer.Ordinal))
            {
                if (!ordered.Contains(dir, StringComparer.Ordinal))
                {
                    ordered.Add(dir);
                }
            }

            foreach (var patchDir in ordered)
            {
                var loaded = TryLoadFromPatchDir(patchDir, minor, extensionName, timeoutMs, cancellationToken, "cache", null);
                if (loaded is not null)
                {
                    return loaded;
                }
            }

            return null;
        }

        private static PhpRuntimeInfo? TryLoadFromPatchDir(
            string patchDir,
            string versionHint,
            string? extensionName,
            int timeoutMs,
            CancellationToken cancellationToken,
            string provider,
            string? artifactUrl)
        {
            if (!Directory.Exists(patchDir))
            {
                return null;
            }

            var phpPath = FindPhpBinary(patchDir);
            if (phpPath is null)
            {
                return null;
            }

            var iniPath = Path.Combine(patchDir, "tyhp.ini");
            if (!File.Exists(iniPath))
            {
                var extensionDir = FindExtensionDir(Path.GetDirectoryName(phpPath) ?? patchDir);
                WriteTyhpIni(iniPath, extensionDir, extensionName);
            }

            var info = new PhpRuntimeInfo
            {
                Path = phpPath,
                Version = versionHint,
                IniPath = iniPath,
                LoadedExtensions = [],
                IsManaged = true,
                Provider = provider,
                ArtifactUrl = artifactUrl,
                ExtensionDir = FindExtensionDir(Path.GetDirectoryName(phpPath) ?? patchDir),
            };
            return Probe(info, extensionName, timeoutMs, cancellationToken);
        }

        private static PhpRuntimeInfo? Probe(
            PhpRuntimeInfo runtime,
            string? extensionName,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var timeout = timeoutMs <= 0 ? 15_000 : Math.Min(timeoutMs, 30_000);
            var versionResult = PhpProcessRunner.Run(runtime, ["-v"], timeout, cancellationToken);
            if (versionResult.TimedOut || versionResult.ExitCode != 0)
            {
                return null;
            }

            var version = PhpRuntimeDetector.ParsePhpVersion(versionResult.StandardOutput) ?? runtime.Version;
            var modulesResult = PhpProcessRunner.Run(runtime, ["-m"], timeout, cancellationToken);
            var extensions = PhpRuntimeDetector.ParseModules(modulesResult.StandardOutput);
            if (extensions.Count == 0)
            {
                return null;
            }

            var iniPath = runtime.IniPath;
            if (!string.IsNullOrWhiteSpace(extensionName)
                && !extensions.Any(e => string.Equals(e, extensionName, StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(runtime.ExtensionDir)
                && TryEnableExtension(runtime.ExtensionDir, extensionName, iniPath))
            {
                modulesResult = PhpProcessRunner.Run(runtime, ["-m"], timeout, cancellationToken);
                extensions = PhpRuntimeDetector.ParseModules(modulesResult.StandardOutput);
            }

            return new PhpRuntimeInfo
            {
                Path = runtime.Path,
                Version = version,
                IniPath = iniPath,
                LoadedExtensions = extensions,
                IsManaged = true,
                Provider = runtime.Provider,
                ArtifactUrl = runtime.ArtifactUrl,
                ExtensionDir = runtime.ExtensionDir,
            };
        }

        internal static void WriteTyhpIni(string iniPath, string? extensionDir, string? extensionName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; Tyhp-managed PHP configuration. Do not use to run applications.");
            sb.AppendLine("; Invoked as php -n -c tyhp.ini so user PHP_INI_SCAN_DIR is ignored.");
            if (!string.IsNullOrWhiteSpace(extensionDir) && Directory.Exists(extensionDir))
            {
                sb.AppendLine("extension_dir=\"" + extensionDir.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"");
                TryAppendExtensionLine(sb, extensionDir, extensionName);
            }

            File.WriteAllText(iniPath, sb.ToString());
        }

        private static bool TryEnableExtension(string extensionDir, string? extensionName, string? iniPath)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || string.IsNullOrWhiteSpace(extensionName))
            {
                return false;
            }

            var sb = new StringBuilder();
            if (File.Exists(iniPath))
            {
                sb.Append(File.ReadAllText(iniPath));
            }

            if (!TryAppendExtensionLine(sb, extensionDir, extensionName))
            {
                return false;
            }

            File.WriteAllText(iniPath, sb.ToString());
            return true;
        }

        private static bool TryAppendExtensionLine(StringBuilder sb, string extensionDir, string? extensionName)
        {
            if (string.IsNullOrWhiteSpace(extensionName))
            {
                return false;
            }

            var dll = OperatingSystem.IsWindows()
                ? "php_" + extensionName + ".dll"
                : extensionName + ".so";
            var candidate = Path.Combine(extensionDir, dll);
            if (!File.Exists(candidate))
            {
                var matches = Directory.GetFiles(extensionDir, "*" + extensionName + "*", SearchOption.TopDirectoryOnly);
                candidate = matches.FirstOrDefault() ?? "";
            }

            if (!File.Exists(candidate))
            {
                return false;
            }

            var line = "extension=" + Path.GetFileName(candidate);
            if (sb.ToString().Contains(line, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            sb.AppendLine(line);
            return true;
        }

        private static void WriteAttribution(
            string patchDir,
            string provider,
            string artifactUrl,
            string version,
            string? sha256)
        {
            var text = string.Join(
                Environment.NewLine,
                [
                    "This directory contains a PHP runtime downloaded by Tyhp for generate_tyhpdef.",
                    "Tyhp does not own PHP, StaticPHP, or Homebrew. See the Tyhp repository THIRD_PARTY.md.",
                    "",
                    "Provider: " + provider,
                    "Version: " + version,
                    "Artifact: " + artifactUrl,
                    string.IsNullOrWhiteSpace(sha256) ? "SHA256: (not published by upstream; HTTPS download + smoke-test)" : "SHA256: " + sha256,
                    "",
                ]);
            File.WriteAllText(Path.Combine(patchDir, "ATTRIBUTION.txt"), text);
        }

        private static void WriteCurrent(string minorDir, string patchSegment)
            => File.WriteAllText(Path.Combine(minorDir, "current.txt"), patchSegment);

        private static string? FindPhpBinary(string root)
        {
            var names = OperatingSystem.IsWindows() ? new[] { "php.exe" } : new[] { "php" };
            foreach (var name in names)
            {
                var direct = Path.Combine(root, name);
                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, OperatingSystem.IsWindows() ? "php.exe" : "php", SearchOption.AllDirectories))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName is "php" or "php.exe")
                    {
                        return file;
                    }
                }
            }
            catch (IOException)
            {
                return null;
            }

            return null;
        }

        private static string? FindExtensionDir(string phpDir)
        {
            var candidates = new[]
            {
                Path.Combine(phpDir, "ext"),
                Path.Combine(phpDir, "lib", "php", "extensions"),
                Path.Combine(Directory.GetParent(phpDir)?.FullName ?? phpDir, "ext"),
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            try
            {
                return Directory.EnumerateDirectories(phpDir, "ext", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void ExtractTarGz(string archivePath, string destination)
        {
            using var file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
        }

        private static void TryChmodExecute(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(
                    path,
                    mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
            {
                // Windows or a filesystem that does not store Unix modes.
            }
        }

        private async Task<string?> TryFetchSha256Async(string artifactUrl, CancellationToken cancellationToken)
        {
            foreach (var suffix in new[] { ".sha256", ".sha256sum" })
            {
                var text = await this._transport.GetStringAsync(new Uri(artifactUrl + suffix), cancellationToken)
                    .ConfigureAwait(false);
                var hash = TryParseSha256(text);
                if (hash is not null)
                {
                    return hash;
                }
            }

            return null;
        }

        private static string? TryParseSha256(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = Regex.Match(text, @"\b[a-fA-F0-9]{64}\b");
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }

        internal static bool TryPickWindowsNtsZip(
            string releasesJson,
            string minor,
            string downloadBase,
            out string url,
            out string version,
            out string? sha256)
        {
            url = "";
            version = "";
            sha256 = null;
            using var doc = JsonDocument.Parse(releasesJson);
            string? bestVersion = null;
            JsonElement best = default;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!property.Name.StartsWith(minor + ".", StringComparison.Ordinal)
                    && !string.Equals(property.Name, minor, StringComparison.Ordinal))
                {
                    continue;
                }

                if (bestVersion is null || PhpRuntimeVersion.ComparePatch(property.Name, bestVersion) > 0)
                {
                    bestVersion = property.Name;
                    best = property.Value;
                }
            }

            if (bestVersion is null || best.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            version = bestVersion;
            if (!TryFindNtsZip(best, downloadBase, out url, out sha256))
            {
                return false;
            }

            return true;
        }

        private static bool TryFindNtsZip(JsonElement node, string downloadBase, out string url, out string? sha256)
        {
            url = "";
            sha256 = null;
            if (node.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (node.TryGetProperty("path", out var pathEl)
                && pathEl.ValueKind == JsonValueKind.String)
            {
                var path = pathEl.GetString() ?? "";
                if (path.Contains("nts", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && (path.Contains("x64", StringComparison.OrdinalIgnoreCase)
                        || path.Contains("x86_64", StringComparison.OrdinalIgnoreCase)))
                {
                    url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? path
                        : downloadBase.TrimEnd('/') + "/" + path.TrimStart('/');
                    if (node.TryGetProperty("sha256", out var shaEl))
                    {
                        sha256 = shaEl.GetString();
                    }

                    return true;
                }
            }

            foreach (var property in node.EnumerateObject())
            {
                if (TryFindNtsZip(property.Value, downloadBase, out url, out sha256))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<Dictionary<string, string>?> CreateGhcrHeadersAsync(
            string blobUrl,
            CancellationToken cancellationToken)
        {
            var repo = "homebrew/core/php";
            var match = Regex.Match(blobUrl, @"ghcr\.io/v2/homebrew/core/([^/]+(?:/[^/]+)*)/blobs/");
            if (match.Success)
            {
                repo = "homebrew/core/" + match.Groups[1].Value;
            }

            var tokenUrl = $"https://ghcr.io/token?service=ghcr.io&scope=repository:{repo}:pull";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "Bearer QQ==",
                ["Accept"] = "application/vnd.oci.image.layer.v1.tar+gzip",
            };
            var tokenJson = await this._transport.GetStringAsync(new Uri(tokenUrl), cancellationToken, headers)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tokenJson))
            {
                return headers;
            }

            try
            {
                using var doc = JsonDocument.Parse(tokenJson);
                if (doc.RootElement.TryGetProperty("token", out var tokenEl))
                {
                    var token = tokenEl.GetString();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        headers["Authorization"] = "Bearer " + token;
                    }
                }
            }
            catch (JsonException)
            {
                // Keep the anonymous Bearer QQ== header.
            }

            return headers;
        }

        private static string? PickBottleTag(string rid, JsonElement files)
        {
            var preferred = BottleTagCandidates(rid);
            foreach (var tag in preferred)
            {
                if (files.TryGetProperty(tag, out _))
                {
                    return tag;
                }
            }

            foreach (var property in files.EnumerateObject())
            {
                if (rid.Contains("arm64", StringComparison.Ordinal)
                    && property.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
                {
                    return property.Name;
                }

                if (rid.Contains("x64", StringComparison.Ordinal)
                    && !property.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                    && (property.Name.Contains("sonoma", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("linux", StringComparison.OrdinalIgnoreCase)))
                {
                    return property.Name;
                }
            }

            return null;
        }

        private static IEnumerable<string> BottleTagCandidates(string rid)
        {
            if (rid == "osx-arm64")
            {
                yield return "arm64_tahoe";
                yield return "arm64_sequoia";
                yield return "arm64_sonoma";
                yield return "arm64_ventura";
                yield break;
            }

            if (rid == "osx-x64")
            {
                yield return "tahoe";
                yield return "sequoia";
                yield return "sonoma";
                yield return "ventura";
                yield break;
            }

            if (rid == "linux-arm64")
            {
                yield return "arm64_linux";
                yield break;
            }

            if (rid == "linux-x64")
            {
                yield return "x86_64_linux";
            }
        }

        private static void MapUnixRid(string rid, out string os, out string arch)
        {
            os = rid.StartsWith("osx", StringComparison.Ordinal) ? "macos" : "linux";
            arch = rid.EndsWith("arm64", StringComparison.Ordinal) ? "aarch64" : "x86_64";
        }

        private static string SanitizeSegment(string value)
        {
            var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray();
            return new string(chars);
        }
    }
}
