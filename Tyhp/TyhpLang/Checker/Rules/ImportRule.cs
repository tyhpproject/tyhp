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
            typeof(PhpNameAst),
        ];

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            var fileName = state.CurrentFileName ?? node.OwningFile?.FileName ?? string.Empty;
            EnsureFile(fileName);

            switch (node)
            {
                case PhpImportDeclAst import:
                    RegisterImport(import, fileName, diagnostics, state);
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
            CheckerState state)
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

            if (fileState.ImportsByFqn.ContainsKey(importedName))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, import, MessageCode.CheckerDuplicateImport, importedName);
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
            public string ImportedName => declaration.NamespaceName ?? string.Empty;
            public string Alias { get; } = alias;
            public PhpUseType UseType { get; } = useType;
            public bool IsUsed { get; set; } = isUsed;
        }
    }
}
