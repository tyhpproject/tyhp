using System.Text.Encodings.Web;
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
    /// Generates or updates <c>composer.json</c> at the published package root for PSR-4 autoloading.
    /// </summary>
    public sealed class ComposerJsonService
    {
        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private const string PhpStubsPackage = "tyhpdef/php";
        private const int MaxDirectorySearchDepth = 10;

        /// <summary>
        /// Runtime package source checkout. A repository root (with a <c>packages/</c> child)
        /// or the <c>packages</c> directory itself.
        /// </summary>
        internal const string RuntimeSrcEnvironmentVariable = "TYHP_RUNTIME_SRC";

        private static readonly object RuntimePackagesRootGate = new();
        private static string? _cachedRuntimePackagesRoot;
        private static string? _cachedRuntimePackagesKey;

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

            MergeRequireSection(root, requiredPackages.ToList(), project);

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

            var psr4Mappings = ComputePsr4Mappings(outputFiles, project, outputDirectory);
            var functionFiles = ComputeFunctionAutoloadFiles(outputFiles, project, outputDirectory);
            var requiredPackages = DetermineRequiredPackages(outputFiles, emitContext);

            MergeAutoloadSection(root, psr4Mappings, functionFiles);
            MergeRequireSection(root, requiredPackages, project);

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
            Project project,
            string composerDirectory)
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

                var relativePath = ToComposerRelativePath(
                    outputFile.OutputFilePath,
                    composerDirectory,
                    project.GetProjectPath());
                var directoryPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
                if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == ".")
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
            Project project,
            string composerDirectory)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var outputFile in outputFiles)
            {
                var relativePath = ToComposerRelativePath(
                    outputFile.OutputFilePath,
                    composerDirectory,
                    project.GetProjectPath());
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

        private static void MergeRequireSection(JsonObject root, List<string> requiredPackages, Project project)
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
            var anyRuntimeRequired = false;
            var anyPrerelease = false;

            foreach (var package in packages)
            {
                var sourceVersion = ResolvePackageSourceVersion(package, runtimePackages);
                // Path repos use the source composer.json version.
                // Packagist: tyhpdef/* keeps that version; tyhp/* runtime helpers are 80N.X.Y.
                var constraint = runtimePackages.ContainsKey(package)
                    ? sourceVersion
                    : ResolvePackagistConstraint(package, project.PhpVersion, sourceVersion);
                require[package] = constraint;
                anyPrerelease = anyPrerelease || constraint.Contains('-', StringComparison.Ordinal);
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
            }

            if (anyPrerelease)
            {
                ApplyPrereleaseStability(root);
            }
        }

        /// <summary>
        /// Relaxes Composer stability so prerelease <c>tyhp/*</c> packages resolve. Existing
        /// user-authored values are preserved.
        /// </summary>
        private static void ApplyPrereleaseStability(JsonObject root)
        {
            if (!root.ContainsKey("minimum-stability"))
            {
                root["minimum-stability"] = "alpha";
            }

            if (!root.ContainsKey("prefer-stable"))
            {
                root["prefer-stable"] = true;
            }
        }

        private static string ResolvePackageSourceVersion(
            string packageName,
            IReadOnlyDictionary<string, string> runtimePackages)
        {
            if (runtimePackages.TryGetValue(packageName, out var directory))
            {
                var fromDisk = RuntimePackageVersions.TryReadComposerVersion(directory);
                if (!string.IsNullOrWhiteSpace(fromDisk))
                {
                    return fromDisk;
                }
            }

            return RuntimePackageVersions.ForPackage(packageName);
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
        /// Locates the runtime package tree for path repositories and PHP-source harvest
        /// catalog indexing. <see cref="RuntimeSrcEnvironmentVariable"/> when it points at a
        /// checkout, otherwise a sibling <c>../tyhp-runtime-src/packages</c>. Does not
        /// require an in-tree <c>runtime/packages</c> directory.
        /// </summary>
        internal static string? TryResolveRuntimePackagesRoot()
        {
            var env = Environment.GetEnvironmentVariable(RuntimeSrcEnvironmentVariable) ?? "";
            var cwd = Directory.GetCurrentDirectory();
            var key = env + "\0" + cwd + "\0" + AppContext.BaseDirectory;
            lock (RuntimePackagesRootGate)
            {
                if (string.Equals(_cachedRuntimePackagesKey, key, StringComparison.Ordinal))
                {
                    return _cachedRuntimePackagesRoot;
                }

                var resolved = ResolveRuntimePackagesRoot(env, [AppContext.BaseDirectory, cwd]);
                _cachedRuntimePackagesKey = key;
                _cachedRuntimePackagesRoot = resolved;
                return resolved;
            }
        }

        /// <summary>
        /// Same resolution as <see cref="TryResolveRuntimePackagesRoot"/> with an explicit
        /// environment value and start directories (tests).
        /// </summary>
        internal static string? ResolveRuntimePackagesRoot(
            string? runtimeSrcEnvironment,
            IReadOnlyList<string> startDirectories)
        {
            var fromEnv = TryResolveFromEnvironment(runtimeSrcEnvironment);
            if (fromEnv != null)
            {
                return fromEnv;
            }

            foreach (var startDirectory in startDirectories)
            {
                var resolved = SearchUpwardForRuntimePackages(startDirectory);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        }

        /// <summary>
        /// Require / suggest / require-dev package names from a Composer manifest
        /// (the PHP package being wrapped, not the <c>tyhp/*</c> wrapper).
        /// </summary>
        internal sealed class ComposerDependencySet
        {
            public string Name { get; init; } = "";

            public HashSet<string> Require { get; } = new(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> Suggest { get; } = new(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> RequireDev { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Package names from <c>extra.tyhp.require</c> (ambient for compiled-library consumers).
            /// </summary>
            public HashSet<string> ExtraTyhpRequire { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads <c>require</c>, <c>suggest</c>, <c>require-dev</c>, and
        /// <c>extra.tyhp.require</c> keys from <paramref name="composerJsonPath"/>.
        /// Returns null when the file is missing or not an object.
        /// </summary>
        internal static ComposerDependencySet? TryReadDependencySet(string composerJsonPath)
        {
            if (string.IsNullOrWhiteSpace(composerJsonPath) || !File.Exists(composerJsonPath))
            {
                return null;
            }

            JsonObject? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(composerJsonPath))?.AsObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }

            if (root is null)
            {
                return null;
            }

            var set = new ComposerDependencySet
            {
                Name = GetNodeStringValue(root["name"]) ?? "",
            };
            FillNameSet(root["require"] as JsonObject, set.Require);
            FillNameSet(root["suggest"] as JsonObject, set.Suggest);
            FillNameSet(root["require-dev"] as JsonObject, set.RequireDev);
            var extraTyhp = (root["extra"] as JsonObject)?["tyhp"] as JsonObject;
            FillNameSet(extraTyhp?["require"] as JsonObject, set.ExtraTyhpRequire);
            return set;
        }

        /// <summary>
        /// Adds missing <c>require</c> entries on a <c>tyhp/*</c> wrapper
        /// <c>composer.json</c>. Existing constraints are kept. Does not encode
        /// Packagist 80N versions or rewrite repositories (Story 21.5 owns that).
        /// </summary>
        internal static void EnsureRequireEntries(
            string composerJsonPath,
            IReadOnlyDictionary<string, string> packages)
        {
            if (string.IsNullOrWhiteSpace(composerJsonPath)
                || packages is null
                || packages.Count == 0
                || !File.Exists(composerJsonPath))
            {
                return;
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(composerJsonPath))?.AsObject()
                    ?? new JsonObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return;
            }

            var require = root["require"] as JsonObject ?? new JsonObject();
            var changed = false;
            foreach (var (package, constraint) in packages)
            {
                if (string.IsNullOrWhiteSpace(package) || require.ContainsKey(package))
                {
                    continue;
                }

                require[package] = string.IsNullOrWhiteSpace(constraint) ? "*" : constraint;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            root["require"] = require;
            try
            {
                File.WriteAllText(composerJsonPath, root.ToJsonString(JsonWriteOptions) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        internal const string TyhpdefDevConstraint = "@dev";

        /// <summary>
        /// Puts <c>tyhpdef/*</c> packages in <c>require-dev</c> and
        /// <c>extra.tyhp.require</c> as <c>@dev</c>. Removes them from
        /// <c>require</c> (PHP-source harvest used to pin <c>tyhpdef/php</c> there with
        /// the stubs package version, e.g. <c>0.0.1</c>). Adds a path
        /// repository when that package is present under the resolved runtime
        /// packages root (<c>TYHP_RUNTIME_SRC</c>, then a sibling
        /// <c>tyhp-runtime-src/packages</c>).
        /// </summary>
        internal static void EnsureTyhpdefRequireDevEntries(
            string composerJsonPath,
            IReadOnlyCollection<string> packages)
        {
            if (string.IsNullOrWhiteSpace(composerJsonPath)
                || packages is null
                || packages.Count == 0
                || !File.Exists(composerJsonPath))
            {
                return;
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(composerJsonPath))?.AsObject()
                    ?? new JsonObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return;
            }

            var require = root["require"] as JsonObject ?? new JsonObject();
            var requireDev = GetOrCreateObject(root, "require-dev");
            var extraRequire = GetOrCreateExtraTyhpRequire(root);
            if (requireDev is null)
            {
                return;
            }

            var changed = false;
            var pathRepositories = new List<(string Name, string Directory)>();
            var runtimePackages = GetRuntimePackagePathMap();
            var wrapperDirectory = Path.GetDirectoryName(Path.GetFullPath(composerJsonPath));

            foreach (var package in packages)
            {
                if (string.IsNullOrWhiteSpace(package))
                {
                    continue;
                }

                if (require.ContainsKey(package))
                {
                    require.Remove(package);
                    changed = true;
                }

                if (SetConstraint(requireDev, package, TyhpdefDevConstraint))
                {
                    changed = true;
                }

                if (extraRequire is not null && SetConstraint(extraRequire, package, TyhpdefDevConstraint))
                {
                    changed = true;
                }

                if (runtimePackages.TryGetValue(package, out var directory)
                    && !string.IsNullOrWhiteSpace(wrapperDirectory))
                {
                    var relative = Path.GetRelativePath(wrapperDirectory, directory).Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(relative) && relative != ".")
                    {
                        pathRepositories.Add((package, relative));
                    }
                }
            }

            root["require"] = require;
            if (pathRepositories.Count > 0)
            {
                var before = root["repositories"]?.ToJsonString();
                MergeRepositoriesSection(root, pathRepositories);
                if (root["repositories"]?.ToJsonString() != before)
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            try
            {
                File.WriteAllText(composerJsonPath, root.ToJsonString(JsonWriteOptions) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static JsonObject? GetOrCreateObject(JsonObject parent, string key)
        {
            if (parent[key] is JsonObject existing)
            {
                return existing;
            }

            if (parent.ContainsKey(key))
            {
                return null;
            }

            var created = new JsonObject();
            parent[key] = created;
            return created;
        }

        private static JsonObject? GetOrCreateExtraTyhpRequire(JsonObject root)
        {
            var extra = GetOrCreateObject(root, "extra");
            if (extra is null)
            {
                return null;
            }

            var tyhp = GetOrCreateObject(extra, "tyhp");
            if (tyhp is null)
            {
                return null;
            }

            return GetOrCreateObject(tyhp, "require");
        }

        private static bool SetConstraint(JsonObject section, string package, string constraint)
        {
            if (section[package] is JsonValue existing
                && existing.TryGetValue<string>(out var text)
                && string.Equals(text, constraint, StringComparison.Ordinal))
            {
                return false;
            }

            section[package] = constraint;
            return true;
        }

        private static void FillNameSet(JsonObject? section, HashSet<string> names)
        {
            if (section is null)
            {
                return;
            }

            foreach (var property in section)
            {
                var key = property.Key.Trim();
                if (key.Length > 0)
                {
                    names.Add(key);
                }
            }
        }

        /// <summary>
        /// Maps each on-disk runtime package's Composer <c>name</c> (e.g. <c>tyhp/core</c>) to its
        /// absolute directory under the resolved runtime packages root, or an empty map when that
        /// root cannot be located (the build then proceeds without path repositories).
        /// </summary>
        private static Dictionary<string, string> BuildRuntimePackagePathMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var runtimePackagesRoot = TryResolveRuntimePackagesRoot();
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
        /// <paramref name="runtimeSrcEnvironment"/> is a tyhp-runtime-src checkout (a
        /// <c>packages/</c> child) or that <c>packages</c> directory itself.
        /// </summary>
        private static string? TryResolveFromEnvironment(string? runtimeSrcEnvironment)
        {
            if (string.IsNullOrWhiteSpace(runtimeSrcEnvironment))
            {
                return null;
            }

            string full;
            try
            {
                full = Path.GetFullPath(runtimeSrcEnvironment.Trim());
            }
            catch (Exception)
            {
                return null;
            }

            if (!Directory.Exists(full))
            {
                return null;
            }

            var packages = Path.Combine(full, "packages");
            if (Directory.Exists(packages))
            {
                return Path.GetFullPath(packages);
            }

            return full;
        }

        /// <summary>
        /// Walks upward from <paramref name="startDirectory"/> for a sibling
        /// <c>../tyhp-runtime-src/packages</c> (or a <c>tyhp-runtime-src</c> directory that
        /// itself contains <c>packages/</c>). Stops there. Does not require in-tree
        /// <c>runtime/packages</c>.
        /// </summary>
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
                if (string.Equals(directory.Name, "tyhp-runtime-src", StringComparison.OrdinalIgnoreCase))
                {
                    var nested = Path.Combine(directory.FullName, "packages");
                    if (Directory.Exists(nested))
                    {
                        return Path.GetFullPath(nested);
                    }
                }

                var sibling = Path.GetFullPath(Path.Combine(
                    directory.FullName,
                    "..",
                    "tyhp-runtime-src",
                    "packages"));
                if (Directory.Exists(sibling))
                {
                    return sibling;
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

        internal const string CompilerPackageName = "tyhp/compiler";

        /// <summary>
        /// Fallback when <c>runtime/packages/compiler/composer.json</c> cannot be read (published
        /// CLI without the in-tree packages). Must match that package's <c>version</c>.
        /// </summary>
        internal const string CompilerPackageVersionFallback = "805.1.0-beta.1";

        /// <summary>
        /// Composer version of <c>tyhp/compiler</c> as the package versions itself (compiler tag,
        /// not runtime <c>80N.X.Y</c>).
        /// </summary>
        internal static string ResolveCompilerPackageVersion()
        {
            var packages = GetRuntimePackagePathMap();
            if (packages.TryGetValue(CompilerPackageName, out var directory))
            {
                var fromDisk = RuntimePackageVersions.TryReadComposerVersion(directory);
                if (!string.IsNullOrWhiteSpace(fromDisk))
                {
                    return fromDisk;
                }
            }

            return CompilerPackageVersionFallback;
        }

        /// <summary>
        /// Packagist constraint for a runtime package when path repositories are not used.
        /// <c>tyhpdef/*</c> publishes its <c>composer.json</c> version as-is;
        /// compiled <c>tyhp/*</c> helpers are <c>80N.X.Y</c> for <paramref name="phpVersion"/>.
        /// </summary>
        internal static string ResolvePackagistConstraint(
            string packageName,
            string phpVersion,
            string packageSourceVersion)
        {
            if (packageName.StartsWith("tyhpdef/", StringComparison.Ordinal))
            {
                var source = packageSourceVersion.Trim();
                return string.IsNullOrEmpty(source) ? packageSourceVersion : source;
            }

            return EncodeRuntimePackageVersion(phpVersion, packageSourceVersion);
        }

        /// <summary>
        /// Maps <c>output.phpVersion</c> plus a compiled runtime package's independent <c>X.Y</c>
        /// to the Packagist artifact version <c>80N.X.Y</c>.
        /// </summary>
        internal static string EncodeRuntimePackageVersion(string phpVersion, string packageSourceVersion)
        {
            var phpMajor = PhpVersionToPackageMajor(phpVersion);
            var source = packageSourceVersion.Trim();
            if (string.IsNullOrEmpty(source))
            {
                return $"{phpMajor}.0.0";
            }

            return phpMajor + "." + source;
        }

        internal static string PhpConstraintForPhpVersion(string phpVersion)
        {
            var trimmed = phpVersion.Trim();
            return trimmed switch
            {
                "8.2" => "~8.2.0",
                "8.3" => "~8.3.0",
                "8.4" => "~8.4.0",
                "8.5" => "~8.5.0",
                _ => ">=8.2",
            };
        }

        internal static string PhpVersionToPackageMajor(string phpVersion)
        {
            var trimmed = phpVersion.Trim();
            return trimmed switch
            {
                "8.2" => "802",
                "8.3" => "803",
                "8.4" => "804",
                "8.5" => "805",
                _ => "804",
            };
        }

        private static string? GetNamespaceName(object? namespaceStatement)
            => namespaceStatement switch
            {
                PhpNamespaceDeclAst ns => ns.Identifier,
                PhpBlockNamespaceDeclAst block => block.Identifier,
                _ => null,
            };

        private static string ToComposerRelativePath(
            string outputFilePath,
            string composerDirectory,
            string projectPath)
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return "";
            }

            var phpFull = Path.IsPathRooted(outputFilePath)
                ? Path.GetFullPath(outputFilePath)
                : Path.GetFullPath(Path.Combine(projectPath, outputFilePath));
            var composerFull = Path.GetFullPath(composerDirectory);
            return Path.GetRelativePath(composerFull, phpFull).Replace('\\', '/');
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
