using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tyhp.Config;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Tracks source file changes between builds and persists build state for incremental compilation.
    /// Parse-level caching is handled by <see cref="AstCacheService"/>; binding and checking always
    /// run on all files when any file changes (conservative strategy).
    /// </summary>
    public sealed class IncrementalBuildService
    {
        public const string BuildStateFileName = "tyhp-build-state.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Result of comparing current source files against a persisted build state.
        /// </summary>
        public sealed class FileChangeResult
        {
            public List<string> ChangedFiles { get; } = [];
            public List<string> NewFiles { get; } = [];
            public List<string> RemovedFiles { get; } = [];
            public List<string> UnchangedFiles { get; } = [];

            public bool HasChanges =>
                this.ChangedFiles.Count > 0
                || this.NewFiles.Count > 0
                || this.RemovedFiles.Count > 0;
        }

        /// <summary>
        /// Persisted build state written after a successful build.
        /// </summary>
        public sealed class BuildState
        {
            public string? CompilerVersion { get; set; }
            public string? ConfigHash { get; set; }
            public DateTime Timestamp { get; set; }
            public Dictionary<string, string> FileHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Absolute paths of output files produced by the last successful build.
            /// </summary>
            public List<string> OutputFilePaths { get; set; } = [];
        }

        /// <summary>
        /// Resolves the path to the build state file under the configured output directory.
        /// </summary>
        public static string GetBuildStatePath(Project project)
        {
            var projectPath = PathCanonicalizer.GetCanonicalFullPath(project.GetProjectPath());
            var outputDir = BuildOutputCleaner.ResolveOutputDirectory(projectPath, project.Output.Path);
            return Path.Combine(outputDir, BuildStateFileName);
        }

        /// <summary>
        /// Loads build state from disk, returning <c>null</c> when missing or invalid.
        /// </summary>
        public BuildState? LoadBuildState(string buildStatePath)
        {
            if (!File.Exists(buildStatePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(buildStatePath);
                return JsonSerializer.Deserialize<BuildState>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns whether the loaded state matches the current compiler version and configuration.
        /// </summary>
        public bool IsStateValid(BuildState? state, Project project)
        {
            if (state == null)
            {
                return false;
            }

            var currentVersion = GetCompilerVersion();
            if (!String.Equals(state.CompilerVersion, currentVersion, StringComparison.Ordinal))
            {
                return false;
            }

            var currentConfigHash = ComputeConfigHash(project);
            return String.Equals(state.ConfigHash, currentConfigHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares file hashes against stored state and classifies each file.
        /// </summary>
        public FileChangeResult DetermineChangedFiles(IEnumerable<string> allFiles, BuildState? previousState)
        {
            var result = new FileChangeResult();
            var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var storedHashes = previousState?.FileHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in allFiles)
            {
                var normalizedPath = NormalizeFilePath(filePath);
                var hash = ComputeFileHash(filePath);
                if (hash == null)
                {
                    result.ChangedFiles.Add(normalizedPath);
                    continue;
                }

                currentHashes[normalizedPath] = hash;

                if (!storedHashes.TryGetValue(normalizedPath, out var storedHash))
                {
                    result.NewFiles.Add(normalizedPath);
                }
                else if (!String.Equals(storedHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    result.ChangedFiles.Add(normalizedPath);
                }
                else
                {
                    result.UnchangedFiles.Add(normalizedPath);
                }
            }

            foreach (var (storedPath, _) in storedHashes)
            {
                if (!currentHashes.ContainsKey(storedPath))
                {
                    result.RemovedFiles.Add(storedPath);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns whether every output file recorded in the build state still exists on disk.
        /// When no output paths were recorded (legacy state), returns <c>true</c>.
        /// </summary>
        public bool AllOutputFilesExist(BuildState? state)
        {
            if (state?.OutputFilePaths == null || state.OutputFilePaths.Count == 0)
            {
                return true;
            }

            foreach (var outputPath in state.OutputFilePaths)
            {
                if (!File.Exists(outputPath))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Saves build state after a successful build.
        /// </summary>
        public void SaveBuildState(
            string buildStatePath,
            IEnumerable<string> allFiles,
            Project project,
            IEnumerable<string>? outputFilePaths = null)
        {
            var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in allFiles)
            {
                var normalizedPath = NormalizeFilePath(filePath);
                var hash = ComputeFileHash(filePath);
                if (hash != null)
                {
                    fileHashes[normalizedPath] = hash;
                }
            }

            var normalizedOutputPaths = outputFilePaths?
                .Select(NormalizeFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList() ?? [];

            var state = new BuildState
            {
                CompilerVersion = GetCompilerVersion(),
                ConfigHash = ComputeConfigHash(project),
                Timestamp = DateTime.UtcNow,
                FileHashes = fileHashes,
                OutputFilePaths = normalizedOutputPaths,
            };

            var directory = Path.GetDirectoryName(buildStatePath);
            if (!String.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(buildStatePath, JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine);
        }

        /// <summary>
        /// Deletes the build state file when present.
        /// </summary>
        public static void DeleteBuildState(string buildStatePath)
        {
            if (File.Exists(buildStatePath))
            {
                File.Delete(buildStatePath);
            }
        }

        internal static string ComputeConfigHash(Project project)
        {
            var builder = new StringBuilder();
            builder.AppendLine(project.Type.ToString());
            builder.AppendLine(project.Tagless.ToString());
            builder.AppendLine(project.Output.Path);
            builder.AppendLine(project.Output.NamespacePrefix ?? "");
            builder.AppendLine(project.Output.PhpVersion);
            builder.AppendLine(project.Output.StrictTypes.ToString());
            builder.AppendLine(project.Output.IncludeComments.ToString());
            builder.AppendLine(String.Join('|', project.IncludePaths.OrderBy(p => p, StringComparer.Ordinal)));
            builder.AppendLine(String.Join('|', project.ExcludePaths.OrderBy(p => p, StringComparer.Ordinal)));
            builder.AppendLine(project.Build.StructBacking);
            builder.AppendLine(project.Build.DecimalBacking);
            builder.AppendLine(project.Build.DecimalScale.ToString());
            builder.AppendLine(project.Build.DecimalRounding);
            builder.AppendLine(project.Build.GenerateSourcemap.ToString());
            builder.AppendLine(project.Build.SourceMapIncludeContent.ToString());
            builder.AppendLine(project.Build.UpdateComposer.ToString());
            builder.AppendLine(project.Build.GenerateTyhpdef?.ToString() ?? "");
            builder.AppendLine(project.Build.AllowEval.ToString());
            builder.AppendLine(project.Build.Profile ?? "");
            builder.AppendLine(project.Build.Optimize ?? "");
            builder.AppendLine(project.Build.ExperimentalReadonlyCloneWith.ToString());
            builder.AppendLine(project.Build.RuntimeGenericChecks.ToString());
            AppendStringMap(builder, project.Build.Psr4);
            AppendStringMap(builder, project.Build.EntryPointAutoloader);
            AppendBoolMap(builder, project.Build.Optimizations);
            AppendStringList(builder, project.Build.Psr4Includes);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static void AppendStringMap(StringBuilder builder, IReadOnlyDictionary<string, string>? map)
        {
            if (map == null)
            {
                builder.AppendLine("<null>");
                return;
            }

            foreach (var (key, value) in map.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                builder.Append(key).Append('=').Append(value).Append(';');
            }

            builder.AppendLine();
        }

        private static void AppendBoolMap(StringBuilder builder, IReadOnlyDictionary<string, bool>? map)
        {
            if (map == null)
            {
                builder.AppendLine("<null>");
                return;
            }

            foreach (var (key, value) in map.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                builder.Append(key).Append('=').Append(value).Append(';');
            }

            builder.AppendLine();
        }

        private static void AppendStringList(StringBuilder builder, IReadOnlyList<string>? list)
        {
            if (list == null)
            {
                builder.AppendLine("<null>");
                return;
            }

            builder.AppendLine(String.Join('|', list.OrderBy(v => v, StringComparer.Ordinal)));
        }

        internal static string GetCompilerVersion()
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        private static string? ComputeFileHash(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                using var stream = File.OpenRead(filePath);
                var hashBytes = SHA256.HashData(stream);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string NormalizeFilePath(string filePath)
            => PathCanonicalizer.GetCanonicalFullPath(filePath).Replace('\\', '/');
    }
}
