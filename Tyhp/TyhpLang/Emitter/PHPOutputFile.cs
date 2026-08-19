using System.Text;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public class PHPOutputFile
    {
        public string OutputFilePath { get; set; } = "";
        public SrcFileAst? SourceFileAst { get; set; }
        public string? GeneratedContent { get; set; }
        public EmitItem? RootEmitItem { get; set; }

        /// <summary>
        /// When set to a non-null collector before <see cref="Generate"/> runs, sourcemap tracking
        /// is active during generation and <see cref="SourceMap"/> reads the populated collector
        /// afterward. Left <see langword="null"/> ⇒ no sourcemap (fast path).
        /// </summary>
        public SourceMapCollector? SourceMapCollector { get; set; }

        /// <summary>
        /// Original <c>.tyhp</c> source path this output file was generated from. Set by
        /// <see cref="FromAstTree"/>; registered on the collector at the start of tracking
        /// <see cref="Generate"/>.
        /// </summary>
        public string? SourceFileName { get; set; }

        /// <summary>
        /// <c>sourceRoot</c> prefix passed to <see cref="SourceMapGenerator"/>. Must share a
        /// resolvable prefix with registered collector paths (filesystem directory or string
        /// prefix) or <c>sources</c> entries stay unrelativized. URL-style values such as
        /// <c>../src/</c> (relative to the output file) do not match project-relative paths like
        /// <c>src/App.tyhp</c>.
        /// </summary>
        public string? SourceRoot { get; set; }

        public List<PhpDeclareAst> FileDeclares { get; set; } = [];
        public ITopStatement? FileNameSpace { get; set; }
        public List<PhpImportDeclListAst> FileImports { get; set; } = [];
        public List<ITopStatement> Statements { get; set; } = [];
        public bool IsPSR4ObjectDeclaration { get; set; }
        public bool IsAnonymousNamespace { get; set; }
        public bool IsEntryPoint { get; set; }

        /// <summary>
        /// When true, <see cref="AutoloadDeclare"/> overrides the project
        /// <c>build.entryPointAutoloader</c> default for this entry-point file.
        /// </summary>
        public bool HasAutoloadDeclare { get; set; }

        /// <summary>Raw <c>declare(autoload=…)</c> value when <see cref="HasAutoloadDeclare"/>.</summary>
        public string AutoloadDeclare { get; set; } = "";

        public static IEnumerable<PHPOutputFile> FromAstTree(SrcFileAst rootAstNode, EmitContext context)
            => PHPOutputFileSplitter.Split(rootAstNode, context);

        public void ConvertAliases(EmitContext context)
            => new AliasConverter(context).Convert(this);

        public void PruneFileImports(EmitContext context)
        {
            // Late post-walk pass: fold in AdditionalImports (runtime classes the inline emitter
            // referenced during the walk) and drop imports that should never appear in output —
            // erased types (type aliases, generic type parameters, structs) and extension-class
            // imports whose call sites were rewritten to fully-qualified static calls.
            this.ConsolidateAdditionalImports(context);
            this.DropErasedAndFullyQualifiedImports(context);

            var usedNames = this.CollectUsedImportNames(context);
            this.FileImports = this.FileImports
                .Select(list => this.PruneImportList(list, usedNames))
                .Where(list => list.GetAllNotNull().Any())
                .ToList();

            this.PruneEmitItemImports(context, usedNames);
        }

        public void Merge(PHPOutputFile other, EmitContext context)
        {
            var thisNamespace = GetNamespaceName(this.FileNameSpace);
            var otherNamespace = GetNamespaceName(other.FileNameSpace);
            if (!string.Equals(thisNamespace, otherNamespace, StringComparison.OrdinalIgnoreCase))
            {
                context.Diagnostics.AddError(
                    MessageCode.EmitterNamespaceMismatch,
                    this.SourceFileAst?.Identifier ?? other.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    thisNamespace ?? "",
                    otherNamespace ?? "");
                return;
            }

            this.MergeDeclares(other, context);
            this.MergeImports(other);
            this.Statements.AddRange(other.Statements);
            DeclarationExistenceGateHelper.MoveGatedFunctionsToEnd(
                this.Statements,
                GetNamespaceName(this.FileNameSpace));

            if (other.RootEmitItem != null && this.RootEmitItem != null)
            {
                foreach (var child in other.RootEmitItem.Children)
                {
                    this.RootEmitItem.Children.Add(child);
                    child.Parent = this.RootEmitItem;
                }
            }

            if (!other.IsPSR4ObjectDeclaration)
            {
                this.IsPSR4ObjectDeclaration = false;
            }

            if (other.IsEntryPoint)
            {
                this.IsEntryPoint = true;
            }

            if (other.HasAutoloadDeclare && !this.HasAutoloadDeclare)
            {
                this.HasAutoloadDeclare = true;
                this.AutoloadDeclare = other.AutoloadDeclare;
            }

            this.ReconcileSourceMapState(other);
        }

        /// <summary>
        /// After merging emit trees, sourcemap tracking state on <paramref name="other"/> must
        /// land on this file so a later <see cref="Generate"/> still tracks. Existing collector
        /// contents are discarded — they describe the pre-merge PHP and would double-count if
        /// <see cref="Generate"/> ran again without a reset.
        /// </summary>
        private void ReconcileSourceMapState(PHPOutputFile other)
        {
            if (string.IsNullOrWhiteSpace(this.SourceFileName))
            {
                this.SourceFileName = other.SourceFileName;
            }

            if (string.IsNullOrWhiteSpace(this.SourceRoot))
            {
                this.SourceRoot = other.SourceRoot;
            }

            var trackingEnabled = this.SourceMapCollector != null || other.SourceMapCollector != null;
            if (trackingEnabled)
            {
                this.SourceMapCollector = new SourceMapCollector();
            }
        }

        public string Generate(EmitContext context)
        {
            var collector = this.SourceMapCollector;
            if (collector != null)
            {
                // Generate() owns the collector: a second call (duplicate output paths / merge
                // then re-emit) must not append onto a stale cursor or duplicate mappings.
                collector.Reset();
                if (!string.IsNullOrWhiteSpace(this.SourceFileName))
                {
                    collector.RegisterSourceFile(this.SourceFileName);
                }
            }

            var sb = new StringBuilder();
            void AppendPreamble(string text)
            {
                sb.Append(text);
                collector?.AddContent(text, null);
            }

            // PSR-12 §3: opening tag is its own header block and must be followed by a blank line.
            AppendPreamble("<?php\n\n");

            if (context.Config.IncludeComments)
            {
                // File-level docblock (not a // comment) so it remains a valid PSR-12 header block.
                AppendPreamble("/**\n * Generated by Tyhp compiler.\n */\n\n");
            }

            var declaresEmitted = false;
            foreach (var declare in this.FileDeclares)
            {
                var declareText = this.FormatDeclare(declare);
                if (!string.IsNullOrWhiteSpace(declareText))
                {
                    AppendPreamble(declareText);
                    AppendPreamble("\n");
                    declaresEmitted = true;
                }
            }

            if (context.Config.StrictTypes && !this.HasStrictTypesDeclare())
            {
                AppendPreamble("declare(strict_types=1);\n");
                declaresEmitted = true;
            }

            // The entry-point autoloader require_once must appear AFTER any namespace
            // declaration: PHP requires the namespace statement to be the first statement
            // after declare(...), so a require_once between them is a fatal error. Only the
            // global-namespace case keeps it in the declares block (no namespace follows).
            var entryPointRequire = this.GetEntryPointRequireLine(context);
            var hasNamespace = this.FileNameSpace is PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst;

            if (entryPointRequire != null && !hasNamespace)
            {
                AppendPreamble(entryPointRequire);
                AppendPreamble("\n");
                declaresEmitted = true;
            }

            if (declaresEmitted)
            {
                AppendPreamble("\n");
            }

            var isBlockNamespace = this.FileNameSpace is PhpBlockNamespaceDeclAst;
            var bodyIndent = isBlockNamespace ? 1 : 0;

            if (this.FileNameSpace is PhpNamespaceDeclAst statementNamespace)
            {
                var namespaceName = ApplyNamespacePrefix(statementNamespace.Identifier, context);
                AppendPreamble(string.IsNullOrWhiteSpace(namespaceName)
                    ? "namespace;\n"
                    : $"namespace {namespaceName};\n");

                if (entryPointRequire != null)
                {
                    AppendPreamble(entryPointRequire);
                    AppendPreamble("\n");
                }
            }
            else if (this.FileNameSpace is PhpBlockNamespaceDeclAst blockNamespace)
            {
                var namespaceName = ApplyNamespacePrefix(blockNamespace.Identifier, context);
                AppendPreamble(string.IsNullOrWhiteSpace(namespaceName)
                    ? "namespace {\n"
                    : $"namespace {namespaceName} {{\n");

                if (entryPointRequire != null)
                {
                    AppendPreamble("    ");
                    AppendPreamble(entryPointRequire);
                    AppendPreamble("\n");
                }
            }

            var importText = this.FormatImports(context);
            if (!string.IsNullOrWhiteSpace(importText))
            {
                // PSR-12 §3: exactly one blank line before the first use group.
                // Declares already leave a trailing blank when present and there is no namespace;
                // with a namespace (or with neither declare nor namespace) we still need one here.
                if (hasNamespace || !declaresEmitted)
                {
                    AppendPreamble("\n");
                }

                AppendPreamble(importText);
                AppendPreamble("\n");
            }

            // Peek the body without the collector first: the pre-body blank-line separator below
            // must be appended (to both `sb` and `collector`) BEFORE the tracked body emission
            // begins, or every mapping in the body would be recorded one line too early (the
            // collector would still be sitting at the position from before that separator existed).
            var bodyPeek = this.EmitBody(context, bodyIndent, collector: null);
            if (!string.IsNullOrWhiteSpace(bodyPeek))
            {
                // PSR-12 §3: exactly one blank line before the remainder of the file.
                // Declares already leave that blank when there is no namespace and no imports;
                // otherwise we still need a separator after the namespace / use block.
                var needsBlankBeforeBody = hasNamespace
                    || !string.IsNullOrWhiteSpace(importText)
                    || !declaresEmitted;
                if (needsBlankBeforeBody && !bodyPeek.StartsWith('\n'))
                {
                    AppendPreamble("\n");
                }

                var bodyStartLine = collector?.CurrentGeneratedLine ?? 0;
                var bodyStartColumn = collector?.CurrentGeneratedColumn ?? 0;
                var body = collector != null ? this.EmitBody(context, bodyIndent, collector) : bodyPeek;

                var trimmedBody = body.TrimEnd();
                sb.Append(trimmedBody);

                // TrimEnd() only strips trailing whitespace, which the tracked emit above already
                // advanced the collector through (whitespace-only content never carries a mapping,
                // so no mapping needs correcting) — but the collector's *position* is now ahead of
                // what was actually written to `sb`. Recompute it from the trimmed text so the next
                // AddContent (or the closing `}` below) records against the real generated position.
                if (collector != null && trimmedBody.Length != body.Length)
                {
                    var (line, column) = ComputeAdvancedPosition(bodyStartLine, bodyStartColumn, trimmedBody);
                    collector.SetPosition(line, column);
                }

                AppendPreamble("\n");
            }

            if (isBlockNamespace)
            {
                AppendPreamble("}\n");
            }

            this.GeneratedContent = sb.ToString().Replace("\r\n", "\n");
            return this.GeneratedContent;
        }

        /// <summary>
        /// Simulates <see cref="SourceMapCollector"/>'s own position tracking for
        /// <paramref name="content"/> starting from an explicit <paramref name="startLine"/> /
        /// <paramref name="startColumn"/>, without touching a collector. Used to recompute the
        /// correct generated position after trimming trailing whitespace that a collector already
        /// advanced through (see <see cref="Generate"/>).
        /// </summary>
        private static (int Line, int Column) ComputeAdvancedPosition(int startLine, int startColumn, string content)
        {
            var line = startLine;
            var lastNewline = -1;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] == '\n')
                {
                    line++;
                    lastNewline = i;
                }
            }

            var column = lastNewline >= 0 ? content.Length - lastNewline - 1 : startColumn + content.Length;
            return (line, column);
        }

        private string? GetEntryPointRequireLine(EmitContext context)
        {
            if (!this.IsEntryPoint)
            {
                return null;
            }

            var autoloaderRelativeToOutput = this.HasAutoloadDeclare
                ? EmitConfig.ResolveAutoloadDirectiveValue(
                    this.AutoloadDeclare,
                    context.Config.EntryPointAutoloaderMap)
                : context.Config.EntryPointAutoloader;

            if (string.IsNullOrWhiteSpace(autoloaderRelativeToOutput))
            {
                return null;
            }

            autoloaderRelativeToOutput = autoloaderRelativeToOutput
                .Replace('\\', '/')
                .TrimStart('/');
            var relativeFromEntry = ResolveAutoloaderPathFromEntryPoint(
                this.OutputFilePath,
                context.Config.OutputPath,
                autoloaderRelativeToOutput);
            return $"require_once __DIR__ . '/{relativeFromEntry}';";
        }

        /// <summary>
        /// Builds a path from the entry-point file's directory to
        /// <c>{outputPath}/{autoloaderRelativeToOutput}</c>, suitable for
        /// <c>require_once __DIR__ . '/…'</c>. Nested entry points (e.g.
        /// <c>build/src/TestEmitter/test.php</c>) become
        /// <c>../../vendor/autoload.php</c>.
        /// </summary>
        internal static string ResolveAutoloaderPathFromEntryPoint(
            string outputFilePath,
            string outputPath,
            string autoloaderRelativeToOutput)
        {
            var entryRelativeToOutput = GetPathRelativeToOutput(outputFilePath, outputPath);
            var entryDir = Path.GetDirectoryName(entryRelativeToOutput)?.Replace('\\', '/') ?? "";
            if (string.IsNullOrWhiteSpace(entryDir) || entryDir == ".")
            {
                return autoloaderRelativeToOutput;
            }

            var depth = entryDir.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
            var ups = string.Join("/", Enumerable.Repeat("..", depth));
            return ups + "/" + autoloaderRelativeToOutput;
        }

        private static string GetPathRelativeToOutput(string outputFilePath, string outputPath)
        {
            var normalized = outputFilePath.Replace('\\', '/');
            var prefix = outputPath.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return normalized.TrimStart('/');
            }

            // Handle "./build" vs "build" and optional leading "./" on the file path.
            var normalizedPrefix = prefix.StartsWith("./", StringComparison.Ordinal)
                ? prefix[2..]
                : prefix;
            var normalizedFile = normalized.StartsWith("./", StringComparison.Ordinal)
                ? normalized[2..]
                : normalized;

            if (normalizedFile.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFile[(normalizedPrefix.Length + 1)..];
            }

            if (normalizedFile.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFile[(prefix.Length + 1)..];
            }

            return normalizedFile.TrimStart('/');
        }

        /// <summary>
        /// Build Source Map v3 JSON from mappings collected during a tracking <see cref="Generate"/>.
        /// </summary>
        /// <param name="includeSourcesContent">
        /// When <see langword="true"/>, embed original source text via
        /// <paramref name="sourceContentProvider"/>.
        /// </param>
        /// <param name="sourceContentProvider">
        /// Callback invoked with each original registered source path (not the relativized
        /// <c>sources</c> entry). Ignored when <paramref name="includeSourcesContent"/> is false.
        /// </param>
        /// <returns>
        /// JSON string, or empty when <see cref="SourceMapCollector"/> is null (tracking was not
        /// enabled for <see cref="Generate"/>).
        /// </returns>
        public string SourceMap(
            bool includeSourcesContent = false,
            Func<string, string?>? sourceContentProvider = null)
        {
            if (this.SourceMapCollector == null)
            {
                return string.Empty;
            }

            var generatedFileName = Path.GetFileName(this.OutputFilePath) ?? string.Empty;
            var generator = new SourceMapGenerator(generatedFileName, this.SourceRoot);
            return generator.Generate(
                this.SourceMapCollector,
                includeSourcesContent,
                sourceContentProvider);
        }

        private string EmitBody(EmitContext context, int indentLevel, SourceMapCollector? collector = null)
        {
            if (this.RootEmitItem == null)
            {
                return "";
            }

            var sb = new StringBuilder();
            this.AppendBodyChildren(sb, this.RootEmitItem, indentLevel, collector);
            return sb.ToString();
        }

        private void AppendBodyChildren(
            StringBuilder sb,
            EmitItem parent,
            int indentLevel,
            SourceMapCollector? collector)
        {
            var first = true;
            foreach (var child in parent.SortedChildren())
            {
                if (child.value.EmitType is EmitType.FileDeclare
                    or EmitType.FileNamespaceDeclaration
                    or EmitType.ImportUse)
                {
                    continue;
                }

                if (child.value.EmitType == EmitType.BlockNamespaceDeclaration)
                {
                    var nestedPeek = new StringBuilder();
                    this.AppendBodyChildren(nestedPeek, child.value, indentLevel, collector: null);
                    var nestedText = nestedPeek.ToString();
                    if (string.IsNullOrWhiteSpace(nestedText))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append("\n\n");
                        collector?.AddContent("\n\n", null);
                    }

                    if (collector != null)
                    {
                        this.AppendBodyChildren(sb, child.value, indentLevel, collector);
                    }
                    else
                    {
                        sb.Append(nestedText);
                    }

                    first = false;
                    continue;
                }

                var peekText = child.value.emit(indentLevel);
                if (string.IsNullOrWhiteSpace(peekText))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append("\n\n");
                    collector?.AddContent("\n\n", null);
                }

                sb.Append(collector != null
                    ? child.value.emit(indentLevel, collector)
                    : peekText);
                first = false;
            }
        }

        private void PruneEmitItemImports(EmitContext context, HashSet<string> usedNames)
        {
            if (this.RootEmitItem == null)
            {
                return;
            }

            this.RootEmitItem.Children.RemoveAll(child =>
                child.EmitType == EmitType.ImportUse && !this.ImportEmitItemIsUsed(child, usedNames));
        }

        /// <summary>
        /// Late pass step 1: merge <see cref="EmitContext.AdditionalImports"/> (fully-qualified names
        /// the inline emitter needs imported — e.g. runtime helper classes) into this file's import
        /// list, de-duplicated against existing <c>use</c> clauses. The entries are class imports; the
        /// existing usage-based pruning pass drops any that the file body does not actually reference.
        /// </summary>
        private void ConsolidateAdditionalImports(EmitContext context)
        {
            if (context.AdditionalImports.Count == 0)
            {
                return;
            }

            foreach (var fqn in context.AdditionalImports)
            {
                this.AddImportClauseIfNeeded(fqn);
            }
        }

        private void AddImportClauseIfNeeded(string fqn)
        {
            var normalized = (fqn ?? "").TrimStart('\\');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            foreach (var list in this.FileImports)
            {
                foreach (var existing in list.GetAllNotNull())
                {
                    if (string.Equals(existing.NamespaceName?.TrimStart('\\'), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            var import = PhpImportDeclAst.CreateFromContext(
                normalized,
                alias: null,
                useType: null,
                this.SourceFileAst ?? new TyhpSrcFileAst());

            if (this.FileImports.Count == 0)
            {
                this.FileImports.Add(new PhpImportDeclListAst());
            }

            if (this.FileImports[^1] is Base2Ast listNode)
            {
                listNode.AddChild(import);
            }
        }

        /// <summary>
        /// Late pass step 2: drop imports that must never reach the output file header.
        /// <list type="bullet">
        /// <item>Erased types — type aliases, object-scoped type aliases, generic type parameters, and
        /// struct declarations (structs erase to <c>array</c>), so a <c>use</c> for them is dead.</item>
        /// <item>Extension-class imports whose call sites were rewritten to fully-qualified static
        /// calls (e.g. <c>\Tyhp\Extensions\StringExtensions::method()</c>) — the leading backslash makes
        /// the <c>use</c> clause redundant.</item>
        /// </list>
        /// Only imports that resolve to a known Tyhp erased symbol are dropped; unresolved imports
        /// (external PHP classes, builtins) are kept conservatively.
        /// </summary>
        private void DropErasedAndFullyQualifiedImports(EmitContext context)
        {
            var fqStaticCallFqns = context.GetFullyQualifiedStaticCallImportsForFile(this);
            if (fqStaticCallFqns.Count == 0 && context.AdditionalImports.Count == 0 && this.FileImports.Count == 0)
            {
                // No possible work; skip constructing a resolver.
                return;
            }

            var resolver = new NameResolver(context.GetSymbolTree(), context.Diagnostics);

            // Computed lazily and only when a fully-qualified-static-call import is present: the drop
            // for those must not remove a `use` that a bare (non-fully-qualified) reference still needs.
            string? bodyText = null;

            foreach (var list in this.FileImports)
            {
                if (list is not Base2Ast listNode)
                {
                    continue;
                }

                var kept = new List<PhpImportDeclAst>();
                foreach (var import in list.GetAllNotNull())
                {
                    var fqn = (import.NamespaceName ?? "").TrimStart('\\');
                    if (string.IsNullOrWhiteSpace(fqn))
                    {
                        kept.Add(import);
                        continue;
                    }

                    if (fqStaticCallFqns.Contains(fqn))
                    {
                        bodyText ??= this.EmitBody(context, indentLevel: 0);
                        if (FullyQualifiedStaticCallImportIsRedundant(fqn, bodyText))
                        {
                            continue;
                        }
                    }

                    if (IsErasedTypeImport(resolver, fqn))
                    {
                        continue;
                    }

                    kept.Add(import);
                }

                listNode.ClearChildren();
                foreach (var import in kept)
                {
                    listNode.AddChild(import);
                }
            }
        }

        /// <summary>
        /// Returns true when a <c>use</c> for <paramref name="fqn"/> is redundant given that the emitter
        /// rewrote its call sites to leading-backslash fully-qualified static calls
        /// (e.g. <c>\App\Ext::method()</c>). The import is only redundant if the body contains the class's
        /// short name solely as part of those fully-qualified references; if a bare short-name reference
        /// (e.g. a direct <c>Ext::other()</c>) also exists, the <c>use</c> is still required and kept.
        /// </summary>
        private static bool FullyQualifiedStaticCallImportIsRedundant(string fqn, string bodyText)
        {
            var shortName = fqn.Split('\\')[^1];
            if (string.IsNullOrEmpty(shortName))
            {
                return true;
            }

            // Strip the fully-qualified occurrences the rewrite emits (leading backslash + FQN); any
            // remaining short-name occurrence is a bare reference that still needs the import.
            var withoutFullyQualified = bodyText.Replace("\\" + fqn, string.Empty, StringComparison.Ordinal);
            return !withoutFullyQualified.Contains(shortName, StringComparison.Ordinal);
        }

        private static bool IsErasedTypeImport(NameResolver resolver, string fqn)
        {
            var segments = fqn.Split('\\');
            var symbol = resolver.ResolveQualifiedName(segments);
            return symbol switch
            {
                TypeAliasSymbol => true,
                ObjectTypeAliasSymbol => true,
                GenericTypeParameterSymbol => true,
                ObjectDeclarationSymbol obj when obj.IsStruct => true,
                _ => false,
            };
        }

        private bool ImportEmitItemIsUsed(EmitItem item, HashSet<string> usedNames)
        {
            if (usedNames.Count == 0)
            {
                return false;
            }

            var line = item.StartContent.FirstOrDefault() ?? "";
            foreach (var used in usedNames)
            {
                var shortName = used.Split('\\')[^1];
                if (line.Contains(shortName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private HashSet<string> CollectUsedImportNames(EmitContext context)
        {
            var used = new HashSet<string>(context.GetUsedImportsForFile(this), StringComparer.OrdinalIgnoreCase);
            var bodyText = this.EmitBody(context, indentLevel: 0);

            foreach (var importList in this.FileImports)
            {
                foreach (var import in importList.GetAllNotNull())
                {
                    var fqn = import.NamespaceName ?? "";
                    var shortName = string.IsNullOrWhiteSpace(import.Identifier)
                        ? fqn.Split('\\')[^1]
                        : import.Identifier;
                    if (!string.IsNullOrWhiteSpace(shortName)
                        && bodyText.Contains(shortName, StringComparison.Ordinal))
                    {
                        used.Add(fqn);
                        used.Add(shortName);
                    }
                }
            }

            return used;
        }

        private PhpImportDeclListAst PruneImportList(PhpImportDeclListAst importList, HashSet<string> usedNames)
        {
            var pruned = importList.GetAllNotNull()
                .Where(import => this.IsImportUsed(import, usedNames))
                .OrderBy(import => import.NamespaceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(import => import.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (importList is Base2Ast baseNode)
            {
                baseNode.ClearChildren();
                foreach (var import in pruned)
                {
                    baseNode.AddChild(import);
                }
            }

            return importList;
        }

        private bool IsImportUsed(PhpImportDeclAst import, HashSet<string> usedNames)
        {
            if (usedNames.Count == 0)
            {
                return false;
            }

            var fqn = import.NamespaceName ?? "";
            if (usedNames.Contains(fqn))
            {
                return true;
            }

            var shortName = string.IsNullOrWhiteSpace(import.Identifier)
                ? fqn.Split('\\')[^1]
                : import.Identifier;
            return usedNames.Contains(shortName);
        }

        private void MergeImports(PHPOutputFile other)
        {
            foreach (var otherList in other.FileImports)
            {
                foreach (var otherImport in otherList.GetAllNotNull())
                {
                    if (this.FileImports.Any(list => list.GetAllNotNull().Any(i =>
                            string.Equals(i.NamespaceName, otherImport.NamespaceName, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(i.Identifier, otherImport.Identifier, StringComparison.OrdinalIgnoreCase))))
                    {
                        continue;
                    }

                    if (this.FileImports.Count == 0)
                    {
                        this.FileImports.Add(new PhpImportDeclListAst());
                    }

                    if (this.FileImports[^1] is Base2Ast listNode)
                    {
                        listNode.AddChild(otherImport);
                    }
                }
            }
        }

        private void MergeDeclares(PHPOutputFile other, EmitContext context)
        {
            foreach (var declare in other.FileDeclares)
            {
                if (this.FileDeclares.Any(existing => this.DeclaresAreEquivalent(existing, declare)))
                {
                    continue;
                }

                if (this.HasConflictingStrictTypes(existing: this.FileDeclares, incoming: declare))
                {
                    context.Diagnostics.AddWarning(
                        MessageCode.EmitterMergeConflict,
                        this.SourceFileAst?.Identifier ?? "",
                        0,
                        0,
                        this.OutputFilePath,
                        "conflicting declare() directives");
                }

                this.FileDeclares.Add(declare);
            }
        }

        private bool DeclaresAreEquivalent(PhpDeclareAst left, PhpDeclareAst right)
            => FormatDeclare(left) == FormatDeclare(right);

        private bool HasConflictingStrictTypes(IEnumerable<PhpDeclareAst> existing, PhpDeclareAst incoming)
        {
            var incomingStrict = GetDeclareValue(incoming, "strict_types");
            if (incomingStrict == null)
            {
                return false;
            }

            foreach (var declare in existing)
            {
                var value = GetDeclareValue(declare, "strict_types");
                if (value != null && !string.Equals(value, incomingStrict, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? GetDeclareValue(PhpDeclareAst declare, string key)
        {
            var constDecl = declare.Declarations?.GetAllNotNull()
                .FirstOrDefault(c => string.Equals(c.Identifier, key, StringComparison.OrdinalIgnoreCase));
            if (constDecl?.Value is PhpScalarAst scalar)
            {
                return scalar.ValueString ?? scalar.ValueInt64?.ToString();
            }

            return null;
        }

        private string FormatImports(EmitContext context)
        {
            var imports = this.FileImports
                .SelectMany(list => list.GetAllNotNull())
                .GroupBy(i => i.UseType?.ValueString ?? "")
                .OrderBy(g => ImportGroupRank(g.Key))
                .ToList();

            // PSR-12 §3: one import per statement; blank line between class / function / const groups.
            var groupTexts = new List<string>();
            foreach (var group in imports)
            {
                var prefix = group.Key switch
                {
                    "function" => "use function ",
                    "const" => "use const ",
                    _ => "use ",
                };

                var lines = group
                    .OrderBy(i => i.NamespaceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.Identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(import =>
                    {
                        var fqn = import.NamespaceName ?? "";
                        context.UsedImports.Add(fqn);
                        var clause = string.IsNullOrWhiteSpace(import.Identifier)
                            ? fqn
                            : $"{fqn} as {import.Identifier}";
                        return prefix + clause + ";";
                    })
                    .ToList();

                if (lines.Count > 0)
                {
                    groupTexts.Add(string.Join("\n", lines));
                }
            }

            return string.Join("\n\n", groupTexts);
        }

        private static int ImportGroupRank(string useType) => useType switch
        {
            "function" => 1,
            "const" => 2,
            _ => 0,
        };

        private string FormatDeclare(PhpDeclareAst declare)
        {
            if (declare.Declarations == null)
            {
                return "declare();";
            }

            var parts = declare.Declarations.GetAllNotNull()
                .Where(c => !IsTyhpOnlyDeclareKey(c.Identifier))
                .Select(c =>
                {
                    var value = c.Value switch
                    {
                        PhpScalarAst scalar when scalar.ValueString != null => $"'{scalar.ValueString}'",
                        PhpScalarAst scalar when scalar.ValueInt64.HasValue => scalar.ValueInt64.Value.ToString(),
                        PhpScalarAst scalar when scalar.ValueBoolean.HasValue => scalar.ValueBoolean.Value ? "true" : "false",
                        _ => "1",
                    };
                    return $"{c.Identifier}={value}";
                })
                .ToList();

            if (parts.Count == 0)
            {
                return "";
            }

            return "declare(" + string.Join(", ", parts) + ");";
        }

        private static bool IsTyhpOnlyDeclareKey(string? identifier) =>
            string.Equals(identifier, "output_file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(identifier, "autoload", StringComparison.OrdinalIgnoreCase);

        private bool HasStrictTypesDeclare()
            => this.FileDeclares.Any(d => GetDeclareValue(d, "strict_types") == "1");

        private static string? GetNamespaceName(ITopStatement? namespaceStatement)
            => namespaceStatement switch
            {
                PhpNamespaceDeclAst ns => ns.Identifier,
                PhpBlockNamespaceDeclAst block => block.Identifier,
                _ => null,
            };

        private static string ApplyNamespacePrefix(string? namespaceName, EmitContext context)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                return "";
            }

            var prefix = context.Config.NamespacePrefix;
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return namespaceName;
            }

            return $"{prefix.TrimEnd('\\')}\\{namespaceName.TrimStart('\\')}";
        }
    }
}
