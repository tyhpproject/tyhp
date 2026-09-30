namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Shared <c>use</c> statement insert, match, and sort helpers for completion
    /// auto-import and code actions / formatting.
    /// </summary>
    internal static class UseStatementEdits
    {
        internal enum UseKind
        {
            Class = 0,
            Function = 1,
            Const = 2,
            Extension = 3,
        }

        /// <summary>
        /// Text edit that inserts a <c>use</c> statement for <paramref name="symbol"/>,
        /// or null when the symbol is already visible or cannot be imported.
        /// </summary>
        public static TextEdit[]? TryCreateImportEdits(IBaseScope? fromScope, BaseSymbol symbol, string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            if (fromScope is not null && !NeedsImport(symbol, fromScope))
            {
                return null;
            }

            string fqn = symbol.FullyQualifiedName.TrimStart('\\');
            if (string.IsNullOrEmpty(fqn))
            {
                return null;
            }

            if (!fqn.Contains('\\', StringComparison.Ordinal)
                && (fromScope is null || string.IsNullOrEmpty(GetCurrentNamespace(fromScope))))
            {
                return null;
            }

            string statement = ImportKeyword(symbol) + fqn + ";" + Environment.NewLine;
            ProtocolRange insertRange = FindUseInsertRange(content);
            return
            [
                new TextEdit
                {
                    Range = insertRange,
                    NewText = statement,
                },
            ];
        }

        public static bool NeedsImport(BaseSymbol symbol, IBaseScope fromScope)
        {
            if (symbol is BuiltInTypeSymbol or BuiltInUtilityTypeSymbol or BuiltInFunctionSymbol or MagicConstantSymbol)
            {
                return false;
            }

            HashSet<string> imported = CollectImportedNames(fromScope);
            string currentNs = GetCurrentNamespace(fromScope);
            return !IsVisibleWithoutImport(symbol, currentNs, imported);
        }

        public static bool IsVisibleWithoutImport(BaseSymbol symbol, string currentNs, HashSet<string> imported)
        {
            string fqn = symbol.FullyQualifiedName.TrimStart('\\');
            if (imported.Contains(fqn) || imported.Contains(symbol.Name))
            {
                return true;
            }

            string typeNs = NamespaceOf(fqn);
            return string.Equals(typeNs, currentNs, StringComparison.OrdinalIgnoreCase);
        }

        public static HashSet<string> CollectImportedNames(IBaseScope? fromScope)
        {
            var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                if (scope is NamespaceBlockScope or FileScope)
                {
                    foreach (IBaseSymbol child in scope.GetAllChildSymbols())
                    {
                        if (child is UseIncludeSymbol use)
                        {
                            imported.Add(use.ImportedName.TrimStart('\\'));
                            imported.Add(use.AliasName ?? use.Name);
                        }
                    }
                }

                if (scope is FileScope or NamespaceScope or GlobalScope)
                {
                    break;
                }

                scope = scope.ParentScope;
            }

            return imported;
        }

        public static string GetCurrentNamespace(IBaseScope? fromScope)
        {
            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is NamespaceBlockSymbol block)
                {
                    return block.Name.Trim('\\');
                }

                if (scope.DeclarationSymbol is NamespaceSymbol ns)
                {
                    return ns.Name.Trim('\\');
                }

                scope = scope.ParentScope;
            }

            return string.Empty;
        }

        /// <summary>
        /// Zero-width insert position after the last file-level <c>use</c>, else after
        /// <c>namespace</c>, else after the open tag.
        /// </summary>
        public static ProtocolRange FindUseInsertRange(string content)
        {
            content ??= string.Empty;
            int brace = 0;
            int lastUseEnd = -1;
            int namespaceEnd = -1;
            int openTagEnd = -1;
            int index = 0;
            int length = content.Length;
            while (index < length)
            {
                int lineStart = index;
                while (index < length && content[index] != '\n' && content[index] != '\r')
                {
                    index++;
                }

                string line = content[lineStart..index];
                string trimmed = line.TrimStart();
                if (brace == 0)
                {
                    if (trimmed.StartsWith("<?tyhp", StringComparison.Ordinal)
                        || trimmed.StartsWith("<?php", StringComparison.Ordinal))
                    {
                        openTagEnd = LineEndOffset(content, index);
                    }
                    else if (trimmed.StartsWith("namespace ", StringComparison.Ordinal))
                    {
                        namespaceEnd = LineEndOffset(content, index);
                    }
                    else if (IsUseLine(trimmed))
                    {
                        lastUseEnd = LineEndOffset(content, index);
                    }
                }

                foreach (char c in line)
                {
                    if (c == '{')
                    {
                        brace++;
                    }
                    else if (c == '}' && brace > 0)
                    {
                        brace--;
                    }
                }

                index = LineEndOffset(content, index);
            }

            int insertAt = lastUseEnd >= 0 ? lastUseEnd : namespaceEnd >= 0 ? namespaceEnd : openTagEnd >= 0 ? openTagEnd : 0;
            Position pos = PositionUtilities.GetPosition(content, insertAt);
            return new ProtocolRange { Start = pos, End = pos };
        }

        /// <summary>
        /// Sorts simple file-level <c>use</c> groups (classes, then functions, then
        /// constants, then extensions). Group-use / commented import blocks are left
        /// unchanged.
        /// </summary>
        public static string SortImports(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            List<(string Text, string Newline)> lines = SplitLinesKeepNewline(content);
            if (lines.Count == 0)
            {
                return content;
            }

            var result = new List<string>(lines.Count);
            int i = 0;
            int brace = 0;
            while (i < lines.Count)
            {
                string trimmed = lines[i].Text.TrimStart();
                if (brace == 0 && IsUseLine(trimmed))
                {
                    int start = i;
                    int end = i;
                    bool sortable = true;
                    i++;
                    while (i < lines.Count)
                    {
                        string next = lines[i].Text;
                        string nextTrim = next.TrimStart();
                        if (string.IsNullOrWhiteSpace(next))
                        {
                            i++;
                            continue;
                        }

                        if (!IsUseLine(nextTrim))
                        {
                            break;
                        }

                        end = i;
                        i++;
                    }

                    var parsed = new List<(UseKind Kind, string SortKey, string Original, string Newline)>();
                    for (int j = start; j <= end; j++)
                    {
                        string text = lines[j].Text;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        if (!TryParseSimpleUse(text, out UseKind kind, out string sortKey))
                        {
                            sortable = false;
                            break;
                        }

                        parsed.Add((kind, sortKey, text.TrimEnd(), lines[j].Newline));
                    }

                    if (!sortable || parsed.Count == 0)
                    {
                        for (int j = start; j < i; j++)
                        {
                            result.Add(lines[j].Text + lines[j].Newline);
                        }

                        continue;
                    }

                    parsed.Sort((a, b) =>
                    {
                        int kind = a.Kind.CompareTo(b.Kind);
                        return kind != 0
                            ? kind
                            : string.Compare(a.SortKey, b.SortKey, StringComparison.OrdinalIgnoreCase);
                    });

                    string indent = LeadingWhitespace(parsed[0].Original);
                    UseKind? previous = null;
                    string lastNewline = parsed[^1].Newline;
                    for (int p = 0; p < parsed.Count; p++)
                    {
                        if (previous is UseKind prev && prev != parsed[p].Kind)
                        {
                            result.Add(lastNewline.Length > 0 ? lastNewline : Environment.NewLine);
                        }

                        string newline = p == parsed.Count - 1
                            ? parsed[p].Newline
                            : (parsed[p].Newline.Length > 0 ? parsed[p].Newline : Environment.NewLine);
                        result.Add(indent + parsed[p].Original.TrimStart() + newline);
                        previous = parsed[p].Kind;
                        lastNewline = newline;
                    }

                    continue;
                }

                result.Add(lines[i].Text + lines[i].Newline);
                foreach (char c in lines[i].Text)
                {
                    if (c == '{')
                    {
                        brace++;
                    }
                    else if (c == '}' && brace > 0)
                    {
                        brace--;
                    }
                }

                i++;
            }

            return string.Concat(result);
        }

        public static bool IsUseLine(string line)
        {
            string trimmed = line.TrimStart();
            return trimmed.StartsWith("use ", StringComparison.Ordinal)
                || trimmed.StartsWith("use\\", StringComparison.Ordinal);
        }

        public static IEnumerable<BaseSymbol> EnumerateAllSymbols(IBaseScope root)
        {
            var stack = new Stack<IBaseScope>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IBaseScope scope = stack.Pop();
                foreach (IBaseSymbol child in scope.GetAllChildSymbols())
                {
                    if (child is BaseSymbol symbol)
                    {
                        yield return symbol;
                    }
                }

                foreach (IBaseScope childScope in scope.GetAllChildScopes())
                {
                    stack.Push(childScope);
                }
            }
        }

        public static IReadOnlyList<BaseSymbol> FindImportableMatches(
            IBaseScope? globalScope,
            IBaseScope? fromScope,
            string unresolvedName,
            bool typesOnly)
        {
            if (globalScope is null || string.IsNullOrWhiteSpace(unresolvedName))
            {
                return [];
            }

            string simple = SimpleName(unresolvedName);
            if (string.IsNullOrEmpty(simple))
            {
                return [];
            }

            var matches = new List<BaseSymbol>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BaseSymbol symbol in EnumerateAllSymbols(globalScope))
            {
                if (!string.Equals(symbol.Name, simple, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsImportableSymbol(symbol, typesOnly))
                {
                    continue;
                }

                if (fromScope is not null && !NeedsImport(symbol, fromScope))
                {
                    continue;
                }

                string fqn = symbol.FullyQualifiedName.TrimStart('\\');
                if (string.IsNullOrEmpty(fqn) || !seen.Add(fqn))
                {
                    continue;
                }

                matches.Add(symbol);
            }

            matches.Sort((a, b) => string.Compare(
                a.FullyQualifiedName,
                b.FullyQualifiedName,
                StringComparison.OrdinalIgnoreCase));
            return matches;
        }

        public static string ImportKeyword(BaseSymbol symbol)
        {
            if (symbol is FunctionDeclarationSymbol or BuiltInFunctionSymbol)
            {
                return "use function ";
            }

            if (symbol is ConstantSymbol)
            {
                return "use const ";
            }

            return "use ";
        }

        public static string SimpleName(string name)
        {
            string trimmed = name.Trim().TrimStart('\\');
            int slash = trimmed.LastIndexOf('\\');
            return slash < 0 ? trimmed : trimmed[(slash + 1)..];
        }

        public static string NamespaceOf(string fqn)
        {
            int slash = fqn.LastIndexOf('\\');
            return slash < 0 ? string.Empty : fqn[..slash];
        }

        public static PhpImportDeclAst? FindImportDeclaration(SrcFileAst ast, string importedName, int line)
        {
            PhpImportDeclAst? byNameAndLine = null;
            PhpImportDeclAst? byName = null;
            PhpImportDeclAst? byLine = null;
            Walk(ast);
            return byNameAndLine ?? byName ?? byLine;

            void Walk(IBase2Ast node)
            {
                if (node is PhpImportDeclAst import)
                {
                    string fqn = (import.NamespaceName ?? string.Empty).TrimStart('\\');
                    string expected = importedName.TrimStart('\\');
                    bool nameMatch = string.Equals(fqn, expected, StringComparison.OrdinalIgnoreCase);
                    bool lineMatch = import.Line == line;
                    if (nameMatch && lineMatch)
                    {
                        byNameAndLine = import;
                    }
                    else if (nameMatch)
                    {
                        byName ??= import;
                    }
                    else if (lineMatch)
                    {
                        byLine ??= import;
                    }
                }

                foreach (IBase2Ast? child in node.AstChildren)
                {
                    if (child is not null)
                    {
                        Walk(child);
                    }
                }
            }
        }

        public static PhpImportDeclListAst? FindOwningImportList(SrcFileAst ast, PhpImportDeclAst import)
        {
            PhpImportDeclListAst? found = null;
            Walk(ast);
            return found;

            void Walk(IBase2Ast node)
            {
                if (found is not null)
                {
                    return;
                }

                if (node is PhpImportDeclListAst list)
                {
                    foreach (PhpImportDeclAst child in list.GetAllNotNull())
                    {
                        if (ReferenceEquals(child, import))
                        {
                            found = list;
                            return;
                        }
                    }
                }

                foreach (IBase2Ast? child in node.AstChildren)
                {
                    if (child is not null)
                    {
                        Walk(child);
                    }
                }
            }
        }

        /// <summary>
        /// Edit that deletes a single-item <c>use</c> statement including its trailing newline.
        /// Group-use lists are left untouched.
        /// </summary>
        public static TextEdit? TryCreateRemoveImportEdit(string content, SrcFileAst ast, string importedName, int line)
        {
            PhpImportDeclAst? import = FindImportDeclaration(ast, importedName, line);
            if (import is null)
            {
                return TryRemoveUseLineByText(content, importedName, line);
            }

            PhpImportDeclListAst? list = FindOwningImportList(ast, import);
            if (list is not null && list.GetAllNotNull().Count() > 1)
            {
                return null;
            }

            IBase2Ast target = list ?? (IBase2Ast)import;
            ProtocolRange span = PositionUtilities.ToLspRange(target);
            if (span.Start is null || span.End is null)
            {
                return TryRemoveUseLineByText(content, importedName, line);
            }

            int start = PositionUtilities.GetOffset(content, new Position { Line = span.Start.Line, Character = 0 });
            int end = PositionUtilities.GetOffset(content, span.End);
            end = ExtendThroughNewline(content, end);
            if (end < start || start < 0 || end > content.Length)
            {
                return null;
            }

            return new TextEdit
            {
                Range = new ProtocolRange
                {
                    Start = PositionUtilities.GetPosition(content, start),
                    End = PositionUtilities.GetPosition(content, end),
                },
                NewText = string.Empty,
            };
        }

        private static bool IsImportableSymbol(BaseSymbol symbol, bool typesOnly)
        {
            if (symbol is BuiltInTypeSymbol or BuiltInUtilityTypeSymbol or BuiltInFunctionSymbol or MagicConstantSymbol)
            {
                return false;
            }

            if (symbol is ObjectDeclarationSymbol { IsCompilerGenerated: true }
                or ObjectDeclarationSymbol { IsExtension: true }
                or AnonymousObjectDeclarationSymbol)
            {
                return false;
            }

            if (string.IsNullOrEmpty(symbol.Name) || symbol.Name.StartsWith("anonClass@", StringComparison.Ordinal))
            {
                return false;
            }

            bool isType = symbol is ObjectDeclarationSymbol or TypeAliasSymbol or ObjectTypeAliasSymbol;
            bool isFunction = symbol is FunctionDeclarationSymbol;
            bool isConst = symbol is ConstantSymbol;
            if (typesOnly)
            {
                return isType;
            }

            return isType || isFunction || isConst;
        }

        private static TextEdit? TryRemoveUseLineByText(string content, string importedName, int line)
        {
            List<(string Text, string Newline)> lines = SplitLinesKeepNewline(content);
            int index = Math.Clamp(line - 1, 0, Math.Max(0, lines.Count - 1));
            if (index >= lines.Count)
            {
                return null;
            }

            string expected = importedName.TrimStart('\\');
            int found = -1;
            for (int delta = 0; delta <= 1; delta++)
            {
                foreach (int candidate in new[] { index, index - delta, index + delta })
                {
                    if (candidate < 0 || candidate >= lines.Count)
                    {
                        continue;
                    }

                    if (IsUseLine(lines[candidate].Text)
                        && lines[candidate].Text.Contains(expected, StringComparison.OrdinalIgnoreCase)
                        && !lines[candidate].Text.Contains('{', StringComparison.Ordinal))
                    {
                        found = candidate;
                        break;
                    }
                }

                if (found >= 0)
                {
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            int start = 0;
            for (int i = 0; i < found; i++)
            {
                start += lines[i].Text.Length + lines[i].Newline.Length;
            }

            int length = lines[found].Text.Length + lines[found].Newline.Length;
            return new TextEdit
            {
                Range = new ProtocolRange
                {
                    Start = PositionUtilities.GetPosition(content, start),
                    End = PositionUtilities.GetPosition(content, start + length),
                },
                NewText = string.Empty,
            };
        }

        internal static bool TryParseSimpleUse(string line, out UseKind kind, out string sortKey)
        {
            kind = UseKind.Class;
            sortKey = string.Empty;
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("use ", StringComparison.Ordinal) || !trimmed.EndsWith(';'))
            {
                return false;
            }

            if (trimmed.Contains('{', StringComparison.Ordinal) || trimmed.Contains('(', StringComparison.Ordinal))
            {
                return false;
            }

            string rest = trimmed[4..^1].Trim();
            if (rest.StartsWith("function ", StringComparison.Ordinal))
            {
                kind = UseKind.Function;
                rest = rest["function ".Length..].Trim();
            }
            else if (rest.StartsWith("const ", StringComparison.Ordinal))
            {
                kind = UseKind.Const;
                rest = rest["const ".Length..].Trim();
            }
            else if (rest.StartsWith("extension ", StringComparison.Ordinal))
            {
                kind = UseKind.Extension;
                rest = rest["extension ".Length..].Trim();
            }

            int asIndex = rest.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            string fqn = asIndex >= 0 ? rest[..asIndex].Trim() : rest.Trim();
            sortKey = fqn.TrimStart('\\');
            return !string.IsNullOrEmpty(sortKey);
        }

        internal static List<(string Text, string Newline)> SplitLinesKeepNewline(string content)
        {
            var lines = new List<(string Text, string Newline)>();
            int i = 0;
            int length = content.Length;
            int lineStart = 0;
            while (i < length)
            {
                char c = content[i];
                if (c == '\r')
                {
                    string nl = (i + 1 < length && content[i + 1] == '\n') ? "\r\n" : "\r";
                    lines.Add((content[lineStart..i], nl));
                    i += nl.Length;
                    lineStart = i;
                    continue;
                }

                if (c == '\n')
                {
                    lines.Add((content[lineStart..i], "\n"));
                    i++;
                    lineStart = i;
                    continue;
                }

                i++;
            }

            lines.Add((content[lineStart..], string.Empty));
            return lines;
        }

        private static int LineEndOffset(string content, int index)
        {
            int length = content.Length;
            if (index < length && content[index] == '\r')
            {
                index++;
                if (index < length && content[index] == '\n')
                {
                    index++;
                }

                return index;
            }

            if (index < length && content[index] == '\n')
            {
                return index + 1;
            }

            return index;
        }

        private static int ExtendThroughNewline(string content, int end)
        {
            if (end < content.Length && content[end] == '\r')
            {
                end++;
            }

            if (end < content.Length && content[end] == '\n')
            {
                end++;
            }

            return end;
        }

        private static string LeadingWhitespace(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                i++;
            }

            return line[..i];
        }
    }
}
