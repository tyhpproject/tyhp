using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tyhp.CLI;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Config
{
    /// <summary>
    /// Expands <c>{name}</c>-style placeholders in interpolatable <c>tyhp.json</c> strings.
    /// Composer identity is read from <c>composer.json</c> next to <c>tyhp.json</c> only when
    /// a Composer-sourced variable is used.
    /// </summary>
    internal static class ConfigInterpolator
    {
        internal const string FileNameVar = "fileName";
        internal const string FileStemVar = "fileStem";
        internal const string ExtensionVar = "extension";
        internal const string RecursiveDirVar = "recursiveDir";
        internal const string RelativePathVar = "relativePath";

        private static readonly Regex PlaceholderRegex = new(
            @"\{\{|\}\}|\{([^{}]+)\}",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> FileVariables = new(StringComparer.Ordinal)
        {
            FileNameVar,
            FileStemVar,
            ExtensionVar,
            RecursiveDirVar,
            RelativePathVar,
        };

        private static readonly HashSet<string> ComposerVariables = new(StringComparer.Ordinal)
        {
            "name",
            "name.vendor",
            "name.package",
            "name.slug",
            "version",
            "projectVersion",
            "version.major",
            "version.minor",
            "version.patch",
            "version.suffix",
            "composer.type",
        };

        private static readonly Regex ComposerVersionRegex = new(
            @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(.*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// True when <paramref name="value"/> still needs the copy-time expansion pass: an
        /// unresolved file-variable placeholder, or a doubled brace left deferred by the
        /// config-time pass (see <see cref="TryExpand"/>). Config-time interpolation leaves both
        /// alone in <c>dst</c> so a name like <c>{{fileName}}</c> unescapes to literal
        /// <c>{fileName}</c> exactly once, at copy time, instead of being unescaped early and then
        /// mistaken for a real placeholder.
        /// </summary>
        internal static bool ContainsFileVariables(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (Match match in PlaceholderRegex.Matches(value))
            {
                if (match.Value is "{{" or "}}")
                {
                    return true;
                }

                if (FileVariables.Contains(match.Groups[1].Value.Trim()))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void Apply(Project project, Action<MessageCode, object[]> reportError)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(reportError);

            var state = new State(project, reportError);

            project.CacheDir = ExpandKey(state, project.CacheDir, "cache-dir", allowFileVars: false);

            project.Output.Path = ExpandKey(state, project.Output.Path, "output.path", allowFileVars: false)
                ?? project.Output.Path;
            project.Output.PublishPath = ExpandKey(
                state,
                project.Output.PublishPath,
                "output.publishPath",
                allowFileVars: false) ?? project.Output.PublishPath;

            if (project.Output.PublishContent.Count > 0)
            {
                var entries = new List<PublishContentEntry>(project.Output.PublishContent.Count);
                for (var i = 0; i < project.Output.PublishContent.Count; i++)
                {
                    entries.Add(ExpandEntry(state, project.Output.PublishContent[i], i));
                }

                project.Output.PublishContent = entries;
            }

            ExpandDictionaryValues(state, project.Build.Psr4, "psr4");
            ExpandList(state, project.Build.Psr4Includes, "psr4Includes");
            ExpandDictionaryValues(state, project.Build.EntryPointAutoloader, "build.entryPointAutoloader");

            project.XDebugProxy.SourceMapDirectory = ExpandKey(
                state,
                project.XDebugProxy.SourceMapDirectory,
                "xdebugProxy.sourceMapDir",
                allowFileVars: false);
            project.XDebugProxy.TyhpSourceRoot = ExpandKey(
                state,
                project.XDebugProxy.TyhpSourceRoot,
                "xdebugProxy.tyhpSourceRoot",
                allowFileVars: false);
            project.XDebugProxy.PhpOutputRoot = ExpandKey(
                state,
                project.XDebugProxy.PhpOutputRoot,
                "xdebugProxy.phpOutputRoot",
                allowFileVars: false);
        }

        internal static bool TryExpandFileDestination(
            string template,
            string sourceFullPath,
            string relativeDestPath,
            string configKey,
            Action<MessageCode, object[]> reportError,
            out string expanded)
        {
            expanded = template;
            var fileValues = BuildFileValues(sourceFullPath, relativeDestPath);
            return TryExpand(
                template,
                configKey,
                allowFileVars: true,
                leaveFilePlaceholders: false,
                fileValues,
                composer: null,
                reportError,
                out expanded);
        }

        /// <summary>
        /// Converts <c>output.phpVersion</c> (e.g. <c>8.2</c>, <c>8.6</c>) to the Tyhp PHP id
        /// (<c>802</c>, <c>806</c>): major digit(s) plus a two-digit minor.
        /// </summary>
        internal static string ToPhpId(string phpVersion)
        {
            var parts = phpVersion.Trim().Split('.', 3);
            if (parts.Length == 0 || !int.TryParse(parts[0], out var major))
            {
                return phpVersion.Trim();
            }

            var minor = 0;
            if (parts.Length >= 2)
            {
                var minorToken = parts[1];
                var digits = 0;
                while (digits < minorToken.Length && char.IsDigit(minorToken[digits]))
                {
                    digits++;
                }

                if (digits > 0)
                {
                    int.TryParse(minorToken[..digits], out minor);
                }
            }

            return $"{major}{minor:D2}";
        }

        internal static string VersionSlice(string version, int index)
        {
            var parts = version.Trim().Split('.', StringSplitOptions.None);
            return index < parts.Length && !string.IsNullOrEmpty(parts[index])
                ? parts[index]
                : "0";
        }

        /// <summary>
        /// Splits a Composer version into numeric major/minor/patch and the remainder after patch
        /// (prerelease, build metadata, extra dotted parts). A missing numeric slice is <c>"0"</c>.
        /// </summary>
        internal static (string Major, string Minor, string Patch, string Suffix) SplitComposerVersion(
            string version)
        {
            var match = ComposerVersionRegex.Match(version.Trim());
            if (!match.Success)
            {
                return (VersionSlice(version, 0), VersionSlice(version, 1), VersionSlice(version, 2), "");
            }

            return (
                match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : "0",
                match.Groups[3].Success ? match.Groups[3].Value : "0",
                match.Groups[4].Value);
        }

        private static PublishContentEntry ExpandEntry(State state, PublishContentEntry entry, int index)
        {
            var src = ExpandListItems(state, entry.Src, $"output.publishContent[{index}].src", allowFileVars: false);
            var exclude = ExpandListItems(
                state,
                entry.Exclude,
                $"output.publishContent[{index}].exclude",
                allowFileVars: false);
            var dst = ExpandKey(
                state,
                entry.Dst,
                $"output.publishContent[{index}].dst",
                allowFileVars: true) ?? entry.Dst;

            return new PublishContentEntry
            {
                Src = src,
                Dst = dst,
                Exclude = exclude,
                Flat = entry.Flat,
                Preserve = entry.Preserve,
                SkipUnchanged = entry.SkipUnchanged,
                OverwriteReadOnly = entry.OverwriteReadOnly,
                Required = entry.Required,
                FollowSymlinks = entry.FollowSymlinks,
                DstForcedDirectory = entry.DstForcedDirectory,
            };
        }

        private static void ExpandDictionaryValues(
            State state,
            Dictionary<string, string>? map,
            string keyPrefix)
        {
            if (map == null || map.Count == 0)
            {
                return;
            }

            foreach (var key in map.Keys.ToList())
            {
                var expanded = ExpandKey(state, map[key], $"{keyPrefix}.{key}", allowFileVars: false);
                if (expanded != null)
                {
                    map[key] = expanded;
                }
            }
        }

        private static void ExpandList(State state, List<string>? list, string keyPrefix)
        {
            if (list == null || list.Count == 0)
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var expanded = ExpandKey(state, list[i], $"{keyPrefix}[{i}]", allowFileVars: false);
                if (expanded != null)
                {
                    list[i] = expanded;
                }
            }
        }

        private static List<string> ExpandListItems(
            State state,
            IReadOnlyList<string> items,
            string keyPrefix,
            bool allowFileVars)
        {
            if (items.Count == 0)
            {
                return [];
            }

            var result = new List<string>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                result.Add(
                    ExpandKey(state, items[i], $"{keyPrefix}[{i}]", allowFileVars) ?? items[i]);
            }

            return result;
        }

        private static string? ExpandKey(State state, string? value, string configKey, bool allowFileVars)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('{') < 0)
            {
                return value;
            }

            if (!TryExpand(
                    value,
                    configKey,
                    allowFileVars,
                    leaveFilePlaceholders: allowFileVars,
                    fileValues: null,
                    composer: state,
                    state.ReportError,
                    out var expanded))
            {
                return value;
            }

            return expanded;
        }

        private static bool TryExpand(
            string template,
            string configKey,
            bool allowFileVars,
            bool leaveFilePlaceholders,
            IReadOnlyDictionary<string, string>? fileValues,
            State? composer,
            Action<MessageCode, object[]> reportError,
            out string expanded)
        {
            var builder = new StringBuilder(template.Length);
            var lastIndex = 0;
            var ok = true;

            foreach (Match match in PlaceholderRegex.Matches(template))
            {
                builder.Append(template, lastIndex, match.Index - lastIndex);
                lastIndex = match.Index + match.Length;

                if (match.Value == "{{")
                {
                    // Deferred alongside file placeholders (see ContainsFileVariables) so a
                    // literal `{fileName}` written as `{{fileName}}` survives config-time
                    // expansion undoubled and is only unescaped once, at copy time.
                    builder.Append(leaveFilePlaceholders ? "{{" : "{");
                    continue;
                }

                if (match.Value == "}}")
                {
                    builder.Append(leaveFilePlaceholders ? "}}" : "}");
                    continue;
                }

                var name = match.Groups[1].Value.Trim();
                if (FileVariables.Contains(name))
                {
                    if (!allowFileVars)
                    {
                        reportError(
                            MessageCode.ConfigInterpolationFailed,
                            [name, configKey, "file variables are only allowed in output.publishContent dst"]);
                        ok = false;
                        builder.Append(match.Value);
                        continue;
                    }

                    if (leaveFilePlaceholders && fileValues == null)
                    {
                        builder.Append(match.Value);
                        continue;
                    }

                    if (fileValues == null || !fileValues.TryGetValue(name, out var fileValue))
                    {
                        reportError(
                            MessageCode.ConfigInterpolationFailed,
                            [name, configKey, "file variable is not available"]);
                        ok = false;
                        builder.Append(match.Value);
                        continue;
                    }

                    if (!TrySanitize(name, fileValue, reportError, configKey, out var safeFile))
                    {
                        ok = false;
                        builder.Append(match.Value);
                        continue;
                    }

                    builder.Append(safeFile);
                    continue;
                }

                if (composer == null)
                {
                    reportError(
                        MessageCode.ConfigInterpolationFailed,
                        [name, configKey, "unknown variable"]);
                    ok = false;
                    builder.Append(match.Value);
                    continue;
                }

                if (!composer.TryResolve(name, out var value, out var reason))
                {
                    reportError(
                        MessageCode.ConfigInterpolationFailed,
                        [name, configKey, reason ?? "unknown variable"]);
                    ok = false;
                    builder.Append(match.Value);
                    continue;
                }

                if (!TrySanitize(name, value, reportError, configKey, out var safe))
                {
                    ok = false;
                    builder.Append(match.Value);
                    continue;
                }

                builder.Append(safe);
            }

            builder.Append(template, lastIndex, template.Length - lastIndex);
            expanded = builder.ToString();
            return ok;
        }

        private static Dictionary<string, string> BuildFileValues(string sourceFullPath, string relativeDestPath)
        {
            var relative = relativeDestPath.Replace('\\', '/').Trim('/');
            var fileName = Path.GetFileName(relative);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Path.GetFileName(sourceFullPath);
            }

            var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FileNameVar] = fileName,
                [FileStemVar] = Path.GetFileNameWithoutExtension(fileName),
                [ExtensionVar] = Path.GetExtension(fileName),
                [RecursiveDirVar] = directory,
                [RelativePathVar] = relative,
            };
        }

        private static bool TrySanitize(
            string variable,
            string value,
            Action<MessageCode, object[]> reportError,
            string configKey,
            out string sanitized)
        {
            sanitized = value;
            // `{recursiveDir}` is empty for a match with no subdirectory. `{extension}` is empty
            // when the file has no extension. `{fileStem}` is empty for dotfiles such as
            // `.gitignore` (the name is all extension). `{version.suffix}` is empty when omitted.
            var allowEmpty = variable is RecursiveDirVar or ExtensionVar or FileStemVar or "version.suffix";
            if (string.IsNullOrEmpty(value))
            {
                if (allowEmpty)
                {
                    return true;
                }

                reportError(
                    MessageCode.ConfigInterpolationFailed,
                    [variable, configKey, "value is empty"]);
                return false;
            }

            if (value.Contains('\0', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
            {
                reportError(
                    MessageCode.ConfigInterpolationFailed,
                    [variable, configKey, "value contains an invalid path character"]);
                return false;
            }

            var allowSlash = variable is "name" or RecursiveDirVar or RelativePathVar;
            if (!allowSlash && value.Contains('/', StringComparison.Ordinal))
            {
                reportError(
                    MessageCode.ConfigInterpolationFailed,
                    [variable, configKey, "value contains `/`"]);
                return false;
            }

            var segments = value.Split('/', StringSplitOptions.None);
            if (variable == "name" && segments.Length > 2)
            {
                reportError(
                    MessageCode.ConfigInterpolationFailed,
                    [variable, configKey, "`name` may contain at most one `/` (vendor/package)"]);
                return false;
            }

            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
                {
                    reportError(
                        MessageCode.ConfigInterpolationFailed,
                        [variable, configKey, "value contains an invalid path segment"]);
                    return false;
                }
            }

            return true;
        }

        private sealed class State
        {
            private readonly Project _project;
            private ComposerIdentity? _composer;
            private bool _composerLoadAttempted;
            private string? _composerLoadError;

            public State(Project project, Action<MessageCode, object[]> reportError)
            {
                this._project = project;
                this.ReportError = reportError;
            }

            public Action<MessageCode, object[]> ReportError { get; }

            public bool TryResolve(string name, out string value, out string? reason)
            {
                value = "";
                reason = null;

                if (name.StartsWith("env.", StringComparison.Ordinal))
                {
                    var envName = name[4..];
                    if (string.IsNullOrWhiteSpace(envName))
                    {
                        reason = "`env.` requires a variable name";
                        return false;
                    }

                    var envValue = Environment.GetEnvironmentVariable(envName);
                    if (envValue is null)
                    {
                        reason = $"environment variable `{envName}` is not set";
                        return false;
                    }

                    value = envValue;
                    return true;
                }

                if (ComposerVariables.Contains(name))
                {
                    if (!this.TryGetComposer(out var identity, out reason))
                    {
                        return false;
                    }

                    return identity.TryGet(name, out value, out reason);
                }

                switch (name)
                {
                    case "type":
                        value = this._project.Type == ProjectType.Library ? "library" : "application";
                        return true;
                    case "phpVersion":
                        value = this._project.Output.PhpVersion;
                        return true;
                    case "phpVersion.major":
                        value = VersionSlice(this._project.Output.PhpVersion, 0);
                        return true;
                    case "phpVersion.minor":
                        value = VersionSlice(this._project.Output.PhpVersion, 1);
                        return true;
                    case "phpVersion.id":
                        value = ToPhpId(this._project.Output.PhpVersion);
                        return true;
                    case "profile":
                        value = string.IsNullOrWhiteSpace(this._project.Build.Profile)
                            ? "debug"
                            : this._project.Build.Profile!;
                        return true;
                    case "tyhpVersion":
                        value = new Message.VersionHelper().GetAssemblyVersion();
                        if (string.IsNullOrEmpty(value))
                        {
                            reason = "compiler version is unavailable";
                            return false;
                        }

                        return true;
                    default:
                        reason = "unknown variable";
                        return false;
                }
            }

            private bool TryGetComposer(out ComposerIdentity identity, out string? reason)
            {
                identity = _composer!;
                reason = _composerLoadError;
                if (this._composerLoadAttempted)
                {
                    return this._composer != null;
                }

                this._composerLoadAttempted = true;
                var path = Path.Combine(this._project.GetProjectPath(), "composer.json");
                if (!File.Exists(path))
                {
                    this._composerLoadError = "composer.json was not found next to tyhp.json";
                    reason = this._composerLoadError;
                    return false;
                }

                try
                {
                    var json = File.ReadAllText(path);
                    if (!ComposerIdentity.TryParse(json, out identity, out this._composerLoadError))
                    {
                        reason = this._composerLoadError;
                        return false;
                    }

                    this._composer = identity;
                    reason = null;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    this._composerLoadError = ex.Message;
                    reason = this._composerLoadError;
                    return false;
                }
            }
        }
    }

    internal sealed class ComposerIdentity
    {
        public string? Name { get; init; }

        public string? Version { get; init; }

        public string? Type { get; init; }

        public static bool TryParse(string json, out ComposerIdentity identity, out string? error)
        {
            identity = new ComposerIdentity();
            error = null;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                error = $"composer.json is invalid JSON ({ex.Message})";
                return false;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = "composer.json root value is not an object";
                    return false;
                }

                identity = new ComposerIdentity
                {
                    Name = ReadString(document.RootElement, "name"),
                    Version = ReadString(document.RootElement, "version"),
                    Type = ReadString(document.RootElement, "type"),
                };
            }

            return true;
        }

        public bool TryGet(string name, out string value, out string? reason)
        {
            value = "";
            reason = null;
            switch (name)
            {
                case "name":
                    return this.Require(this.Name, "name", out value, out reason);
                case "name.vendor":
                    if (!this.Require(this.Name, "name", out var fullName, out reason))
                    {
                        return false;
                    }

                    value = SplitName(fullName).Vendor;
                    return true;
                case "name.package":
                    if (!this.Require(this.Name, "name", out var pkgName, out reason))
                    {
                        return false;
                    }

                    value = SplitName(pkgName).Package;
                    return true;
                case "name.slug":
                    if (!this.Require(this.Name, "name", out var slugName, out reason))
                    {
                        return false;
                    }

                    value = slugName.Replace('/', '-');
                    return true;
                case "version":
                case "projectVersion":
                    return this.Require(this.Version, "version", out value, out reason);
                case "version.major":
                case "version.minor":
                case "version.patch":
                case "version.suffix":
                    if (!this.Require(this.Version, "version", out var ver, out reason))
                    {
                        return false;
                    }

                    var parts = ConfigInterpolator.SplitComposerVersion(ver);
                    value = name switch
                    {
                        "version.major" => parts.Major,
                        "version.minor" => parts.Minor,
                        "version.patch" => parts.Patch,
                        _ => parts.Suffix,
                    };
                    return true;
                case "composer.type":
                    return this.Require(this.Type, "type", out value, out reason);
                default:
                    reason = "unknown variable";
                    return false;
            }
        }

        private bool Require(string? field, string fieldName, out string value, out string? reason)
        {
            value = field ?? "";
            if (string.IsNullOrWhiteSpace(field))
            {
                reason = $"composer.json has no `{fieldName}` string";
                return false;
            }

            reason = null;
            return true;
        }

        private static (string Vendor, string Package) SplitName(string name)
        {
            var slash = name.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0 || slash == name.Length - 1)
            {
                return (name, name);
            }

            return (name[..slash], name[(slash + 1)..]);
        }

        private static string? ReadString(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out var element))
            {
                return null;
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                _ => null,
            };
        }
    }
}
