using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Generates or updates <c>composer.json</c> in the build output directory for PSR-4 autoloading.
    /// </summary>
    public sealed class ComposerJsonService
    {
        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
        };

        private const string PhpStubsPackage = "tyhp/php";
        private const int MaxDirectorySearchDepth = 10;

        private readonly DiagnosticBag _diagnostics;

        public ComposerJsonService(DiagnosticBag diagnostics)
        {
            this._diagnostics = diagnostics;
        }

        /// <summary>
        /// Merges runtime package <c>require</c> entries into an existing or new <c>composer.json</c>.
        /// </summary>
        public void MergeRuntimePackages(
            string outputDirectory,
            Project project,
            IReadOnlyList<string> requiredPackages)
        {
            if (requiredPackages.Count == 0)
            {
                return;
            }

            var composerPath = Path.Combine(outputDirectory, "composer.json");
            JsonObject root;

            if (File.Exists(composerPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(composerPath);
                    root = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    this._diagnostics.AddError(
                        Domain.Exceptions.MessageCode.BuildFileWriteError,
                        "",
                        0,
                        0,
                        composerPath,
                        ex.Message);
                    return;
                }
            }
            else
            {
                root = new JsonObject
                {
                    ["name"] = DerivePackageName(project),
                };
            }

            MergeRequireSection(root, requiredPackages.ToList());

            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(composerPath, root.ToJsonString(JsonWriteOptions) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this._diagnostics.AddError(
                    Domain.Exceptions.MessageCode.BuildFileWriteError,
                    "",
                    0,
                    0,
                    composerPath,
                    ex.Message);
            }
        }

        /// <summary>
        /// Generates or merges <c>composer.json</c> when <see cref="BuildConfig.UpdateComposer"/> is enabled.
        /// </summary>
        public void GenerateOrUpdate(
            string outputDirectory,
            Project project,
            IReadOnlyList<PHPOutputFile> outputFiles,
            bool dryRun = false,
            EmitContext? emitContext = null)
        {
            if (!project.Build.UpdateComposer)
            {
                return;
            }

            if (dryRun)
            {
                if (project.Build.Verbose)
                {
                    var packages = DetermineRequiredPackages(outputFiles);
                    if (packages.Count > 0)
                    {
                        Message.Display("CLI_VerboseComposerPackagesNeeded", string.Join(", ", packages));
                    }
                }

                return;
            }

            var composerPath = Path.Combine(outputDirectory, "composer.json");
            JsonObject root;

            if (File.Exists(composerPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(composerPath);
                    root = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    this._diagnostics.AddError(
                        Domain.Exceptions.MessageCode.BuildFileWriteError,
                        "",
                        0,
                        0,
                        composerPath,
                        ex.Message);
                    return;
                }
            }
            else
            {
                root = new JsonObject
                {
                    ["name"] = DerivePackageName(project),
                };
            }

            var psr4Mappings = ComputePsr4Mappings(outputFiles, project);
            var functionFiles = ComputeFunctionAutoloadFiles(outputFiles, project);
            var requiredPackages = DetermineRequiredPackages(outputFiles, emitContext);

            MergeAutoloadSection(root, psr4Mappings, functionFiles);
            MergeRequireSection(root, requiredPackages);

            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(composerPath, root.ToJsonString(JsonWriteOptions) + Environment.NewLine);
                if (project.Build.Verbose)
                {
                    Message.Display("CLI_VerboseComposerJsonWritten", composerPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this._diagnostics.AddError(
                    Domain.Exceptions.MessageCode.BuildFileWriteError,
                    "",
                    0,
                    0,
                    composerPath,
                    ex.Message);
            }
        }

        internal static Dictionary<string, string> ComputePsr4Mappings(
            IReadOnlyList<PHPOutputFile> outputFiles,
            Project project)
        {
            var mappings = new Dictionary<string, string>(StringComparer.Ordinal);

            if (project.Build.Psr4 != null)
            {
                foreach (var (ns, path) in project.Build.Psr4)
                {
                    mappings[NormalizePsr4Namespace(ns)] = NormalizePsr4Directory(path);
                }
            }

            foreach (var outputFile in outputFiles.Where(f => f.IsPSR4ObjectDeclaration))
            {
                var namespaceName = GetNamespaceName(outputFile.FileNameSpace);
                if (string.IsNullOrWhiteSpace(namespaceName))
                {
                    continue;
                }

                var relativePath = ToOutputRelativePath(outputFile.OutputFilePath, project.Output.Path);
                var directoryPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    continue;
                }

                var derived = DerivePsr4Mapping(namespaceName, directoryPath);
                if (derived == null)
                {
                    continue;
                }

                var (prefix, directory) = derived.Value;
                if (!mappings.ContainsKey(prefix))
                {
                    mappings[prefix] = directory;
                }
            }

            return mappings;
        }

        internal static List<string> ComputeFunctionAutoloadFiles(
            IReadOnlyList<PHPOutputFile> outputFiles,
            Project project)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var outputFile in outputFiles)
            {
                var relativePath = ToOutputRelativePath(outputFile.OutputFilePath, project.Output.Path);
                if (relativePath.EndsWith("_functions.php", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(relativePath.Replace('\\', '/'));
                }
            }

            return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static List<string> DetermineRequiredPackages(
            IReadOnlyList<PHPOutputFile> outputFiles,
            EmitContext? emitContext = null)
        {
            if (emitContext?.RequiredPackages.Count > 0)
            {
                var fromEmit = new HashSet<string>(emitContext.RequiredPackages, StringComparer.Ordinal)
                {
                    PhpStubsPackage,
                };
                return fromEmit.OrderBy(p => p, StringComparer.Ordinal).ToList();
            }

            // PLACEHOLDER_STORY_11: EmitContext.RequiredPackages provides required runtime package list.
            var packages = new HashSet<string>(StringComparer.Ordinal);
            var combinedContent = string.Join(
                '\n',
                outputFiles
                    .Select(f => f.GeneratedContent)
                    .Where(content => !string.IsNullOrWhiteSpace(content)));

            if (ContainsAny(combinedContent, "Tyhp\\Async", "Tyhp\\Promise", "tyhpAwait", "Tyhp\\Runtime\\Async"))
            {
                packages.Add("tyhp/async");
            }

            if (ContainsAny(combinedContent, "Tyhp\\Decimal", "\\decimal(", "Tyhp\\Runtime\\Decimal"))
            {
                packages.Add("tyhp/decimal");
            }

            if (ContainsAny(
                    combinedContent,
                    "Tyhp\\Core",
                    "DisposableScope",
                    "GenericObject",
                    "tyhpGeneric",
                    "NamedType",
                    "Tyhp\\Runtime\\Core",
                    "ObjectHelper"))
            {
                packages.Add("tyhp/core");
            }

            packages.Add(PhpStubsPackage);

            return packages.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        private static void MergeAutoloadSection(
            JsonObject root,
            Dictionary<string, string> psr4Mappings,
            List<string> functionFiles)
        {
            var autoload = root["autoload"] as JsonObject ?? new JsonObject();
            root["autoload"] = autoload;

            var psr4Node = autoload["psr-4"] as JsonObject ?? new JsonObject();
            foreach (var (ns, directory) in psr4Mappings.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                if (!psr4Node.ContainsKey(ns))
                {
                    psr4Node[ns] = directory;
                }
            }

            autoload["psr-4"] = psr4Node;

            if (functionFiles.Count == 0)
            {
                return;
            }

            var filesNode = autoload["files"] as JsonArray ?? new JsonArray();
            var existing = filesNode
                .Select(GetNodeStringValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in functionFiles)
            {
                if (existing.Add(file))
                {
                    filesNode.Add(file);
                }
            }

            autoload["files"] = filesNode;
        }

        private static void MergeRequireSection(JsonObject root, List<string> requiredPackages)
        {
            var packages = new List<string>(requiredPackages);
            if (!packages.Contains(PhpStubsPackage, StringComparer.Ordinal))
            {
                packages.Add(PhpStubsPackage);
            }

            if (packages.Count == 0)
            {
                return;
            }

            var runtimePackages = BuildRuntimePackagePathMap();
            var require = root["require"] as JsonObject ?? new JsonObject();
            var versionConstraint = GetCompilerVersionConstraint();
            var anyRuntimeRequired = false;

            foreach (var package in packages)
            {
                require[package] = versionConstraint;
                if (runtimePackages.ContainsKey(package))
                {
                    anyRuntimeRequired = true;
                }
            }

            root["require"] = require;

            if (runtimePackages.Count > 0 && anyRuntimeRequired)
            {
                // Register every runtime package found on disk as a path repository — not only the
                // directly-required ones — so transitive tyhp/* dependencies also resolve locally.
                var pathRepositories = runtimePackages
                    .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                    .Select(kvp => (kvp.Key, kvp.Value))
                    .ToList();
                MergeRepositoriesSection(root, pathRepositories);
                ApplyPrereleaseStability(root, versionConstraint);
            }
            else
            {
                ApplyPrereleaseStability(root, versionConstraint);
            }
        }

        /// <summary>
        /// Relaxes Composer stability so prerelease <c>tyhp/*</c> packages resolve. Existing
        /// user-authored values are preserved.
        /// </summary>
        private static void ApplyPrereleaseStability(JsonObject root, string versionConstraint)
        {
            var needsPrerelease = versionConstraint.Contains('-', StringComparison.Ordinal);
            if (!needsPrerelease)
            {
                return;
            }

            if (!root.ContainsKey("minimum-stability"))
            {
                root["minimum-stability"] = "alpha";
            }

            if (!root.ContainsKey("prefer-stable"))
            {
                root["prefer-stable"] = true;
            }
        }

        private static void MergeRepositoriesSection(
            JsonObject root,
            List<(string Name, string Directory)> pathRepositories)
        {
            if (pathRepositories.Count == 0)
            {
                return;
            }

            // Preserve an existing object-form "repositories" map (rare, user-authored) instead of
            // clobbering it; add keyed path entries when not already present.
            if (root["repositories"] is JsonObject repositoryObject)
            {
                foreach (var (name, directory) in pathRepositories)
                {
                    var key = name.Replace('/', '-');
                    if (!repositoryObject.ContainsKey(key))
                    {
                        repositoryObject[key] = CreatePathRepository(directory);
                    }
                }

                return;
            }

            var repositories = root["repositories"] as JsonArray ?? new JsonArray();
            var existingUrls = repositories
                .OfType<JsonObject>()
                .Select(repo => GetNodeStringValue(repo["url"]))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url!.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (_, directory) in pathRepositories)
            {
                var url = directory.Replace('\\', '/');
                if (existingUrls.Add(url))
                {
                    repositories.Add(CreatePathRepository(directory));
                }
            }

            root["repositories"] = repositories;
        }

        private static JsonObject CreatePathRepository(string directory) => new()
        {
            ["type"] = "path",
            ["url"] = directory.Replace('\\', '/'),
        };

        /// <summary>
        /// Exposes the runtime package path map to sibling services (e.g. the interop
        /// contract-version check in <see cref="TyhpLibDistributionService"/>).
        /// </summary>
        internal static Dictionary<string, string> GetRuntimePackagePathMap()
            => BuildRuntimePackagePathMap();

        /// <summary>
        /// Maps each on-disk runtime package's Composer <c>name</c> (e.g. <c>tyhp/core</c>) to its
        /// absolute directory under <c>runtime/packages/</c>, or an empty map when the runtime
        /// package root cannot be located (the build then proceeds without path repositories).
        /// </summary>
        private static Dictionary<string, string> BuildRuntimePackagePathMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var runtimePackagesRoot = ResolveRuntimePackagesRoot();
            if (runtimePackagesRoot == null)
            {
                return map;
            }

            foreach (var packageDirectory in Directory.EnumerateDirectories(runtimePackagesRoot))
            {
                var manifestPath = Path.Combine(packageDirectory, "composer.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var packageName = ReadComposerPackageName(manifestPath);
                if (!string.IsNullOrWhiteSpace(packageName) && !map.ContainsKey(packageName!))
                {
                    map[packageName!] = Path.GetFullPath(packageDirectory);
                }
            }

            return map;
        }

        private static string? ReadComposerPackageName(string manifestPath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String)
                {
                    return nameElement.GetString();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }

            return null;
        }

        /// <summary>
        /// Locates the compiler's <c>runtime/packages/</c> directory by walking upward from the
        /// running assembly's base directory and the current working directory — the same roots the
        /// tyhpdef loader uses to discover runtime packages.
        /// </summary>
        private static string? ResolveRuntimePackagesRoot()
        {
            foreach (var startDirectory in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var resolved = SearchUpwardForRuntimePackages(startDirectory);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        }

        private static string? SearchUpwardForRuntimePackages(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                return null;
            }

            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
            }
            catch (Exception)
            {
                return null;
            }

            var depth = 0;
            while (directory != null && depth++ < MaxDirectorySearchDepth)
            {
                var candidate = Path.Combine(directory.FullName, "runtime", "packages");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static (string Prefix, string Directory)? DerivePsr4Mapping(string namespaceName, string directoryPath)
        {
            var namespaceParts = namespaceName
                .Trim('\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var directoryParts = directoryPath
                .Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (namespaceParts.Length == 0 || directoryParts.Length == 0)
            {
                return null;
            }

            var matchingSegments = 0;
            var maxSegments = Math.Min(namespaceParts.Length, directoryParts.Length);
            for (var i = 1; i <= maxSegments; i++)
            {
                var nsSegment = namespaceParts[^i];
                var dirSegment = directoryParts[^i];
                if (!string.Equals(nsSegment, dirSegment, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                matchingSegments = i;
            }

            if (matchingSegments == 0)
            {
                return (namespaceParts[0] + "\\", directoryParts[0] + "/");
            }

            var prefixParts = namespaceParts[..^matchingSegments];
            var dirPrefixParts = directoryParts[..^matchingSegments];

            var prefix = prefixParts.Length == 0
                ? ""
                : string.Join("\\", prefixParts) + "\\";
            var directory = dirPrefixParts.Length == 0
                ? ""
                : string.Join("/", dirPrefixParts) + "/";

            return (prefix, directory);
        }

        private static string DerivePackageName(Project project)
        {
            var projectPath = project.GetProjectPath();
            var directoryName = new DirectoryInfo(projectPath).Name
                .ToLowerInvariant()
                .Replace(' ', '-')
                .Replace('_', '-');
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                directoryName = "app";
            }

            return $"tyhp/{directoryName}";
        }

        private static string GetCompilerVersionConstraint()
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            var version = StripBuildMetadata(informational);
            if (string.IsNullOrWhiteSpace(version))
            {
                return "805.0.0-alpha.1";
            }

            return version;
        }

        internal static string? StripBuildMetadata(string? informationalVersion)
        {
            if (string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }

            var plus = informationalVersion.IndexOf('+');
            return plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        }

        private static string? GetNamespaceName(object? namespaceStatement)
            => namespaceStatement switch
            {
                PhpNamespaceDeclAst ns => ns.Identifier,
                PhpBlockNamespaceDeclAst block => block.Identifier,
                _ => null,
            };

        private static string ToOutputRelativePath(string outputFilePath, string outputPathPrefix)
        {
            var normalized = outputFilePath.Replace('\\', '/');
            var prefix = outputPathPrefix.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return normalized;
            }

            if (normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized[(prefix.Length + 1)..];
            }

            return normalized;
        }

        private static string NormalizePsr4Namespace(string ns)
        {
            var trimmed = ns.Trim().Trim('\\');
            return string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed + "\\";
        }

        private static string NormalizePsr4Directory(string path)
        {
            var normalized = path.Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            return normalized.EndsWith('/') ? normalized : normalized + "/";
        }

        private static string? GetNodeStringValue(JsonNode? node)
        {
            // Defensive: a user-authored composer.json may carry non-string entries in
            // "autoload.files"; TryGetValue avoids the throw that GetValue<string>() raises
            // for non-string nodes, so a malformed file cannot crash the build.
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text;
            }

            return null;
        }

        private static bool ContainsAny(string content, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (content.Contains(needle, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
