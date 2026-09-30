using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Reflection harvest orchestrator: resolve PHP, run the Reflection dumper, map JSON → IR, attach
    /// php.net comments, write Layer 1 <c>.tyhpdef</c> via <see cref="TyhpdefOutputWriter"/>.
    /// </summary>
    public class PhpDelegationTyhpdefGenerator
    {
        private readonly PhpRuntimeManager _runtimeManager;
        private readonly PhpRuntimeDetector _runtimeDetector;
        private readonly PhpManualDocExtractor _docExtractor;
        private readonly StubHarvestTyhpdefEnricher _stubHarvest;

        /// <summary>Layer 1 IR from the last successful map (golden for <c>--verify</c>).</summary>
        internal TyhpdefFile? LastLayer1 { get; private set; }

        public PhpDelegationTyhpdefGenerator()
            : this(new PhpRuntimeManager(), new PhpRuntimeDetector(), new PhpManualDocExtractor())
        {
        }

        public PhpDelegationTyhpdefGenerator(
            PhpRuntimeManager runtimeManager,
            PhpRuntimeDetector runtimeDetector,
            PhpManualDocExtractor? docExtractor = null,
            StubHarvestTyhpdefEnricher? stubHarvest = null)
        {
            this._runtimeManager = runtimeManager;
            this._runtimeDetector = runtimeDetector;
            this._docExtractor = docExtractor ?? new PhpManualDocExtractor();
            this._stubHarvest = stubHarvest ?? new StubHarvestTyhpdefEnricher();
        }

        /// <summary>
        /// Resolve the Reflection harvest runtime (managed or <c>--php</c>) and generate Layer 1 tyhpdefs.
        /// </summary>
        public void GenerateFromExtension(
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            string? projectPhpVersion,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            var extName = options.ExtensionName?.Trim();
            if (string.IsNullOrWhiteSpace(extName))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefRequiredArguments"));
                return;
            }

            var runtime = this.ResolveRuntime(options, projectPhpVersion, result.Diagnostics, cancellationToken);
            if (runtime is null || result.Diagnostics.HasErrors)
            {
                if (!result.Diagnostics.HasErrors)
                {
                    result.Diagnostics.AddError(MessageCode.TyhpdefPhpNotFound, "generate_tyhpdef", 0, 0);
                }

                return;
            }

            if (!runtime.IsManaged)
            {
                Message.Warn("CLI_TyhpdefUserBinaryUncontrolled", runtime.Path);
            }
            else
            {
                Message.Info("CLI_TyhpdefUsingManagedRuntime", runtime.Version, runtime.Path);
            }

            if (!runtime.HasExtension(extName))
            {
                if (runtime.IsManaged)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefPhpExtensionNotProvisioned,
                        "generate_tyhpdef",
                        0,
                        0,
                        extName);
                }
                else
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefExtensionNotLoaded", extName, runtime.Path));
                }

                return;
            }

            this.GenerateWithRuntime(options, result, runtime, extName, cancellationToken);
        }

        /// <summary>
        /// Multi-target Reflection harvest: managed PHP only. Ensures every minor (or reuses snapshots),
        /// diffs surfaces, and writes one gated Layer 1 tree. Fail-closed: any missing target
        /// aborts the whole run.
        /// </summary>
        public void GenerateFromPhpTargets(
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            var extName = options.ExtensionName?.Trim();
            if (string.IsNullOrWhiteSpace(extName))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefRequiredArguments"));
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.PhpExecutablePath))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefPhpUserBinaryWithTargets,
                    "generate_tyhpdef",
                    0,
                    0);
                return;
            }

            var minors = NormalizeTargets(options.PhpTargets, result);
            if (minors.Count == 0 || result.Diagnostics.HasErrors)
            {
                return;
            }

            var snapshotDir = TyhpdefSnapshotStore.ResolveDirectory(options);
            var versioned = new List<(string Minor, TyhpdefFile File)>(minors.Count);

            foreach (var minor in minors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!this.TryCollectTarget(
                    minor,
                    extName,
                    snapshotDir,
                    options,
                    result,
                    cancellationToken,
                    out var file))
                {
                    return;
                }

                versioned.Add((minor, file));
            }

            var merged = TyhpdefMultiTargetMerger.Merge(versioned);
            var writeOptions = options with { Overwrite = true };
            this.WriteFiles(merged, writeOptions, result, extName, cancellationToken);
        }

        /// <summary>JSON snapshots → gated IR → writer. Used by tests (no PHP process).</summary>
        public void GenerateFromTargetJsons(
            IReadOnlyList<(string Minor, string Json)> snapshots,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            var extName = options.ExtensionName?.Trim() ?? "json";
            var versioned = new List<(string Minor, TyhpdefFile File)>(snapshots.Count);
            foreach (var (minor, json) in snapshots)
            {
                if (!this.TryMapJson(json, minor, extName, options, result, cancellationToken, out var file)
                    || file is null)
                {
                    return;
                }

                versioned.Add((minor, file));
            }

            if (versioned.Count == 0)
            {
                return;
            }

            var merged = TyhpdefMultiTargetMerger.Merge(versioned);
            var writeOptions = options with { Overwrite = true };
            this.WriteFiles(merged, writeOptions, result, extName, cancellationToken);
        }

        internal void GenerateWithRuntime(
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            PhpRuntimeInfo runtime,
            string extName,
            CancellationToken cancellationToken)
        {
            string? dumperPath = null;
            try
            {
                dumperPath = PhpReflectionDumper.WriteTempScript();
                var dumpResult = PhpProcessRunner.Run(
                    runtime,
                    [dumperPath, extName],
                    options.PhpProcessTimeoutMs,
                    cancellationToken);
                if (dumpResult.TimedOut || dumpResult.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(dumpResult.StandardError)
                        ? dumpResult.StandardOutput
                        : dumpResult.StandardError;
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefDumperFailed", detail.Trim()));
                    return;
                }

                var json = dumpResult.StandardOutput.Trim();
                if (json.Length == 0)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefDumperFailed", "empty stdout"));
                    return;
                }

                PhpReflectionDumpDto dump;
                try
                {
                    dump = PhpReflectionJson.Deserialize(json);
                }
                catch (JsonException ex)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefSchemaInvalid", ex.Message));
                    return;
                }

                if (dump.SchemaVersion != PhpReflectionJson.SchemaVersion)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefSchemaInvalid", "schemaVersion " + dump.SchemaVersion));
                    return;
                }

                if (string.IsNullOrWhiteSpace(dump.PhpVersion)
                    || string.IsNullOrWhiteSpace(dump.Extension)
                    || dump.Constants is null
                    || dump.Functions is null
                    || dump.Classes is null)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefSchemaInvalid", "missing required properties"));
                    return;
                }

                var file = PhpReflectionMapper.Map(dump, options, runtime);
                if (options.IncludeDocComments)
                {
                    this._docExtractor.Attach(
                        file,
                        options.Locale,
                        dump.PhpVersion,
                        dump.Extension,
                        dump.ExtensionVersion,
                        result.Diagnostics,
                        cancellationToken);
                }

                this.WriteFiles(file, options, result, extName, cancellationToken);
            }
            finally
            {
                if (dumperPath is not null)
                {
                    try
                    {
                        File.Delete(dumperPath);
                    }
                    catch (IOException)
                    {
                        // Temp cleanup is best-effort.
                    }
                }
            }
        }

        /// <summary>JSON → IR → writer path used by unit tests (no PHP process).</summary>
        public void GenerateFromJson(
            string json,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            PhpRuntimeInfo runtime,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(runtime);

            var dump = PhpReflectionJson.Deserialize(json);
            if (dump.SchemaVersion != PhpReflectionJson.SchemaVersion)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefSchemaInvalid", "schemaVersion " + dump.SchemaVersion));
                return;
            }

            var file = PhpReflectionMapper.Map(dump, options, runtime);
            if (options.IncludeDocComments)
            {
                this._docExtractor.Attach(
                    file,
                    options.Locale,
                    dump.PhpVersion,
                    dump.Extension,
                    dump.ExtensionVersion,
                    result.Diagnostics,
                    cancellationToken);
            }

            this.WriteFiles(file, options, result, options.ExtensionName ?? dump.Extension, cancellationToken);
        }

        internal PhpRuntimeInfo? ResolveRuntime(
            TyhpdefGenerationOptions options,
            string? projectPhpVersion,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(options.PhpExecutablePath))
            {
                return this._runtimeDetector.Inspect(
                    options.PhpExecutablePath,
                    diagnostics,
                    expectedMinor: options.PhpVersion,
                    timeoutMs: Math.Min(options.PhpProcessTimeoutMs, 15_000),
                    cancellationToken: cancellationToken);
            }

            var minor = PhpRuntimeVersion.ToMinor(options.PhpVersion)
                ?? PhpRuntimeVersion.ToMinor(projectPhpVersion)
                ?? "8.2";
            return this._runtimeManager.Ensure(minor, options, diagnostics, cancellationToken);
        }

        private bool TryCollectTarget(
            string minor,
            string extName,
            string snapshotDir,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken,
            out TyhpdefFile file)
        {
            file = new TyhpdefFile();
            var snapshotPath = TyhpdefSnapshotStore.FilePath(snapshotDir, minor, extName);
            string? json = null;
            if (!options.RefreshSnapshots && TyhpdefSnapshotStore.TryRead(snapshotPath, out var cached))
            {
                json = cached;
                Message.Info("CLI_TyhpdefUsingSnapshot", minor, snapshotPath);
            }
            else
            {
                var runtime = this._runtimeManager.Ensure(minor, options, result.Diagnostics, cancellationToken);
                if (runtime is null || result.Diagnostics.HasErrors)
                {
                    if (!result.Diagnostics.HasErrors)
                    {
                        result.Diagnostics.AddError(
                            MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                            "generate_tyhpdef",
                            0,
                            0,
                            Message.Localize("CLI_TyhpdefUnsupportedMinor", minor));
                    }

                    return false;
                }

                if (!runtime.HasExtension(extName))
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefPhpExtensionNotProvisioned,
                        "generate_tyhpdef",
                        0,
                        0,
                        extName);
                    return false;
                }

                json = this.DumpExtensionJson(runtime, extName, options, result, cancellationToken);
                if (json is null)
                {
                    return false;
                }

                try
                {
                    TyhpdefSnapshotStore.Write(snapshotPath, json);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        snapshotPath,
                        0,
                        0,
                        snapshotPath,
                        ex.Message);
                    return false;
                }
            }

            if (json is null
                || !this.TryMapJson(json, minor, extName, options, result, cancellationToken, out var mapped)
                || mapped is null)
            {
                return false;
            }

            file = mapped;
            return true;
        }

        private bool TryMapJson(
            string json,
            string minor,
            string extName,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken,
            [NotNullWhen(true)] out TyhpdefFile? file)
        {
            file = null;
            PhpReflectionDumpDto dump;
            try
            {
                dump = PhpReflectionJson.Deserialize(json);
            }
            catch (JsonException ex)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefSchemaInvalid", ex.Message));
                return false;
            }

            if (dump.SchemaVersion != PhpReflectionJson.SchemaVersion
                || string.IsNullOrWhiteSpace(dump.Extension)
                || dump.Constants is null
                || dump.Functions is null
                || dump.Classes is null)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefSchemaInvalid", "missing required properties"));
                return false;
            }

            var runtime = new PhpRuntimeInfo
            {
                Path = "snapshot://" + minor,
                Version = string.IsNullOrWhiteSpace(dump.PhpVersion) ? minor : dump.PhpVersion,
                IsManaged = true,
                LoadedExtensions = [extName],
            };
            file = PhpReflectionMapper.Map(dump, options, runtime);
            if (options.IncludeDocComments)
            {
                this._docExtractor.Attach(
                    file,
                    options.Locale,
                    dump.PhpVersion,
                    dump.Extension,
                    dump.ExtensionVersion,
                    result.Diagnostics,
                    cancellationToken);
            }

            return !result.Diagnostics.HasErrors;
        }

        private string? DumpExtensionJson(
            PhpRuntimeInfo runtime,
            string extName,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken)
        {
            string? dumperPath = null;
            try
            {
                dumperPath = PhpReflectionDumper.WriteTempScript();
                var dumpResult = PhpProcessRunner.Run(
                    runtime,
                    [dumperPath, extName],
                    options.PhpProcessTimeoutMs,
                    cancellationToken);
                if (dumpResult.TimedOut || dumpResult.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(dumpResult.StandardError)
                        ? dumpResult.StandardOutput
                        : dumpResult.StandardError;
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefDumperFailed", detail.Trim()));
                    return null;
                }

                var json = dumpResult.StandardOutput.Trim();
                if (json.Length == 0)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefDumperFailed", "empty stdout"));
                    return null;
                }

                return json;
            }
            finally
            {
                if (dumperPath is not null)
                {
                    try
                    {
                        File.Delete(dumperPath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private static List<string> NormalizeTargets(IEnumerable<string> targets, TyhpdefGenerationResult result)
        {
            var minors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in targets ?? [])
            {
                var minor = PhpRuntimeVersion.ToMinor(raw);
                if (minor is null || !PhpRuntimeVersion.IsSupportedMinor(minor))
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                        "generate_tyhpdef",
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefUnsupportedMinor", raw ?? ""));
                    return [];
                }

                if (seen.Add(minor))
                {
                    minors.Add(minor);
                }
            }

            minors.Sort(PhpRuntimeVersion.ComparePatch);
            if (minors.Count == 0)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefRequiredArguments"));
            }

            return minors;
        }

        private void WriteFiles(
            TyhpdefFile file,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            string extName,
            CancellationToken cancellationToken = default)
        {
            RewriteLateStaticTypes(file);
            this.LastLayer1 = file;
            var outputs = TyhpdefOutputLayout.Split(file, options, extName);
            var writerOptions = new TyhpdefOutputOptions { IncludeDocComments = options.IncludeDocComments };
            foreach (var (path, part) in outputs)
            {
                if (File.Exists(path) && !options.Overwrite)
                {
                    Message.Warn("CLI_TyhpdefSkippedExistingFile", path);
                    continue;
                }

                string text;
                try
                {
                    text = TyhpdefOutputWriter.Write(part, writerOptions);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        path,
                        0,
                        0,
                        path,
                        ex.Message);
                    continue;
                }

                var parseDiagnostics = new DiagnosticBag();
                try
                {
                    var ast = Tyhpdef.ParseContent(text, path, ParseMode.Tyhpdef, parseDiagnostics);
                    if (ast is null || parseDiagnostics.HasErrors)
                    {
                        foreach (var diagnostic in parseDiagnostics.All)
                        {
                            result.Diagnostics.Add(diagnostic);
                        }

                        var detail = parseDiagnostics.Errors.Count > 0
                            ? parseDiagnostics.Errors[0].Message
                            : "parse returned null";
                        FailParse(result, path, detail);
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    FailParse(result, path, ex.Message);
                    continue;
                }

                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(path, text);
                    result.GeneratedFiles.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        path,
                        0,
                        0,
                        path,
                        ex.Message);
                }
            }

            result.ClassCount = CountTypes(file);
            result.FunctionCount = CountFunctions(file);
            result.ConstantCount = CountConstants(file);
            result.TotalDeclarations = result.ClassCount + result.FunctionCount + result.ConstantCount;

            if (options.IncludeDocComments && this._docExtractor.ManualLoaded)
            {
                TyhpdefSourcesMarkdown.WriteManualCredit(options.OutputDirectory, extName);
            }

            if (!result.Diagnostics.HasErrors
                && result.GeneratedFiles.Count > 0
                && options.Mode == TyhpdefGenerationMode.PhpExtension)
            {
                this._stubHarvest.Enrich(file, options, result, extName, cancellationToken);
            }
        }

        /// <summary>
        /// Reports a failed round-trip parse of freshly generated Layer 1 text. Must call
        /// <see cref="Message.Error"/> immediately and add <see cref="MessageCode.TyhpdefParseError"/>
        /// — the CLI formatter skips <see cref="MessageCode.TyhpdefGenerationError"/> (usage
        /// errors already printed), so a bag-only 7500 would exit non-zero with no TYHP####.
        /// Always add 8001 even when ANTLR already reported errors: swallowed failed-predicate
        /// parses can leave the bag empty while <c>ParseContent</c> still returns null.
        /// </summary>
        private static void FailParse(TyhpdefGenerationResult result, string path, string detail)
        {
            Message.Error("CLI_TyhpdefParseFailed", path, detail);
            result.Diagnostics.AddError(
                MessageCode.TyhpdefParseError,
                path,
                0,
                0,
                Message.Localize("CLI_TyhpdefParseFailed", path, detail));
        }

        /// <summary>
        /// PHP Reflection spells late-static-binding returns as <c>static</c> / <c>?static</c>.
        /// Tyhpdef parse-check rejects those tokens as types; Layer 1 therefore uses the
        /// declaring class FQCN (same spelling as hand-written extension stubs).
        /// </summary>
        private static void RewriteLateStaticTypes(TyhpdefFile file)
        {
            RewriteClasses(file.GlobalTypes, namespaceName: "");
            foreach (var ns in file.Namespaces ?? [])
            {
                RewriteClasses(ns.Classes, ns.Name);
            }

            foreach (var block in file.DeclareBlocks ?? [])
            {
                RewriteClasses(block.Classes, namespaceName: "");
                foreach (var ns in block.Namespaces ?? [])
                {
                    RewriteClasses(ns.Classes, ns.Name);
                }
            }
        }

        private static void RewriteClasses(List<TyhpdefClassDeclaration>? classes, string namespaceName)
        {
            if (classes is null)
            {
                return;
            }

            foreach (var type in classes)
            {
                var fqn = QualifyClassName(namespaceName, type.Name);
                if (fqn.Length == 0)
                {
                    continue;
                }

                RewriteMethods(type.Methods, fqn);
                RewriteMethods(type.Operators, fqn);
                RewriteProperties(type.Properties, fqn);
                RewriteExtensionMembers(type.ExtensionMembers, fqn);
                RewriteExtensionGroups(type.ExtensionGroups, fqn);
            }
        }

        private static void RewriteMethods(List<TyhpdefMethod>? methods, string fqn)
        {
            if (methods is null)
            {
                return;
            }

            for (var i = 0; i < methods.Count; i++)
            {
                methods[i] = RewriteMethod(methods[i], fqn);
            }
        }

        private static TyhpdefMethod RewriteMethod(TyhpdefMethod method, string fqn)
        {
            var overloads = method.Overloads;
            if (overloads is { Count: > 0 })
            {
                for (var i = 0; i < overloads.Count; i++)
                {
                    overloads[i] = RewriteMethod(overloads[i], fqn);
                }
            }

            return method with
            {
                ReturnType = RewriteStaticType(method.ReturnType, fqn),
                Parameters = RewriteParameters(method.Parameters, fqn),
            };
        }

        private static void RewriteProperties(List<TyhpdefProperty>? properties, string fqn)
        {
            if (properties is null)
            {
                return;
            }

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                properties[i] = property with { Type = RewriteStaticType(property.Type, fqn) };
            }
        }

        private static void RewriteExtensionGroups(List<TyhpdefExtensionGroup>? groups, string fqn)
        {
            if (groups is null)
            {
                return;
            }

            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                var members = group.Members ?? [];
                RewriteExtensionMembers(members, fqn);
                groups[i] = group with
                {
                    TargetType = RewriteStaticType(group.TargetType, fqn),
                    Members = members,
                };
            }
        }

        private static void RewriteExtensionMembers(List<TyhpdefExtensionMember>? members, string fqn)
        {
            if (members is null)
            {
                return;
            }

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                members[i] = member with
                {
                    ReturnType = RewriteStaticType(member.ReturnType, fqn),
                    Parameters = RewriteParameters(member.Parameters, fqn),
                };
            }
        }

        private static List<TyhpdefParameter> RewriteParameters(List<TyhpdefParameter>? parameters, string fqn)
        {
            if (parameters is null || parameters.Count == 0)
            {
                return parameters ?? [];
            }

            var rewritten = new List<TyhpdefParameter>(parameters.Count);
            foreach (var parameter in parameters)
            {
                rewritten.Add(parameter with { Type = RewriteStaticType(parameter.Type, fqn) });
            }

            return rewritten;
        }

        private static string QualifyClassName(string? namespaceName, string? className)
        {
            var name = (className ?? "").Trim().TrimStart('\\');
            if (name.Length == 0)
            {
                return "";
            }

            var ns = (namespaceName ?? "").Trim().TrimStart('\\');
            return ns.Length == 0 ? "\\" + name : "\\" + ns + "\\" + name;
        }

        private static string RewriteStaticType(string? type, string classFqn)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return type ?? "";
            }

            return StaticTypeToken.Replace(type, classFqn);
        }

        private static readonly Regex StaticTypeToken = new(
            @"(?<![\w\\])static(?![\w\\])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static int CountTypes(TyhpdefFile file)
            => (file.GlobalTypes?.Count ?? 0)
                + (file.Namespaces ?? []).Sum(n => n.Classes?.Count ?? 0)
                + (file.DeclareBlocks ?? []).Sum(b =>
                    (b.Classes?.Count ?? 0) + (b.Namespaces ?? []).Sum(n => n.Classes?.Count ?? 0));

        private static int CountFunctions(TyhpdefFile file)
            => (file.GlobalFunctions?.Count ?? 0)
                + (file.Namespaces ?? []).Sum(n => n.Functions?.Count ?? 0)
                + (file.DeclareBlocks ?? []).Sum(b =>
                    (b.Functions?.Count ?? 0) + (b.Namespaces ?? []).Sum(n => n.Functions?.Count ?? 0));

        private static int CountConstants(TyhpdefFile file)
            => (file.GlobalConstants?.Count ?? 0)
                + (file.Namespaces ?? []).Sum(n => n.Constants?.Count ?? 0)
                + (file.DeclareBlocks ?? []).Sum(b =>
                    (b.Constants?.Count ?? 0) + (b.Namespaces ?? []).Sum(n => n.Constants?.Count ?? 0));
    }
}
