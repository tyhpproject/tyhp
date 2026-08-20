namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Resolution;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using Tyhp.TyhpLang.Checker;
    using Tyhp.TyhpLang.Enum;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Builds LSP completion lists from binder scopes, inferred types, and cursor text.
    /// </summary>
    public static class CompletionEngine
    {
        private const string ResolveDataPrefix = "tyhp-doc:";
        private const int MaxInheritanceDepth = 64;

        private static readonly string[] Keywords =
        [
            "if", "else", "elseif", "for", "foreach", "while", "do", "switch", "match",
            "case", "default", "break", "continue", "try", "catch", "finally", "throw",
            "return", "function", "fn", "class", "interface", "trait", "enum", "struct",
            "namespace", "use", "const", "new", "clone", "yield", "async", "await",
            "public", "protected", "private", "static", "abstract", "final", "readonly",
            "extends", "implements", "instanceof", "as", "parent", "self", "static",
            "true", "false", "null", "echo", "isset", "empty", "unset", "include",
            "require", "include_once", "require_once",
        ];

        private static readonly (string Label, string Insert)[] Snippets =
        [
            ("if", "if ($1) {\n\t$0\n}"),
            ("foreach", "foreach ($1 as $2) {\n\t$0\n}"),
            ("for", "for ($1; $2; $3) {\n\t$0\n}"),
            ("while", "while ($1) {\n\t$0\n}"),
            ("try", "try {\n\t$0\n} catch (\\Throwable $e) {\n\t\n}"),
            ("function", "function $1($2): $3 {\n\t$0\n}"),
            ("class", "class $1 {\n\t$0\n}"),
            ("match", "match ($1) {\n\t$2 => $0,\n}"),
        ];

        /// <summary>
        /// Builds a completion list for <paramref name="position"/> in <paramref name="content"/>.
        /// </summary>
        public static CompletionList Complete(
            string content,
            Position position,
            CompletionContext? context,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? symbolTree,
            SymbolFinder finder,
            Func<IBase2Ast, ICheckedType?> getInferredType)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(position);
            ArgumentNullException.ThrowIfNull(finder);
            ArgumentNullException.ThrowIfNull(getInferredType);

            int offset = PositionUtilities.GetOffset(content, position);
            CompletionContextKind kind = Detect(content, offset, context, ast);
            ProtocolRange replaceRange = ComputeReplaceRange(content, offset, kind);
            var (antlrLine, antlrColumn) = PositionUtilities.FromLspPosition(position);

            IBaseScope? fromScope = ast is not null && globalScope is not null
                ? finder.FindScopeAtPosition(ast, globalScope, antlrLine, antlrColumn)
                : globalScope;
            NameResolver? resolver = CreateResolver(globalScope, symbolTree);
            var items = new List<CompletionItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            switch (kind)
            {
                case CompletionContextKind.Variable:
                    AddVariableItems(items, seen, fromScope, ast, antlrLine, antlrColumn, replaceRange);
                    break;
                case CompletionContextKind.InstanceMember:
                    AddInstanceMemberItems(
                        items,
                        seen,
                        content,
                        offset,
                        ast,
                        globalScope,
                        symbolTree,
                        finder,
                        resolver,
                        fromScope,
                        getInferredType,
                        replaceRange);
                    break;
                case CompletionContextKind.StaticMember:
                    AddStaticMemberItems(
                        items,
                        seen,
                        content,
                        offset,
                        ast,
                        globalScope,
                        finder,
                        resolver,
                        fromScope,
                        replaceRange);
                    break;
                case CompletionContextKind.Type:
                    AddTypeItems(items, seen, globalScope, fromScope, resolver, content, replaceRange, includeBuiltIns: true, autoImport: true);
                    break;
                case CompletionContextKind.NewClass:
                    AddTypeItems(items, seen, globalScope, fromScope, resolver, content, replaceRange, includeBuiltIns: false, autoImport: true, classLikeOnly: true);
                    break;
                case CompletionContextKind.Extends:
                    AddTypeItems(items, seen, globalScope, fromScope, resolver, content, replaceRange, includeBuiltIns: false, autoImport: true, classLikeOnly: true);
                    break;
                case CompletionContextKind.Implements:
                    AddTypeItems(items, seen, globalScope, fromScope, resolver, content, replaceRange, includeBuiltIns: false, autoImport: true, interfacesOnly: true);
                    break;
                case CompletionContextKind.TraitUse:
                    AddTypeItems(items, seen, globalScope, fromScope, resolver, content, replaceRange, includeBuiltIns: false, autoImport: true, traitsOnly: true);
                    break;
                case CompletionContextKind.UseImport:
                    AddNamespaceAndTypeNames(items, seen, globalScope, prefixPath: "", replaceRange, fileLevelUse: true);
                    break;
                case CompletionContextKind.Namespace:
                    AddNamespaceItems(items, seen, content, offset, globalScope, replaceRange);
                    break;
                default:
                    AddGlobalItems(items, seen, globalScope, fromScope, resolver, content, ast, antlrLine, antlrColumn, replaceRange);
                    break;
            }

            return new CompletionList
            {
                IsIncomplete = false,
                Items = [.. items],
            };
        }

        /// <summary>
        /// Fills documentation from the item's deferred data payload when the client resolves it.
        /// </summary>
        public static void Resolve(CompletionItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            string? payload = item.Data as string;
            if (string.IsNullOrEmpty(payload) || !payload.StartsWith(ResolveDataPrefix, StringComparison.Ordinal))
            {
                return;
            }

            string doc = payload[ResolveDataPrefix.Length..];
            if (string.IsNullOrEmpty(doc))
            {
                return;
            }

            item.Documentation = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = doc,
            };
        }

        /// <summary>
        /// Classifies the completion context from the source prefix and optional LSP trigger.
        /// </summary>
        public static CompletionContextKind Detect(
            string content,
            int offset,
            CompletionContext? context,
            SrcFileAst? ast)
        {
            ArgumentNullException.ThrowIfNull(content);
            offset = Math.Clamp(offset, 0, content.Length);
            string prefix = content[..offset];
            int identStart = FindPartialIdentifierStart(prefix, includeDollar: true);
            string beforeIdent = prefix[..identStart];
            string partial = prefix[identStart..];
            string trimmed = beforeIdent.TrimEnd();

            if (trimmed.EndsWith("?->", StringComparison.Ordinal) || trimmed.EndsWith("->", StringComparison.Ordinal))
            {
                return CompletionContextKind.InstanceMember;
            }

            if (trimmed.EndsWith("::", StringComparison.Ordinal))
            {
                return CompletionContextKind.StaticMember;
            }

            if (partial.StartsWith('$') || beforeIdent.EndsWith('$'))
            {
                return CompletionContextKind.Variable;
            }

            if (trimmed.EndsWith('\\'))
            {
                return CompletionContextKind.Namespace;
            }

            if (EndsWithKeyword(trimmed, "new"))
            {
                return CompletionContextKind.NewClass;
            }

            if (EndsWithKeyword(trimmed, "extends"))
            {
                return CompletionContextKind.Extends;
            }

            if (EndsWithKeyword(trimmed, "implements"))
            {
                return CompletionContextKind.Implements;
            }

            if (EndsWithKeyword(trimmed, "use"))
            {
                return IsInsideClassBody(content, offset) || IsInsideClassAst(ast, offset, content)
                    ? CompletionContextKind.TraitUse
                    : CompletionContextKind.UseImport;
            }

            if (LooksLikeTypeAnnotation(trimmed))
            {
                return CompletionContextKind.Type;
            }

            if (trimmed.EndsWith('<') && LooksLikeGenericArgument(trimmed))
            {
                return CompletionContextKind.Type;
            }

            if (context?.TriggerKind == CompletionTriggerKind.TriggerCharacter)
            {
                return context.TriggerCharacter switch
                {
                    "$" => CompletionContextKind.Variable,
                    ">" => trimmed.EndsWith("->", StringComparison.Ordinal) || trimmed.EndsWith("?->", StringComparison.Ordinal)
                        ? CompletionContextKind.InstanceMember
                        : CompletionContextKind.Global,
                    ":" => trimmed.EndsWith("::", StringComparison.Ordinal)
                        ? CompletionContextKind.StaticMember
                        : LooksLikeTypeAnnotation(trimmed)
                            ? CompletionContextKind.Type
                            : CompletionContextKind.Global,
                    "\\" => CompletionContextKind.Namespace,
                    "<" => CompletionContextKind.Type,
                    _ => CompletionContextKind.Global,
                };
            }

            return CompletionContextKind.Global;
        }

        private static void AddVariableItems(
            List<CompletionItem> items,
            HashSet<string> seen,
            IBaseScope? fromScope,
            SrcFileAst? ast,
            int line,
            int column,
            ProtocolRange replaceRange)
        {
            foreach (BaseSymbol symbol in EnumerateVariablesInScope(fromScope))
            {
                string label = EnsureDollar(symbol.Name);
                if (!seen.Add("var:" + label))
                {
                    continue;
                }

                items.Add(CreateSymbolItem(
                    symbol,
                    label,
                    label,
                    CompletionItemKind.Variable,
                    "0",
                    replaceRange));
            }

            foreach (string name in EnumerateLocalVariableNames(ast, line, column))
            {
                string label = EnsureDollar(name);
                if (!seen.Add("var:" + label))
                {
                    continue;
                }

                items.Add(CreatePlainItem(
                    label,
                    label,
                    CompletionItemKind.Variable,
                    "0",
                    replaceRange,
                    detail: "variable"));
            }

            if (IsInsideInstanceMethod(fromScope) && seen.Add("var:$this"))
            {
                items.Add(CreatePlainItem(
                    "$this",
                    "$this",
                    CompletionItemKind.Variable,
                    "0",
                    replaceRange,
                    detail: "this"));
            }
        }

        private static void AddInstanceMemberItems(
            List<CompletionItem> items,
            HashSet<string> seen,
            string content,
            int offset,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? symbolTree,
            SymbolFinder finder,
            NameResolver? resolver,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType,
            ProtocolRange replaceRange)
        {
            ObjectDeclarationSymbol? owner = ResolveInstanceReceiver(
                content,
                offset,
                ast,
                globalScope,
                symbolTree,
                finder,
                resolver,
                fromScope,
                getInferredType);
            if (owner is null)
            {
                return;
            }

            ObjectDeclarationSymbol? enclosing = FindEnclosingObject(fromScope);
            foreach (BaseSymbol member in EnumerateInstanceMembers(owner, resolver, fromScope))
            {
                if (!IsMemberAccessible(member, enclosing, resolver, fromScope))
                {
                    continue;
                }

                string label = InstanceMemberLabel(member);
                if (string.IsNullOrEmpty(label) || label.StartsWith("__", StringComparison.Ordinal) || !seen.Add("im:" + label))
                {
                    continue;
                }

                items.Add(CreateSymbolItem(
                    member,
                    label,
                    label,
                    ToMemberKind(member),
                    "0",
                    replaceRange));
            }

            AddExtensionMethods(items, seen, owner, symbolTree, resolver, fromScope, replaceRange);
        }

        private static void AddStaticMemberItems(
            List<CompletionItem> items,
            HashSet<string> seen,
            string content,
            int offset,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolFinder finder,
            NameResolver? resolver,
            IBaseScope? fromScope,
            ProtocolRange replaceRange)
        {
            ObjectDeclarationSymbol? owner = ResolveStaticReceiver(
                content,
                offset,
                ast,
                globalScope,
                finder,
                resolver,
                fromScope);
            if (owner is null)
            {
                return;
            }

            ObjectDeclarationSymbol? enclosing = FindEnclosingObject(fromScope);
            foreach (BaseSymbol member in EnumerateStaticMembers(owner, resolver, fromScope))
            {
                if (!IsMemberAccessible(member, enclosing, resolver, fromScope))
                {
                    continue;
                }

                string label = StaticMemberLabel(member);
                if (string.IsNullOrEmpty(label) || label.StartsWith("__", StringComparison.Ordinal) || !seen.Add("sm:" + label))
                {
                    continue;
                }

                items.Add(CreateSymbolItem(
                    member,
                    label,
                    label,
                    ToMemberKind(member),
                    "0",
                    replaceRange));
            }
        }

        private static void AddTypeItems(
            List<CompletionItem> items,
            HashSet<string> seen,
            GlobalScope? globalScope,
            IBaseScope? fromScope,
            NameResolver? resolver,
            string content,
            ProtocolRange replaceRange,
            bool includeBuiltIns,
            bool autoImport,
            bool classLikeOnly = false,
            bool interfacesOnly = false,
            bool traitsOnly = false)
        {
            if (globalScope is null)
            {
                return;
            }

            HashSet<string> imported = UseStatementEdits.CollectImportedNames(fromScope);
            string currentNs = UseStatementEdits.GetCurrentNamespace(fromScope);

            foreach (BaseSymbol symbol in UseStatementEdits.EnumerateAllSymbols(globalScope))
            {
                if (!IsTypeSymbol(symbol) || ShouldSkipTypeDeclaration(symbol))
                {
                    continue;
                }

                if (symbol is ObjectDeclarationSymbol obj)
                {
                    if (classLikeOnly
                        && obj.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Enum)
                        && !obj.IsStruct)
                    {
                        continue;
                    }

                    if (interfacesOnly && obj.ObjectKind != PhpTypeDeclType.Interface)
                    {
                        continue;
                    }

                    if (traitsOnly && obj.ObjectKind != PhpTypeDeclType.Trait)
                    {
                        continue;
                    }
                }
                else if (classLikeOnly || interfacesOnly || traitsOnly)
                {
                    continue;
                }

                bool inScope = IsTypeVisibleWithoutImport(symbol, currentNs, imported);
                string label = symbol.Name;
                string key = "type:" + symbol.FullyQualifiedName;
                if (!seen.Add(key))
                {
                    continue;
                }

                TextEdit[]? additional = null;
                string sortPrefix = inScope ? "1" : "3";
                string insert = symbol.Name;
                if (!inScope && autoImport)
                {
                    additional = TryCreateImportEdits(fromScope, symbol, content);
                    if (additional is null)
                    {
                        insert = QualifiedInsertName(symbol);
                    }
                }

                CompletionItem item = CreateSymbolItem(
                    symbol,
                    label,
                    insert,
                    ToTypeKind(symbol),
                    sortPrefix,
                    replaceRange,
                    additional);
                if (!inScope)
                {
                    string fqn = symbol.FullyQualifiedName.TrimStart('\\');
                    item.Detail = string.IsNullOrEmpty(item.Detail) ? fqn : item.Detail + " — " + fqn;
                    item.FilterText = symbol.Name + " " + fqn;
                }

                items.Add(item);
            }

            if (includeBuiltIns)
            {
                foreach (BaseSymbol symbol in EnumerateAllSymbols(globalScope))
                {
                    if (symbol is not (BuiltInTypeSymbol or BuiltInUtilityTypeSymbol)
                        || !seen.Add("builtin:" + symbol.Name))
                    {
                        continue;
                    }

                    items.Add(CreateSymbolItem(
                        symbol,
                        symbol.Name,
                        symbol.Name,
                        CompletionItemKind.Keyword,
                        "0",
                        replaceRange));
                }
            }

            _ = resolver;
        }

        private static void AddGlobalItems(
            List<CompletionItem> items,
            HashSet<string> seen,
            GlobalScope? globalScope,
            IBaseScope? fromScope,
            NameResolver? resolver,
            string content,
            SrcFileAst? ast,
            int line,
            int column,
            ProtocolRange replaceRange)
        {
            AddVariableItems(items, seen, fromScope, ast, line, column, replaceRange);
            AddTypeItems(items, seen, globalScope, fromScope, resolver, content, replaceRange, includeBuiltIns: true, autoImport: true);

            if (globalScope is not null)
            {
                foreach (BaseSymbol symbol in EnumerateAllSymbols(globalScope))
                {
                    if (symbol is FunctionDeclarationSymbol or BuiltInFunctionSymbol)
                    {
                        if (!seen.Add("fn:" + symbol.FullyQualifiedName))
                        {
                            continue;
                        }

                        string sort = symbol is BuiltInFunctionSymbol ? "4" : "1";
                        items.Add(CreateSymbolItem(
                            symbol,
                            symbol.Name,
                            symbol.Name,
                            CompletionItemKind.Function,
                            sort,
                            replaceRange,
                            TryCreateImportEdits(fromScope, symbol, content)));
                    }
                    else if (symbol is ConstantSymbol or MagicConstantSymbol)
                    {
                        if (!seen.Add("const:" + symbol.FullyQualifiedName))
                        {
                            continue;
                        }

                        items.Add(CreateSymbolItem(
                            symbol,
                            symbol.Name,
                            symbol.Name,
                            CompletionItemKind.Constant,
                            "2",
                            replaceRange));
                    }
                }
            }

            foreach (string keyword in Keywords)
            {
                if (!seen.Add("kw:" + keyword))
                {
                    continue;
                }

                items.Add(CreatePlainItem(
                    keyword,
                    keyword,
                    CompletionItemKind.Keyword,
                    "5",
                    replaceRange));
            }

            foreach ((string label, string insert) in Snippets)
            {
                if (!seen.Add("snip:" + label))
                {
                    continue;
                }

                var item = CreatePlainItem(
                    label,
                    insert,
                    CompletionItemKind.Snippet,
                    "6",
                    replaceRange,
                    detail: "snippet");
                item.InsertTextFormat = InsertTextFormat.Snippet;
                items.Add(item);
            }
        }

        private static void AddNamespaceItems(
            List<CompletionItem> items,
            HashSet<string> seen,
            string content,
            int offset,
            GlobalScope? globalScope,
            ProtocolRange replaceRange)
        {
            if (globalScope is null)
            {
                return;
            }

            string nsPrefix = ReadNamespacePrefix(content, offset);
            AddNamespaceAndTypeNames(items, seen, globalScope, nsPrefix, replaceRange, fileLevelUse: false);
        }

        private static void AddNamespaceAndTypeNames(
            List<CompletionItem> items,
            HashSet<string> seen,
            GlobalScope? globalScope,
            string prefixPath,
            ProtocolRange replaceRange,
            bool fileLevelUse)
        {
            if (globalScope is null)
            {
                return;
            }

            string normalizedPrefix = prefixPath.Trim('\\');
            var childNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IBaseScope child in ((IBaseScope)globalScope).GetAllChildScopes())
            {
                if (child is not NamespaceScope nsScope || nsScope.DeclarationSymbol is not NamespaceSymbol ns)
                {
                    continue;
                }

                string full = ns.Name.Trim('\\');
                string? nextSegment = NextNamespaceSegment(full, normalizedPrefix);
                if (nextSegment is not null && childNamespaces.Add(nextSegment) && seen.Add("ns:" + nextSegment))
                {
                    items.Add(CreatePlainItem(
                        nextSegment,
                        nextSegment,
                        CompletionItemKind.Module,
                        "0",
                        replaceRange,
                        detail: "namespace"));
                }

                if (!NamespaceMatchesPrefix(full, normalizedPrefix))
                {
                    continue;
                }

                foreach (IBaseScope block in ((IBaseScope)nsScope).GetAllChildScopes())
                {
                    foreach (IBaseSymbol symbol in block.GetAllChildSymbols())
                    {
                        if (symbol is not BaseSymbol typed || !IsTypeSymbol(typed) || ShouldSkipTypeDeclaration(typed))
                        {
                            continue;
                        }

                        if (!seen.Add("nstype:" + typed.FullyQualifiedName))
                        {
                            continue;
                        }

                        string insert = fileLevelUse ? typed.FullyQualifiedName.TrimStart('\\') : typed.Name;
                        items.Add(CreateSymbolItem(
                            typed,
                            typed.Name,
                            insert,
                            ToTypeKind(typed),
                            "1",
                            replaceRange));
                    }
                }
            }
        }

        private static void AddExtensionMethods(
            List<CompletionItem> items,
            HashSet<string> seen,
            ObjectDeclarationSymbol owner,
            SymbolTree? symbolTree,
            NameResolver? resolver,
            IBaseScope? fromScope,
            ProtocolRange replaceRange)
        {
            if (symbolTree is null || resolver is null)
            {
                return;
            }

            foreach (List<ObjectMethodSymbol> candidates in symbolTree.ExtensionMethodIndex.Values)
            {
                foreach (ObjectMethodSymbol method in candidates)
                {
                    if (resolver.ResolveExtensionMethod(method.Name, owner) is not ObjectMethodSymbol match)
                    {
                        continue;
                    }

                    if (!seen.Add("im:" + match.Name))
                    {
                        continue;
                    }

                    items.Add(CreateSymbolItem(
                        match,
                        match.Name,
                        match.Name,
                        CompletionItemKind.Method,
                        "1",
                        replaceRange));
                }
            }

            _ = fromScope;
        }

        private static ObjectDeclarationSymbol? ResolveInstanceReceiver(
            string content,
            int offset,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? symbolTree,
            SymbolFinder finder,
            NameResolver? resolver,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType)
        {
            int arrow = LastIndexOfArrow(content, offset);
            if (arrow < 0)
            {
                return null;
            }

            int receiverEnd = SkipWhitespaceBack(content, arrow);
            if (receiverEnd < 0)
            {
                return null;
            }

            var position = PositionUtilities.GetPosition(content, receiverEnd);
            var (line, column) = PositionUtilities.FromLspPosition(position);
            if (ast is not null)
            {
                SymbolLookupResult? lookup = finder.LookupAtPosition(ast, globalScope, symbolTree, line, column);
                if (lookup is not null)
                {
                    ObjectDeclarationSymbol? fromSymbol = ObjectFromSymbol(lookup.Symbol, resolver, fromScope);
                    if (fromSymbol is not null)
                    {
                        return fromSymbol;
                    }

                    ObjectDeclarationSymbol? fromInferred = ObjectFromCheckedType(getInferredType(lookup.InferredTypeNode));
                    if (fromInferred is not null)
                    {
                        return fromInferred;
                    }

                    if (lookup.DeclaringNode is IBase2Ast declaring)
                    {
                        fromInferred = ObjectFromCheckedType(getInferredType(declaring));
                        if (fromInferred is not null)
                        {
                            return fromInferred;
                        }
                    }
                }

                IBase2Ast? node = finder.FindNodeAtPosition(ast, line, column);
                if (node is not null)
                {
                    ObjectDeclarationSymbol? fromNode = ObjectFromCheckedType(getInferredType(node));
                    if (fromNode is not null)
                    {
                        return fromNode;
                    }
                }

                // Position-based lookup can land on an unrelated sibling AST node near
                // incomplete syntax (e.g. a dangling `->` with no member yet stops the
                // enclosing statement/block from recording a real end position, so
                // CollectAtPosition falls through to whatever earlier sibling — such as an
                // empty parameter list — has no resolvable end either). For a plain
                // `$name->` receiver, resolve the untyped local directly by name within its
                // enclosing callable instead of depending on that exact position resolving.
                string? variableName = ReadVariableNameBack(content, receiverEnd);
                if (variableName is not null)
                {
                    BaseSymbol? local = finder.FindLocalVariableByName(
                        ast,
                        variableName,
                        line,
                        column,
                        out IBase2Ast? inferredTypeNode);
                    ObjectDeclarationSymbol? fromLocal = ObjectFromSymbol(local, resolver, fromScope);
                    if (fromLocal is not null)
                    {
                        return fromLocal;
                    }

                    if (inferredTypeNode is not null)
                    {
                        ObjectDeclarationSymbol? fromLocalInferred = ObjectFromCheckedType(getInferredType(inferredTypeNode));
                        if (fromLocalInferred is not null)
                        {
                            return fromLocalInferred;
                        }
                    }

                    // A statement ending in a dangling `->`/`?->` with nothing after it (the
                    // common mid-typing case) can make the parser drop the enclosing
                    // function/method body from the AST entirely rather than partially
                    // recovering it, so even the name-based walk above has nothing to search.
                    // As a last resort, read the class name straight out of the most recent
                    // `$name = new ClassName(...)` assignment textually preceding the cursor
                    // and resolve it by name — the same "prefix-text heuristic" style already
                    // used for context detection and for `self`/`static`/`parent` receivers.
                    if (resolver is not null && fromScope is not null)
                    {
                        string? className = FindMostRecentNewAssignmentClassName(content, arrow, variableName);
                        if (className is not null
                            && SymbolFinder.ResolveTypeByName(className, fromScope, resolver) is ObjectDeclarationSymbol fromText)
                        {
                            return fromText;
                        }
                    }
                }
            }

            return FindEnclosingObject(fromScope);
        }

        private static ObjectDeclarationSymbol? ResolveStaticReceiver(
            string content,
            int offset,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolFinder finder,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            int colons = content.LastIndexOf("::", Math.Min(offset, content.Length) - 1, StringComparison.Ordinal);
            if (colons < 0)
            {
                return null;
            }

            int nameEnd = SkipWhitespaceBack(content, colons);
            if (nameEnd < 0)
            {
                return null;
            }

            string name = ReadIdentifierBack(content, nameEnd);
            if (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase))
            {
                if (resolver is not null && fromScope is not null)
                {
                    return resolver.ResolveSelfStaticParent(name, fromScope) as ObjectDeclarationSymbol
                        ?? FindEnclosingObject(fromScope);
                }

                return FindEnclosingObject(fromScope);
            }

            if (ast is not null)
            {
                var position = PositionUtilities.GetPosition(content, nameEnd);
                var (line, column) = PositionUtilities.FromLspPosition(position);
                BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, globalScope, line, column);
                if (symbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }
            }

            if (resolver is not null && fromScope is not null && !string.IsNullOrEmpty(name))
            {
                if (resolver.ResolveSymbol(name, fromScope) is ObjectDeclarationSymbol resolved)
                {
                    return resolved;
                }

                if (resolver.ResolveRelativeName([name], fromScope) is ObjectDeclarationSymbol relative)
                {
                    return relative;
                }
            }

            return FindEnclosingObject(fromScope);
        }

        private static IEnumerable<BaseSymbol> EnumerateInstanceMembers(
            ObjectDeclarationSymbol owner,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            foreach (ObjectDeclarationSymbol type in WalkTypeHierarchy(owner, resolver, fromScope, visited))
            {
                foreach (IBaseSymbol member in type.EnumerateMembersAndConstants())
                {
                    if (member is ObjectMethodSymbol method && (method.IsStatic || method.SymbolType == SymbolType.ObjectConstructor))
                    {
                        continue;
                    }

                    if (member is ObjectPropertySymbol property
                        && (property.SymbolType == SymbolType.StaticObjectProperty
                            || property.Visibility.HasFlag(MemberModifier.Static)))
                    {
                        continue;
                    }

                    if (member is ObjectConstantSymbol)
                    {
                        continue;
                    }

                    if (member is BaseSymbol symbol)
                    {
                        yield return symbol;
                    }
                }
            }
        }

        private static IEnumerable<BaseSymbol> EnumerateStaticMembers(
            ObjectDeclarationSymbol owner,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            foreach (ObjectDeclarationSymbol type in WalkTypeHierarchy(owner, resolver, fromScope, visited))
            {
                foreach (IBaseSymbol member in type.EnumerateMembersAndConstants())
                {
                    if (member is ObjectMethodSymbol method && !method.IsStatic)
                    {
                        continue;
                    }

                    if (member is ObjectPropertySymbol property
                        && property.SymbolType != SymbolType.StaticObjectProperty
                        && !property.Visibility.HasFlag(MemberModifier.Static))
                    {
                        continue;
                    }

                    if (member is BaseSymbol symbol)
                    {
                        yield return symbol;
                    }
                }
            }
        }

        private static IEnumerable<ObjectDeclarationSymbol> WalkTypeHierarchy(
            ObjectDeclarationSymbol owner,
            NameResolver? resolver,
            IBaseScope? fromScope,
            HashSet<ObjectDeclarationSymbol> visited,
            int depth = 0)
        {
            if (depth > MaxInheritanceDepth || !visited.Add(owner))
            {
                yield break;
            }

            yield return owner;

            ObjectDeclarationSymbol? parent = ResolveParent(owner, resolver, fromScope);
            if (parent is not null)
            {
                foreach (ObjectDeclarationSymbol ancestor in WalkTypeHierarchy(parent, resolver, fromScope, visited, depth + 1))
                {
                    yield return ancestor;
                }
            }

            foreach (ITypeExpression implemented in owner.ImplementsTypes)
            {
                if (ResolveTypeToObject(implemented, resolver, owner.ContainingScope ?? fromScope) is ObjectDeclarationSymbol iface)
                {
                    foreach (ObjectDeclarationSymbol ancestor in WalkTypeHierarchy(iface, resolver, fromScope, visited, depth + 1))
                    {
                        yield return ancestor;
                    }
                }
            }
        }

        private static ObjectDeclarationSymbol? ResolveParent(
            ObjectDeclarationSymbol owner,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            if (owner.ExtendsType is not null)
            {
                ObjectDeclarationSymbol? fromType = ResolveTypeToObject(owner.ExtendsType, resolver, owner.ContainingScope ?? fromScope);
                if (fromType is not null)
                {
                    return fromType;
                }
            }

            if (owner.DeclaringAstNode is PhpObjectTypeDeclAst { Extends: { } className } && resolver is not null)
            {
                string name = className switch
                {
                    PhpNameAst named => FirstNonEmpty(named.ValueString, named.Identifier),
                    TokenValueAst token => token.ValueString ?? string.Empty,
                    _ => className.Identifier ?? string.Empty,
                };
                if (!string.IsNullOrEmpty(name) && (owner.ContainingScope ?? fromScope) is IBaseScope scope)
                {
                    if (resolver.ResolveSymbol(name, scope) is ObjectDeclarationSymbol obj)
                    {
                        return obj;
                    }

                    if (resolver.ResolveRelativeName([name.TrimStart('\\')], scope) is ObjectDeclarationSymbol relative)
                    {
                        return relative;
                    }
                }
            }

            return null;
        }

        private static ObjectDeclarationSymbol? ResolveTypeToObject(
            ITypeExpression type,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            if (resolver is null || fromScope is null)
            {
                return type.BoundSymbol as ObjectDeclarationSymbol;
            }

            return resolver.ResolveType(type, fromScope) as ObjectDeclarationSymbol
                ?? type.BoundSymbol as ObjectDeclarationSymbol;
        }

        private static ObjectDeclarationSymbol? ObjectFromSymbol(
            BaseSymbol? symbol,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            if (symbol is ObjectDeclarationSymbol obj)
            {
                return obj;
            }

            if (symbol is VariableSymbol variable && variable.DeclaredType is ITypeExpression declared)
            {
                return ResolveTypeToObject(declared, resolver, fromScope);
            }

            return null;
        }

        private static ObjectDeclarationSymbol? ObjectFromCheckedType(ICheckedType? type)
        {
            while (type is not null)
            {
                switch (type)
                {
                    case NullableCheckedType nullable:
                        type = nullable.InnerType;
                        continue;
                    case StaticCheckedType staticType:
                        type = staticType.DeclaringType;
                        continue;
                    case GenericCheckedType generic:
                        type = generic.BaseType;
                        continue;
                    case SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj }:
                        return obj;
                    case UnionCheckedType union:
                        foreach (ICheckedType member in union.Members)
                        {
                            ObjectDeclarationSymbol? found = ObjectFromCheckedType(member);
                            if (found is not null)
                            {
                                return found;
                            }
                        }

                        return null;
                    default:
                        return null;
                }
            }

            return null;
        }

        private static IEnumerable<BaseSymbol> EnumerateVariablesInScope(IBaseScope? fromScope)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                foreach (IBaseSymbol child in scope.GetAllChildSymbols())
                {
                    if (child is VariableSymbol or SuperGlobalSymbol)
                    {
                        var symbol = (BaseSymbol)child;
                        if (seen.Add(StripDollar(symbol.Name)))
                        {
                            yield return symbol;
                        }
                    }
                }

                if (scope is FileScope or GlobalScope or NamespaceScope)
                {
                    yield break;
                }

                scope = scope.ParentScope;
            }
        }

        private static IEnumerable<string> EnumerateLocalVariableNames(
            SrcFileAst? ast,
            int line,
            int column)
        {
            if (ast is null)
            {
                yield break;
            }

            IBase2Ast? enclosing = FindEnclosingCallable(ast, line, column);
            if (enclosing is null)
            {
                yield break;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectVariableNames(enclosing, names);
            foreach (string name in names)
            {
                yield return name;
            }
        }

        private static IBase2Ast? FindEnclosingCallable(SrcFileAst ast, int line, int column)
        {
            IBase2Ast? containing = null;
            IBase2Ast? lastStartingBefore = null;
            Walk(ast);
            return containing ?? lastStartingBefore;

            void Walk(IBase2Ast node)
            {
                if (node is PhpFunctionDeclAst or PhpMethodDeclAst)
                {
                    if (SymbolFinder.ContainsPosition(node, line, column))
                    {
                        containing = node;
                    }

                    if (node.Line >= 1
                        && (node.Line < line || (node.Line == line && node.Column <= column)))
                    {
                        if (lastStartingBefore is null
                            || node.Line > lastStartingBefore.Line
                            || (node.Line == lastStartingBefore.Line && node.Column > lastStartingBefore.Column))
                        {
                            lastStartingBefore = node;
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

        private static void CollectVariableNames(IBase2Ast node, HashSet<string> names)
        {
            if (node is PhpVariableAst variable)
            {
                string raw = variable.VariableToken?.ValueString
                    ?? variable.Identifier
                    ?? variable.ValueString
                    ?? string.Empty;
                if (variable.VariableExpression is TokenValueAst token && string.IsNullOrEmpty(raw))
                {
                    raw = token.ValueString ?? string.Empty;
                }

                string bare = StripDollar(raw);
                if (!string.IsNullOrEmpty(bare)
                    && !string.Equals(bare, "this", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(bare, "GLOBALS", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(bare);
                }
            }

            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    CollectVariableNames(child, names);
                }
            }
        }

        private static IEnumerable<BaseSymbol> EnumerateAllSymbols(IBaseScope root)
            => UseStatementEdits.EnumerateAllSymbols(root);

        private static CompletionItem CreateSymbolItem(
            BaseSymbol symbol,
            string label,
            string insertText,
            CompletionItemKind kind,
            string sortPrefix,
            ProtocolRange replaceRange,
            TextEdit[]? additionalEdits = null)
        {
            string detail = SymbolFormatter.FormatSignature(symbol);
            string? doc = SymbolFormatter.FormatDocumentation(symbol);
            var item = new CompletionItem
            {
                Label = label,
                Kind = kind,
                Detail = detail,
                InsertText = insertText,
                FilterText = label,
                SortText = sortPrefix + "_" + label,
                TextEdit = new TextEdit
                {
                    Range = replaceRange,
                    NewText = insertText,
                },
            };

            bool deprecated = IsDeprecatedSymbol(symbol);
            if (deprecated)
            {
                item.Detail = string.IsNullOrEmpty(detail) ? "(deprecated)" : detail + " (deprecated)";
            }

            if (!string.IsNullOrEmpty(doc))
            {
                if (deprecated)
                {
                    doc += "\n\n**Deprecated**";
                }

                item.Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = doc,
                };
                item.Data = ResolveDataPrefix + doc;
            }
            else if (deprecated)
            {
                item.Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = "**Deprecated**",
                };
                item.Data = ResolveDataPrefix + "**Deprecated**";
            }

            if (additionalEdits is { Length: > 0 })
            {
                item.AdditionalTextEdits = additionalEdits;
            }

            return item;
        }

        private static CompletionItem CreatePlainItem(
            string label,
            string insertText,
            CompletionItemKind kind,
            string sortPrefix,
            ProtocolRange replaceRange,
            string? detail = null)
        {
            return new CompletionItem
            {
                Label = label,
                Kind = kind,
                Detail = detail,
                InsertText = insertText,
                FilterText = label,
                SortText = sortPrefix + "_" + label,
                TextEdit = new TextEdit
                {
                    Range = replaceRange,
                    NewText = insertText,
                },
            };
        }

        private static TextEdit[]? TryCreateImportEdits(IBaseScope? fromScope, BaseSymbol symbol, string content)
            => UseStatementEdits.TryCreateImportEdits(fromScope, symbol, content);

        private static bool IsTypeVisibleWithoutImport(BaseSymbol symbol, string currentNs, HashSet<string> imported)
            => UseStatementEdits.IsVisibleWithoutImport(symbol, currentNs, imported);

        private static ProtocolRange ComputeReplaceRange(string content, int offset, CompletionContextKind kind)
        {
            offset = Math.Clamp(offset, 0, content.Length);
            bool includeDollar = kind == CompletionContextKind.Variable;
            int start = FindPartialIdentifierStart(content[..offset], includeDollar);
            Position startPos = PositionUtilities.GetPosition(content, start);
            Position endPos = PositionUtilities.GetPosition(content, offset);
            return new ProtocolRange { Start = startPos, End = endPos };
        }

        private static int FindPartialIdentifierStart(string prefix, bool includeDollar)
        {
            int i = prefix.Length;
            while (i > 0 && IsIdentifierChar(prefix[i - 1]))
            {
                i--;
            }

            if (includeDollar && i > 0 && prefix[i - 1] == '$')
            {
                i--;
            }

            return i;
        }

        private static bool LooksLikeTypeAnnotation(string trimmed)
        {
            if (trimmed.EndsWith("):", StringComparison.Ordinal) || trimmed.EndsWith("): ", StringComparison.Ordinal))
            {
                return true;
            }

            if (trimmed.Length == 0 || trimmed[^1] != ':')
            {
                return false;
            }

            if (trimmed.EndsWith("::", StringComparison.Ordinal))
            {
                return false;
            }

            int lastSemi = trimmed.LastIndexOfAny([';', '{', '}']);
            string stmt = lastSemi >= 0 ? trimmed[(lastSemi + 1)..] : trimmed;
            if (stmt.Contains('?', StringComparison.Ordinal))
            {
                return false;
            }

            return stmt.Contains("function ", StringComparison.Ordinal)
                || stmt.Contains("function(", StringComparison.Ordinal)
                || EndsWithKeyword(stmt.TrimEnd(':').TrimEnd(), "function");
        }

        private static bool LooksLikeGenericArgument(string trimmed)
        {
            int lt = trimmed.LastIndexOf('<');
            if (lt <= 0)
            {
                return false;
            }

            return IsIdentifierChar(trimmed[lt - 1]);
        }

        private static bool EndsWithKeyword(string text, string keyword)
        {
            if (text.Length < keyword.Length)
            {
                return false;
            }

            if (!text.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (text.Length == keyword.Length)
            {
                return true;
            }

            char before = text[text.Length - keyword.Length - 1];
            return !IsIdentifierChar(before);
        }

        private static bool IsInsideClassBody(string content, int offset)
        {
            offset = Math.Clamp(offset, 0, content.Length);
            int lastClass = LastIndexOfKeyword(content, offset, "class");
            int lastInterface = LastIndexOfKeyword(content, offset, "interface");
            int lastTrait = LastIndexOfKeyword(content, offset, "trait");
            int lastEnum = LastIndexOfKeyword(content, offset, "enum");
            int lastStruct = LastIndexOfKeyword(content, offset, "struct");
            int start = Math.Max(lastClass, Math.Max(lastInterface, Math.Max(lastTrait, Math.Max(lastEnum, lastStruct))));
            if (start < 0)
            {
                return false;
            }

            int brace = 0;
            for (int i = start; i < offset; i++)
            {
                if (content[i] == '{')
                {
                    brace++;
                }
                else if (content[i] == '}' && brace > 0)
                {
                    brace--;
                }
            }

            return brace > 0;
        }

        private static bool IsInsideClassAst(SrcFileAst? ast, int offset, string content)
        {
            if (ast is null)
            {
                return false;
            }

            Position position = PositionUtilities.GetPosition(content, offset);
            var (line, column) = PositionUtilities.FromLspPosition(position);
            return ContainsClassAt(ast, line, column);
        }

        private static bool ContainsClassAt(IBase2Ast node, int line, int column)
        {
            if (node is PhpObjectTypeDeclAst && SymbolFinder.ContainsPosition(node, line, column))
            {
                return true;
            }

            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null && ContainsClassAt(child, line, column))
                {
                    return true;
                }
            }

            return false;
        }

        private static int LastIndexOfKeyword(string content, int offset, string keyword)
        {
            int from = Math.Min(offset, content.Length);
            int index = content.LastIndexOf(keyword, from, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                bool startOk = index == 0 || !IsIdentifierChar(content[index - 1]);
                int end = index + keyword.Length;
                bool endOk = end >= content.Length || !IsIdentifierChar(content[end]);
                if (startOk && endOk)
                {
                    return index;
                }

                if (index == 0)
                {
                    return -1;
                }

                index = content.LastIndexOf(keyword, index - 1, StringComparison.OrdinalIgnoreCase);
            }

            return -1;
        }

        /// <summary>
        /// Returns the start index of the closest <c>-&gt;</c> or <c>?-&gt;</c> operator at or
        /// before <paramref name="offset"/>. When a matched <c>-&gt;</c> is the tail of a
        /// <c>?-&gt;</c> (i.e. they refer to the same operator), the <c>?-&gt;</c> start is
        /// returned so callers skip back past the whole operator — not just <c>-&gt;</c>,
        /// which would otherwise land one character short, inside the null-safe marker.
        /// </summary>
        private static int LastIndexOfArrow(string content, int offset)
        {
            int from = Math.Min(offset, content.Length);
            int nullSafe = content.LastIndexOf("?->", from, StringComparison.Ordinal);
            int arrow = content.LastIndexOf("->", from, StringComparison.Ordinal);
            int nullSafeEnd = nullSafe >= 0 ? nullSafe + 3 : -1;
            int arrowEnd = arrow >= 0 ? arrow + 2 : -1;
            return nullSafeEnd >= arrowEnd ? nullSafe : arrow;
        }

        private static int SkipWhitespaceBack(string content, int index)
        {
            int i = index - 1;
            while (i >= 0 && char.IsWhiteSpace(content[i]))
            {
                i--;
            }

            return i;
        }

        /// <summary>
        /// Reads a bare <c>$name</c> variable ending at <paramref name="endInclusive"/>
        /// (inclusive), returning the name without the <c>$</c> sigil, or <c>null</c> when
        /// the text there is not a plain variable reference (e.g. a method call result or
        /// <c>$this</c>/property chain).
        /// </summary>
        private static string? ReadVariableNameBack(string content, int endInclusive)
        {
            int i = endInclusive;
            while (i >= 0 && IsIdentifierChar(content[i]))
            {
                i--;
            }

            if (i < 0 || content[i] != '$' || i == endInclusive)
            {
                return null;
            }

            string name = content[(i + 1)..(endInclusive + 1)];
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Textually finds the class name from the last <c>$name = new ClassName(...)</c> (or
        /// <c>$name = new ClassName;</c>) assignment to <paramref name="variableName"/> before
        /// <paramref name="beforeOffset"/>. Used only as a last-resort receiver-type fallback
        /// when AST/scope-based resolution has nothing to work with.
        /// </summary>
        private static string? FindMostRecentNewAssignmentClassName(string content, int beforeOffset, string variableName)
        {
            string pattern = @"\$" + System.Text.RegularExpressions.Regex.Escape(variableName)
                + @"\s*=\s*new\s+([A-Za-z_\\][A-Za-z0-9_\\]*)";
            string? best = null;
            int bestIndex = -1;
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(content, pattern))
            {
                if (match.Index >= beforeOffset)
                {
                    continue;
                }

                if (match.Index > bestIndex)
                {
                    bestIndex = match.Index;
                    best = match.Groups[1].Value;
                }
            }

            return best;
        }

        private static string ReadIdentifierBack(string content, int endInclusive)
        {
            int i = endInclusive;
            while (i >= 0 && (IsIdentifierChar(content[i]) || content[i] == '\\'))
            {
                i--;
            }

            return content[(i + 1)..(endInclusive + 1)];
        }

        private static string ReadNamespacePrefix(string content, int offset)
        {
            int i = Math.Clamp(offset, 0, content.Length);
            while (i > 0 && IsIdentifierChar(content[i - 1]))
            {
                i--;
            }

            int end = i;
            while (i > 0 && (IsIdentifierChar(content[i - 1]) || content[i - 1] == '\\'))
            {
                i--;
            }

            string raw = content[i..end].Trim('\\');
            return raw;
        }

        private static string? NextNamespaceSegment(string fullName, string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                int slash = fullName.IndexOf('\\');
                return slash < 0 ? fullName : fullName[..slash];
            }

            if (!fullName.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string rest = fullName[(prefix.Length + 1)..];
            int next = rest.IndexOf('\\');
            return next < 0 ? rest : rest[..next];
        }

        private static bool NamespaceMatchesPrefix(string fullName, string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return false;
            }

            return string.Equals(fullName, prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Visibility check for member completion. Private/protected access must be judged
        /// against the class that actually <em>declares</em> the member — not the receiver's
        /// static type — otherwise a private member simply inherited into the receiver's class
        /// (same as <c>fromClass</c>) would incorrectly appear accessible via <c>$this-&gt;</c>,
        /// even though PHP/Tyhp private members are never inherited-accessible.
        /// </summary>
        private static bool IsMemberAccessible(
            BaseSymbol member,
            ObjectDeclarationSymbol? fromClass,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            ObjectDeclarationSymbol? declaringClass = FindDeclaringObject(member);

            if (member.Visibility.HasFlag(MemberModifier.Private))
            {
                return fromClass is not null && declaringClass is not null && SameType(fromClass, declaringClass);
            }

            if (member.Visibility.HasFlag(MemberModifier.Protected))
            {
                if (fromClass is null || declaringClass is null)
                {
                    return false;
                }

                if (SameType(fromClass, declaringClass))
                {
                    return true;
                }

                var visited = new HashSet<ObjectDeclarationSymbol>();
                foreach (ObjectDeclarationSymbol ancestor in WalkTypeHierarchy(fromClass, resolver, fromScope, visited))
                {
                    if (SameType(ancestor, declaringClass))
                    {
                        return true;
                    }
                }

                return false;
            }

            return true;
        }

        /// <summary>The class/interface/trait whose own scope declares <paramref name="member"/>.</summary>
        private static ObjectDeclarationSymbol? FindDeclaringObject(BaseSymbol member)
        {
            IBaseScope? scope = member.ContainingScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private static bool SameType(ObjectDeclarationSymbol left, ObjectDeclarationSymbol right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return string.Equals(
                left.FullyQualifiedName,
                right.FullyQualifiedName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsideInstanceMethod(IBaseScope? fromScope)
        {
            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectMethodSymbol method)
                {
                    return !method.IsStatic;
                }

                if (scope.DeclarationSymbol is ObjectDeclarationSymbol)
                {
                    return false;
                }

                scope = scope.ParentScope;
            }

            return false;
        }

        private static ObjectDeclarationSymbol? FindEnclosingObject(IBaseScope? fromScope)
        {
            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private static NameResolver? CreateResolver(GlobalScope? scope, SymbolTree? tree)
        {
            var diagnostics = new DiagnosticBag();
            if (tree is not null)
            {
                return tree.CreateNameResolver(diagnostics);
            }

            if (scope is not null)
            {
                return new NameResolver(scope, diagnostics);
            }

            return null;
        }

        private static bool IsDeprecatedSymbol(BaseSymbol symbol)
        {
            if (symbol.IsDeprecated || symbol.IsObsolete)
            {
                return true;
            }

            return !string.IsNullOrEmpty(symbol.DocComment)
                && symbol.DocComment.Contains("@deprecated", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTypeSymbol(BaseSymbol symbol)
            => symbol is ObjectDeclarationSymbol
            or TypeAliasSymbol
            or ObjectTypeAliasSymbol
            or GenericTypeParameterSymbol;

        private static bool ShouldSkipTypeDeclaration(BaseSymbol symbol)
        {
            if (symbol is ObjectDeclarationSymbol { IsCompilerGenerated: true } or ObjectDeclarationSymbol { IsExtension: true })
            {
                return true;
            }

            if (symbol is AnonymousObjectDeclarationSymbol)
            {
                return true;
            }

            return string.IsNullOrEmpty(symbol.Name) || symbol.Name.StartsWith("anonClass@", StringComparison.Ordinal);
        }

        private static CompletionItemKind ToTypeKind(BaseSymbol symbol)
        {
            if (symbol is ObjectDeclarationSymbol obj)
            {
                if (obj.IsStruct)
                {
                    return CompletionItemKind.Struct;
                }

                return obj.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => CompletionItemKind.Interface,
                    PhpTypeDeclType.Enum => CompletionItemKind.Enum,
                    PhpTypeDeclType.Trait => CompletionItemKind.Class,
                    _ => CompletionItemKind.Class,
                };
            }

            if (symbol is GenericTypeParameterSymbol)
            {
                return CompletionItemKind.TypeParameter;
            }

            return CompletionItemKind.Class;
        }

        private static CompletionItemKind ToMemberKind(BaseSymbol symbol)
        {
            return symbol switch
            {
                ObjectMethodSymbol method when method.SymbolType == SymbolType.ObjectConstructor => CompletionItemKind.Constructor,
                ObjectMethodSymbol => CompletionItemKind.Method,
                ObjectPropertySymbol => CompletionItemKind.Property,
                ObjectConstantSymbol constant when constant.IsEnumCase => CompletionItemKind.EnumMember,
                ObjectConstantSymbol or ConstantSymbol => CompletionItemKind.Constant,
                _ => CompletionItemKind.Field,
            };
        }

        private static string InstanceMemberLabel(BaseSymbol member)
        {
            if (member is ObjectPropertySymbol)
            {
                return StripDollar(member.Name);
            }

            return member.Name.StartsWith('$') ? member.Name[1..] : member.Name;
        }

        private static string StaticMemberLabel(BaseSymbol member)
        {
            if (member is ObjectPropertySymbol)
            {
                return EnsureDollar(member.Name);
            }

            return member.Name;
        }

        private static string QualifiedInsertName(BaseSymbol symbol)
        {
            string fqn = symbol.FullyQualifiedName.TrimStart('\\');
            return string.IsNullOrEmpty(fqn) ? symbol.Name : "\\" + fqn;
        }

        private static string EnsureDollar(string name)
            => name.StartsWith('$') ? name : "$" + name;

        private static string StripDollar(string name)
            => name.StartsWith('$') ? name[1..] : name;

        private static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_';

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
