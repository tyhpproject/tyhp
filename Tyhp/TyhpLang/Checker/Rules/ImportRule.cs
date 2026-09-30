using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Tracks file-level imports and reports unused, duplicate, or conflicting aliases.
    /// </summary>
    public sealed class ImportRule : ICheckerRule
    {
        private readonly Dictionary<string, FileImportState> _importsByFile = new(StringComparer.Ordinal);

        public IEnumerable<Type> HandledNodeTypes =>
        [
            // Only visit decls — not PhpImportDeclListAst — so each use is registered once.
            // Visiting both double-registered every import and spuriously emitted 4131.
            typeof(PhpImportDeclAst),
            typeof(TyhpImportExtensionAst),
            typeof(PhpNameAst),
        ];

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            var fileName = state.CurrentFileName ?? node.OwningFile?.FileName ?? string.Empty;
            EnsureFile(fileName);

            switch (node)
            {
                case PhpImportDeclAst import:
                    RegisterImport(import, fileName, diagnostics, state, context);
                    break;
                case TyhpImportExtensionAst importExtension:
                    CheckRedundantExtensionImport(importExtension, fileName, diagnostics, state, context);
                    break;
                case PhpNameAst name:
                    MarkImportUsed(name, fileName);
                    break;
            }
        }

        public void FlushRemainingImports(DiagnosticBag diagnostics)
        {
            // Report after the full multi-file walk so every PhpNameAst has been seen, and so
            // diagnostics are attributed to each import's own file (not whatever file was active
            // when a mid-walk flush used to run).
            foreach (var fileName in _importsByFile.Keys)
            {
                ReportUnusedImports(fileName, diagnostics);
            }
        }

        /// <summary>
        /// Scans an AST subtree for <see cref="PhpNameAst"/> spellings — including
        /// <c>AstGrammarAddons</c> (generic type arguments) — and marks matching imports used.
        /// Does not dispatch other checker rules (avoids re-entrancy from suppressed walks).
        /// </summary>
        public void MarkNamesIn(IBase2Ast? node, string fileName)
        {
            if (node is null || string.IsNullOrEmpty(fileName))
            {
                return;
            }

            EnsureFile(fileName);
            var visited = new HashSet<IBase2Ast>();
            MarkNamesInCore(node, fileName, visited);
        }

        private void MarkNamesInCore(IBase2Ast node, string fileName, HashSet<IBase2Ast> visited)
        {
            if (!visited.Add(node))
            {
                return;
            }

            if (node is PhpNameAst name)
            {
                MarkImportUsed(name, fileName);
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    MarkNamesInCore(child, fileName, visited);
                }
            }

            foreach (var attr in node.AstAttributes)
            {
                if (attr is not null)
                {
                    MarkNamesInCore(attr, fileName, visited);
                }
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                if (addon is not null)
                {
                    MarkNamesInCore(addon, fileName, visited);
                }
            }
        }

        private void EnsureFile(string fileName)
        {
            if (!_importsByFile.ContainsKey(fileName))
            {
                _importsByFile[fileName] = new FileImportState();
            }
        }

        private void RegisterImport(
            PhpImportDeclAst import,
            string fileName,
            DiagnosticBag diagnostics,
            CheckerState state,
            CheckerRuleContext context)
        {
            var importedName = import.NamespaceName ?? string.Empty;
            if (string.IsNullOrEmpty(importedName))
            {
                return;
            }

            var alias = string.IsNullOrEmpty(import.Identifier)
                ? importedName[(importedName.LastIndexOf('\\') + 1)..]
                : import.Identifier;

            var fileState = _importsByFile[fileName];
            var useType = ResolveUseType(import);

            if (!import.IsGlobal && IsRedundantGlobalClassImport(import, alias, useType, context))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, import, MessageCode.CheckerRedundantGlobalImport, importedName);
            }

            if (fileState.ImportsByFqn.TryGetValue(importedName, out var existingImport))
            {
                var fileNameForDup = CheckerHelpers.ResolveDiagnosticFileName(state, import);
                diagnostics.AddDuplicateFromAst(
                    DiagnosticSeverity.Warning,
                    MessageCode.CheckerDuplicateImport,
                    import,
                    fileNameForDup,
                    existingImport.Declaration,
                    fileNameForDup,
                    importedName);
                return;
            }

            var record = new ImportRecord(import, alias, useType, isUsed: false);
            fileState.ImportsByFqn[importedName] = record;

            if (fileState.ImportsByAlias.TryGetValue(alias, out var existingAlias)
                && !string.Equals(existingAlias.ImportedName, importedName, StringComparison.Ordinal))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, import, MessageCode.CheckerConflictingImportAlias, alias);
            }
            else
            {
                // Same instance as ImportsByFqn so MarkImportUsed via alias updates the FQN record.
                fileState.ImportsByAlias[alias] = record;
            }
        }

        private static PhpUseType ResolveUseType(PhpImportDeclAst import) =>
            import.UseType?.ValueString?.ToLowerInvariant() switch
            {
                "const" => PhpUseType.Const,
                "function" => PhpUseType.Function,
                _ => PhpUseType.Class,
            };

        private static bool IsRedundantGlobalClassImport(
            PhpImportDeclAst import,
            string localAlias,
            PhpUseType useType,
            CheckerRuleContext context)
        {
            var imported = (import.NamespaceName ?? "").Trim().TrimStart('\\');
            if (imported.Length == 0)
            {
                return false;
            }

            foreach (var global in context.GlobalScope.GlobalImports)
            {
                if (global.UseType != useType)
                {
                    continue;
                }

                if (!string.Equals(global.ImportedName.TrimStart('\\'), imported, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var globalAlias = string.IsNullOrEmpty(global.AliasName) ? global.Name : global.AliasName;
                return string.Equals(localAlias, globalAlias, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void CheckRedundantExtensionImport(
            TyhpImportExtensionAst importExt,
            string fileName,
            DiagnosticBag diagnostics,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (importExt.IsGlobal)
            {
                return;
            }

            var declarations = importExt.UseDeclarations?.GetAllNotNull().ToList() ?? [];
            if (declarations.Count == 0)
            {
                return;
            }

            // Multiple extensions may share one `use extension Foo, Bar { … }` statement with a
            // single adaptations block. A mutation targeting one of them (by qualifier, or
            // unqualified when it is the only extension listed) must not silence the redundant
            // warning for the others — each imported name is judged independently (Story 20
            // decision 18: "names in the same group that do alias or adapt are silent").
            var soleExtension = declarations.Count == 1;
            var adaptations = importExt.Adaptations?.GetAllNotNull().ToList() ?? [];

            foreach (var decl in declarations)
            {
                var imported = (decl.NamespaceName ?? "").Trim().TrimStart('\\');
                if (imported.Length == 0)
                {
                    continue;
                }

                var lastSegment = imported[(imported.LastIndexOf('\\') + 1)..];
                var localAlias = string.IsNullOrEmpty(decl.Identifier) ? lastSegment : decl.Identifier;
                var aliasesToSameShortName = string.Equals(localAlias, lastSegment, StringComparison.OrdinalIgnoreCase);

                if (!aliasesToSameShortName && !string.IsNullOrEmpty(decl.Identifier))
                {
                    // Renamed the import itself — a mutation for this extension.
                    continue;
                }

                if (HasAdaptationMutatingExtension(adaptations, lastSegment, soleExtension))
                {
                    continue;
                }

                foreach (var ext in context.GlobalScope.GloballyActivatedExtensions)
                {
                    var fqn = (string.IsNullOrEmpty(ext.FullyQualifiedName) ? ext.Name : ext.FullyQualifiedName)
                        .TrimStart('\\');
                    if (string.Equals(fqn, imported, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext.Name, imported, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext.Name, lastSegment, StringComparison.OrdinalIgnoreCase))
                    {
                        CheckerHelpers.ReportWarning(
                            diagnostics,
                            state,
                            decl,
                            MessageCode.CheckerRedundantGlobalImport,
                            decl.NamespaceName ?? imported);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// True when at least one adaptation in the shared <c>{ … }</c> block mutates
        /// <paramref name="extensionShortName"/> specifically: a qualified reference naming that
        /// extension (<c>Ext::member hide;</c>, <c>Ext::member as alias;</c>,
        /// <c>Other::member insteadof Ext;</c>), or any unqualified adaptation when it is the only
        /// extension listed on the <c>use</c> statement.
        /// </summary>
        private static bool HasAdaptationMutatingExtension(
            List<ITraitAdaptation> adaptations,
            string extensionShortName,
            bool soleExtension)
        {
            foreach (var adaptation in adaptations)
            {
                var qualifier = adaptation switch
                {
                    PhpTraitAliasAst alias => AdaptationQualifier(alias.MethodReference),
                    PhpTraitPrecedenceAst precedence => AdaptationQualifier(precedence.MethodReference),
                    _ => null,
                };

                if (string.IsNullOrEmpty(qualifier))
                {
                    if (soleExtension)
                    {
                        return true;
                    }
                }
                else if (string.Equals(qualifier.TrimStart('\\'), extensionShortName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (adaptation is PhpTraitPrecedenceAst { InsteadOfTraits: not null } precedenceAst)
                {
                    foreach (var loser in precedenceAst.InsteadOfTraits.GetAllNotNull())
                    {
                        var loserName = string.IsNullOrEmpty(loser.Identifier) ? loser.ValueString : loser.Identifier;
                        if (string.Equals(loserName?.TrimStart('\\'), extensionShortName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string? AdaptationQualifier(PhpTraitMemberRefAst? methodRef)
        {
            var traitName = methodRef?.TraitName;
            if (traitName is null)
            {
                return null;
            }

            // IClassName.Identifier defaults to "" (not null) when unset, so an empty Identifier
            // must fall through to ValueString rather than short-circuiting via `??`.
            return string.IsNullOrEmpty(traitName.Identifier) ? traitName.ValueString : traitName.Identifier;
        }

        private void MarkImportUsed(PhpNameAst name, string fileName)
        {
            if (!_importsByFile.TryGetValue(fileName, out var fileState))
            {
                return;
            }

            var referencedName = name.ValueString;
            if (string.IsNullOrEmpty(referencedName) || referencedName.StartsWith('\\'))
            {
                // A fully-qualified reference resolves from the global namespace and never
                // consumes a `use` alias.
                return;
            }

            // `use Foo\Bar;` is used by a bare `Bar` and equally by a relative `Bar\Baz`, which
            // PHP resolves through the alias to `Foo\Bar\Baz`.
            var separator = referencedName.IndexOf('\\');
            var alias = separator < 0 ? referencedName : referencedName[..separator];

            if (fileState.ImportsByAlias.TryGetValue(alias, out var record))
            {
                record.IsUsed = true;
            }

            if (name.BoundSymbol is Binder.Symbols.UseIncludeSymbol useSymbol
                && fileState.ImportsByFqn.TryGetValue(useSymbol.ImportedName, out var fqnRecord))
            {
                fqnRecord.IsUsed = true;
            }
        }

        private void ReportUnusedImports(string fileName, DiagnosticBag diagnostics)
        {
            if (!_importsByFile.TryGetValue(fileName, out var fileState))
            {
                return;
            }

            var reported = new HashSet<PhpImportDeclAst>();
            foreach (var record in fileState.ImportsByFqn.Values)
            {
                if (record.IsUsed || !reported.Add(record.Declaration))
                {
                    continue;
                }

                // Prefer the declaration's owning file so attribution cannot drift to another file.
                var reportFile = record.Declaration.OwningFile?.FileName ?? fileName;
                diagnostics.AddWarningFromAst(
                    MessageCode.CheckerUnusedImport,
                    record.Declaration,
                    reportFile,
                    record.ImportedName);
            }
        }

        private sealed class FileImportState
        {
            public Dictionary<string, ImportRecord> ImportsByFqn { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, ImportRecord> ImportsByAlias { get; } = new(StringComparer.Ordinal);
        }

        private sealed class ImportRecord(
            PhpImportDeclAst declaration,
            string alias,
            PhpUseType useType,
            bool isUsed)
        {
            public PhpImportDeclAst Declaration { get; } = declaration;
            public string ImportedName => Declaration.NamespaceName ?? string.Empty;
            public string Alias { get; } = alias;
            public PhpUseType UseType { get; } = useType;
            public bool IsUsed { get; set; } = isUsed;
        }
    }
}
