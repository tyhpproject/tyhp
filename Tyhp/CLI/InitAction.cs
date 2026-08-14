using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.CLI.ProjectTemplates;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Extensions;

namespace Tyhp.CLI
{
    /// <summary>
    /// Scaffolds a new Tyhp project (<c>tyhp init</c>).
    /// </summary>
    public class InitAction : ActionRunnerBase
    {
        private const string ExcludeKeyPrefix = "exclude:";

        private static readonly IReadOnlyList<IProjectTemplate> AvailableTemplates =
            new IProjectTemplate[] { new BasicTemplate() };

        /// <summary>
        /// Fixed <c>.gitignore</c> entries. The output directory is appended separately because it
        /// follows <c>--output</c>.
        /// </summary>
        private static readonly string[] GitignoreEntries =
        {
            "tyhp.pid",
            ".tyhp-cache/",
        };

        private readonly Config.Project _project;

        public InitAction(Config.Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            try
            {
                return this.Run(cancellationToken);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or IOException
                or ArgumentException
                or NotSupportedException)
            {
                // Permission failures, I/O failures and malformed paths (invalid characters, bad
                // drive qualifiers) all abort the scaffold instead of escaping as a crash.
                Message.Error("CLI_InitError", ex.Message);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }
        }

        private CompilationResult? Run(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetDir = this.ResolveTargetDirectory();
            var template = this.ResolveTemplate();
            if (template is null)
            {
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            if (File.Exists(targetDir))
            {
                Message.Error("CLI_InitTargetIsFile", targetDir);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            var tyhpJsonPath = Path.Combine(targetDir, "tyhp.json");
            if (File.Exists(tyhpJsonPath))
            {
                Message.Error("CLI_InitAlreadyExists", tyhpJsonPath);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            var options = this.CollectOptions(template, targetDir);
            if (options is null)
            {
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(targetDir);

            var createdDirs = this.CreateDirectories(template, targetDir, options);
            this.WriteTyhpJson(tyhpJsonPath, template, options);
            var (createdFiles, skippedFiles) = this.WriteScaffoldFiles(template, targetDir, options);
            var gitignoreUpdated = this.UpdateGitignore(targetDir, options);

            if (!this._project.BeQuiet)
            {
                Message.Success("CLI_InitSuccess", options.ProjectName, targetDir);
                foreach (var dir in createdDirs)
                {
                    Message.Display("CLI_InitCreatedDir", dir);
                }

                Message.Display("CLI_InitCreatedFile", "tyhp.json");
                foreach (var file in createdFiles)
                {
                    Message.Display("CLI_InitCreatedFile", file);
                }

                foreach (var file in skippedFiles)
                {
                    Message.Display("CLI_InitSkippedFile", file);
                }

                if (gitignoreUpdated)
                {
                    Message.Display("CLI_InitUpdatedGitignore");
                }
            }

            Environment.ExitCode = (int)ExitCode.Success;
            return null;
        }

        private string ResolveTargetDirectory()
        {
            string? relative = null;
            if (this._project.ExplicitPaths.Count > 0)
            {
                relative = this._project.ExplicitPaths[0];
            }
            else
            {
                var directoryFlag = this._project.GetConfigValue("directory");
                if (!String.IsNullOrWhiteSpace(directoryFlag))
                {
                    relative = directoryFlag;
                }
            }

            if (String.IsNullOrWhiteSpace(relative))
            {
                return Directory.GetCurrentDirectory();
            }

            return Path.GetFullPath(relative);
        }

        private IProjectTemplate? ResolveTemplate()
        {
            var name = this._project.GetConfigValue("template");
            if (String.IsNullOrWhiteSpace(name))
            {
                name = BasicTemplate.TemplateName;
            }

            var template = AvailableTemplates.FirstOrDefault(
                t => String.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (template is not null)
            {
                return template;
            }

            var available = String.Join(", ", AvailableTemplates.Select(t => t.Name));
            Message.Error("CLI_InitUnknownTemplate", name, available);
            return null;
        }

        private InitOptions? CollectOptions(IProjectTemplate template, string targetDir)
        {
            var defaults = template.GetDefaultConfig();
            var defaultSrc = NormalizeDir(defaults.GetValueOrDefault("include") is string include
                ? DeriveSrcFromInclude(include)
                : "src/");
            var defaultOutput = NormalizeDir(defaults.GetValueOrDefault("output.path") ?? "build/");
            var defaultNamespace = defaults.GetValueOrDefault("namespace") ?? @"App\";
            var defaultPhpVersion = defaults.GetValueOrDefault("output.phpVersion") ?? "8.4";
            var defaultProjectName = new DirectoryInfo(targetDir).Name;

            // CLI value-flag overrides (--src / --output / --namespace / --php-version).
            var cliSrc = this._project.GetConfigValue("src");
            var cliOutput = this._project.GetConfigValue("output");
            var cliNamespace = this._project.GetConfigValue("namespace");
            var cliPhpVersion = this._project.GetConfigValue("php-version");

            // Prompting would write to the stream --quiet asked to be silenced, so quiet runs take
            // the same defaults-only path as --yes.
            var acceptDefaults = this._project.GetConfigValue("yes").ParseBool() || this._project.BeQuiet;

            string projectName = defaultProjectName;
            string src = !String.IsNullOrWhiteSpace(cliSrc) ? NormalizeDir(cliSrc) : defaultSrc;
            string output = !String.IsNullOrWhiteSpace(cliOutput) ? NormalizeDir(cliOutput) : defaultOutput;
            string ns = !String.IsNullOrWhiteSpace(cliNamespace) ? cliNamespace : defaultNamespace;
            string phpVersion = !String.IsNullOrWhiteSpace(cliPhpVersion) ? cliPhpVersion.Trim() : defaultPhpVersion;
            var namespaceFromCli = !String.IsNullOrWhiteSpace(cliNamespace);
            var namespaceFromPrompt = false;

            if (!acceptDefaults)
            {
                projectName = Prompt("CLI_InitPromptProjectName", defaultProjectName);
                if (String.IsNullOrWhiteSpace(cliSrc))
                {
                    src = NormalizeDir(Prompt("CLI_InitPromptSrc", defaultSrc));
                }

                if (String.IsNullOrWhiteSpace(cliOutput))
                {
                    output = NormalizeDir(Prompt("CLI_InitPromptOutput", defaultOutput));
                }

                if (String.IsNullOrWhiteSpace(cliNamespace))
                {
                    ns = Prompt(
                        "CLI_InitPromptNamespace",
                        DeriveNamespaceFromProjectName(projectName, defaultNamespace));
                    namespaceFromPrompt = true;
                }

                if (String.IsNullOrWhiteSpace(cliPhpVersion))
                {
                    phpVersion = Prompt("CLI_InitPromptPhpVersion", defaultPhpVersion).Trim();
                }
            }

            if (!IsProjectRelativeDir(src))
            {
                Message.Error("CLI_InitInvalidDirectory", "--src", src);
                return null;
            }

            if (!IsProjectRelativeDir(output))
            {
                Message.Error("CLI_InitInvalidDirectory", "--output", output);
                return null;
            }

            var normalizedNamespace = NormalizeNamespace(ns);
            if (normalizedNamespace is null)
            {
                Message.Error("CLI_InitInvalidNamespace", ns);
                return null;
            }

            if (!Config.OutputConfig.IsSupportedPhpVersion(phpVersion))
            {
                Message.Error(
                    "CLI_InitInvalidPhpVersion",
                    phpVersion,
                    String.Join(", ", Config.OutputConfig.SupportedPhpVersionNames));
                return null;
            }

            // psr4 is added when --namespace was passed, or when interactive mode collected one.
            var includePsr4 = namespaceFromCli || namespaceFromPrompt;

            return new InitOptions(
                ProjectName: projectName,
                SrcDir: src,
                OutputDir: output,
                Namespace: normalizedNamespace,
                PhpVersion: phpVersion,
                IncludePsr4: includePsr4);
        }

        private static string Prompt(string resourceKey, string defaultValue)
        {
            Console.Write(Message.Localize(resourceKey, defaultValue));
            var line = Console.ReadLine();
            return String.IsNullOrWhiteSpace(line) ? defaultValue : line.Trim();
        }

        private List<string> CreateDirectories(
            IProjectTemplate template,
            string targetDir,
            InitOptions options)
        {
            var created = new List<string>();
            foreach (var relative in template.GetDirectories())
            {
                var mapped = RemapTemplatePath(relative, options.SrcDir, options.OutputDir);
                var full = Path.Combine(targetDir, mapped.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(full))
                {
                    Directory.CreateDirectory(full);
                    created.Add(mapped.TrimEnd('/'));
                }
            }

            // Ensure overridden src/output exist even if the template listed different names.
            EnsureDir(targetDir, options.SrcDir, created);
            EnsureDir(targetDir, options.OutputDir, created);
            return created;
        }

        private static void EnsureDir(string targetDir, string relativeDir, List<string> created)
        {
            var trimmed = relativeDir.TrimEnd('/');
            var full = Path.Combine(targetDir, trimmed.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                if (!created.Contains(trimmed, StringComparer.Ordinal))
                {
                    created.Add(trimmed);
                }
            }
        }

        private void WriteTyhpJson(string path, IProjectTemplate template, InitOptions options)
        {
            var defaults = template.GetDefaultConfig();
            var srcRoot = options.SrcDir.TrimEnd('/');
            var includeGlob = srcRoot.Length == 0 || srcRoot == "."
                ? "**/*.tyhp"
                : srcRoot + "/**/*.tyhp";

            var exclude = new JsonArray();
            foreach (var key in defaults.Keys
                .Where(k => k.StartsWith(ExcludeKeyPrefix, StringComparison.Ordinal))
                .OrderBy(ExcludeKeyIndex))
            {
                exclude.Add(defaults[key]);
            }

            if (exclude.Count == 0)
            {
                exclude.Add("vendor/**");
                exclude.Add("node_modules/**");
            }

            var tagless = defaults.GetValueOrDefault("source.tagless").ParseBool();
            var strictTypes = !defaults.ContainsKey("output.strictTypes")
                || defaults["output.strictTypes"].ParseBool();
            var comments = !defaults.ContainsKey("output.comments")
                || defaults["output.comments"].ParseBool();

            var root = new JsonObject
            {
                ["include"] = new JsonArray { includeGlob },
                ["exclude"] = exclude,
                ["source"] = new JsonObject
                {
                    ["tagless"] = tagless,
                },
                ["output"] = new JsonObject
                {
                    ["path"] = options.OutputDir,
                    ["phpVersion"] = options.PhpVersion,
                    ["strictTypes"] = strictTypes,
                    ["comments"] = comments,
                },
            };

            if (options.IncludePsr4)
            {
                root["psr4"] = new JsonObject
                {
                    [options.Namespace] = options.SrcDir,
                };
            }

            var json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            // System.Text.Json omits a trailing newline; add one for POSIX friendliness.
            File.WriteAllText(path, json + Environment.NewLine);
        }

        private (List<string> Created, List<string> Skipped) WriteScaffoldFiles(
            IProjectTemplate template,
            string targetDir,
            InitOptions options)
        {
            var created = new List<string>();
            var skipped = new List<string>();
            var namespaceForFile = options.Namespace.TrimEnd('\\');

            foreach (var (relativePath, content) in template.GetScaffoldFiles())
            {
                var mapped = RemapTemplatePath(relativePath, options.SrcDir, options.OutputDir);
                var full = Path.Combine(targetDir, mapped.Replace('/', Path.DirectorySeparatorChar));

                // A missing tyhp.json does not mean the directory is empty, so scaffolding must
                // never clobber a file the user already wrote.
                if (File.Exists(full))
                {
                    skipped.Add(mapped);
                    continue;
                }

                var dir = Path.GetDirectoryName(full);
                if (!String.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var rendered = content.Replace("{{NAMESPACE}}", namespaceForFile, StringComparison.Ordinal);
                File.WriteAllText(full, rendered);
                created.Add(mapped);
            }

            return (created, skipped);
        }

        private bool UpdateGitignore(string targetDir, InitOptions options)
        {
            var gitignorePath = Path.Combine(targetDir, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                return false;
            }

            var existing = File.ReadAllText(gitignorePath);
            var existingLines = new HashSet<string>(
                existing.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                    .Select(static l => l.Trim()),
                StringComparer.Ordinal);

            // The build directory follows --output; the remaining entries are fixed.
            var candidates = new List<string> { options.OutputDir };
            candidates.AddRange(GitignoreEntries);

            var toAppend = candidates
                .Where(entry => existingLines.Add(entry))
                .ToList();
            if (toAppend.Count == 0)
            {
                return false;
            }

            var builder = new StringBuilder();
            if (existing.Length > 0 && !existing.EndsWith('\n') && !existing.EndsWith('\r'))
            {
                builder.AppendLine();
            }

            foreach (var entry in toAppend)
            {
                builder.AppendLine(entry);
            }

            File.AppendAllText(gitignorePath, builder.ToString());
            return true;
        }

        private static string RemapTemplatePath(string relative, string srcDir, string outputDir)
        {
            var normalized = relative.Replace('\\', '/');

            if (normalized == "src" || normalized == "src/"
                || normalized.StartsWith("src/", StringComparison.Ordinal))
            {
                var suffix = normalized.StartsWith("src/", StringComparison.Ordinal)
                    ? normalized["src/".Length..]
                    : "";
                return CombineRelative(srcDir, suffix);
            }

            if (normalized == "build" || normalized == "build/"
                || normalized.StartsWith("build/", StringComparison.Ordinal))
            {
                var suffix = normalized.StartsWith("build/", StringComparison.Ordinal)
                    ? normalized["build/".Length..]
                    : "";
                return CombineRelative(outputDir, suffix);
            }

            return normalized.TrimEnd('/');
        }

        private static string CombineRelative(string rootDir, string suffix)
        {
            var root = rootDir.TrimEnd('/');
            suffix = suffix.TrimEnd('/');
            return String.IsNullOrEmpty(suffix) ? root : root + "/" + suffix;
        }

        private static string DeriveSrcFromInclude(string include)
        {
            // "src/**/*.tyhp" → "src/"
            var star = include.IndexOf("/**", StringComparison.Ordinal);
            if (star > 0)
            {
                return include[..star] + "/";
            }

            return "src/";
        }

        private static string NormalizeDir(string path)
        {
            path = path.Replace('\\', '/').Trim();
            while (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            if (path.Length == 0)
            {
                return "./";
            }

            if (!path.EndsWith('/'))
            {
                path += "/";
            }

            return path;
        }

        /// <summary>
        /// Rejects source/output directories that would place project files outside the project
        /// root, which also produces <c>include</c> globs the project matcher cannot resolve.
        /// </summary>
        private static bool IsProjectRelativeDir(string normalizedDir)
        {
            if (normalizedDir.StartsWith('/') || Path.IsPathRooted(normalizedDir))
            {
                return false;
            }

            // Drive-qualified Windows paths are not rooted when running on Unix.
            if (normalizedDir.Length > 1 && normalizedDir[1] == ':')
            {
                return false;
            }

            return !normalizedDir
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Contains("..", StringComparer.Ordinal);
        }

        /// <summary>
        /// Normalizes a namespace prefix to <c>Segment\Segment\</c> form.
        /// </summary>
        /// <returns>The normalized prefix, or <see langword="null"/> when it is not a valid PHP namespace.</returns>
        private static string? NormalizeNamespace(string ns)
        {
            ns = ns.Trim().Replace('/', '\\');
            if (ns.Length == 0)
            {
                return @"App\";
            }

            var segments = ns.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || !segments.All(IsPhpIdentifier))
            {
                return null;
            }

            return String.Join('\\', segments) + "\\";
        }

        private static bool IsPhpIdentifier(string value)
        {
            if (value.Length == 0 || !(Char.IsLetter(value[0]) || value[0] == '_' || value[0] >= 0x80))
            {
                return false;
            }

            return value.All(static c => Char.IsLetterOrDigit(c) || c == '_' || c >= 0x80);
        }

        /// <summary>
        /// Builds the interactive namespace default from the project name
        /// (<c>my-project</c> → <c>MyProject\</c>), falling back when no valid prefix can be formed.
        /// </summary>
        private static string DeriveNamespaceFromProjectName(string projectName, string fallback)
        {
            var builder = new StringBuilder();
            foreach (var word in projectName.Split(
                new[] { '-', '_', ' ', '.' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = new string(word.Where(static c => Char.IsLetterOrDigit(c) || c >= 0x80).ToArray());
                if (cleaned.Length == 0)
                {
                    continue;
                }

                builder.Append(Char.ToUpperInvariant(cleaned[0])).Append(cleaned.AsSpan(1));
            }

            var candidate = builder.ToString();
            return IsPhpIdentifier(candidate) ? candidate + "\\" : fallback;
        }

        /// <summary>
        /// Orders <c>exclude:N</c> template keys numerically so index 10 does not sort before 2.
        /// </summary>
        private static int ExcludeKeyIndex(string key)
            => Int32.TryParse(key.AsSpan(ExcludeKeyPrefix.Length), out int index) ? index : Int32.MaxValue;

        private sealed record InitOptions(
            string ProjectName,
            string SrcDir,
            string OutputDir,
            string Namespace,
            string PhpVersion,
            bool IncludePsr4);
    }
}
