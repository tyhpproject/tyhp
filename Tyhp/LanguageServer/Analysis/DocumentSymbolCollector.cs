namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Enum;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Builds a hierarchical LSP outline from a document AST.
    /// </summary>
    internal static class DocumentSymbolCollector
    {
        /// <summary>
        /// Walks <paramref name="ast"/> and returns nested <see cref="DocumentSymbol"/> nodes
        /// for namespaces, types, members, functions, constants, and type aliases.
        /// </summary>
        public static DocumentSymbol[] Collect(SrcFileAst ast, string content)
        {
            ArgumentNullException.ThrowIfNull(ast);
            content ??= string.Empty;
            var roots = new List<DocumentSymbol>();
            CollectContainer(ast, content, roots, currentNamespace: null);
            return [.. roots];
        }

        private static DocumentSymbol? CollectContainer(
            IBase2Ast node,
            string content,
            List<DocumentSymbol> siblings,
            DocumentSymbol? currentNamespace)
        {
            if (node is SrcFileAst or PhpTopStatementListAst)
            {
                DocumentSymbol? ns = currentNamespace;
                foreach (IBase2Ast child in EnumerateChildren(node))
                {
                    ns = CollectContainer(child, content, siblings, ns);
                }

                return ns;
            }

            if (IsBraceLessNamespace(node))
            {
                DocumentSymbol? created = CreateNamedSymbol(node, content, out _);
                if (created is not null)
                {
                    siblings.Add(created);
                    return created;
                }

                return currentNamespace;
            }

            if (node is PhpPropertyDeclAst propertyDecl)
            {
                foreach (PhpPropertyAst property in propertyDecl.Properties?.GetAllNotNull() ?? [])
                {
                    DocumentSymbol? propertySymbol = CreateNamedSymbol(property, content, out _, propertyDecl);
                    if (propertySymbol is not null)
                    {
                        AddSibling(siblings, currentNamespace, propertySymbol);
                    }
                }

                return currentNamespace;
            }

            if (TryCreateDeclaration(node, content, out DocumentSymbol? symbol, out IBase2Ast? childRoot))
            {
                var children = new List<DocumentSymbol>();
                if (childRoot is not null)
                {
                    CollectContainer(childRoot, content, children, currentNamespace: null);
                }

                symbol!.Children = [.. children];
                AddSibling(siblings, currentNamespace, symbol);
                return currentNamespace;
            }

            foreach (IBase2Ast child in EnumerateChildren(node))
            {
                currentNamespace = CollectContainer(child, content, siblings, currentNamespace);
            }

            return currentNamespace;
        }

        private static bool TryCreateDeclaration(
            IBase2Ast node,
            string content,
            out DocumentSymbol? symbol,
            out IBase2Ast? childRoot)
        {
            symbol = null;
            childRoot = null;

            if (node is PhpPropertyDeclAst or PhpConstDeclListAst)
            {
                return false;
            }

            switch (node)
            {
                case PhpNamespaceDeclAst ns:
                    symbol = CreateNamedSymbol(ns, content, out childRoot);
                    childRoot ??= ns.TopStatements;
                    return symbol is not null;
                case PhpBlockNamespaceDeclAst blockNs:
                    symbol = CreateNamedSymbol(blockNs, content, out childRoot);
                    childRoot ??= blockNs.TopStatements;
                    return symbol is not null;
                case PhpObjectTypeDeclAst obj:
                    symbol = CreateNamedSymbol(obj, content, out childRoot);
                    childRoot ??= obj.Body;
                    return symbol is not null;
                case TyhpStructDeclAst structDecl:
                    symbol = CreateNamedSymbol(structDecl, content, out childRoot);
                    childRoot ??= structDecl.PropertyList;
                    return symbol is not null;
                case TyhpExtensionDeclAst extension:
                    symbol = CreateNamedSymbol(extension, content, out childRoot);
                    childRoot ??= extension.FunctionList;
                    return symbol is not null;
                case PhpFunctionDeclAst function:
                    symbol = CreateNamedSymbol(function, content, out childRoot);
                    childRoot ??= function.Body;
                    return symbol is not null;
                case PhpMethodDeclAst method:
                    symbol = CreateNamedSymbol(method, content, out childRoot);
                    childRoot ??= method.Body;
                    return symbol is not null;
                case PhpPropertyAst property:
                    symbol = CreateNamedSymbol(property, content, out childRoot);
                    return symbol is not null;
                case PhpConstDeclAst constant:
                    symbol = CreateNamedSymbol(constant, content, out childRoot);
                    return symbol is not null;
                case PhpEnumCaseAst enumCase:
                    symbol = CreateNamedSymbol(enumCase, content, out childRoot);
                    return symbol is not null;
                case TyhpTypeAliasAst alias:
                    symbol = CreateNamedSymbol(alias, content, out childRoot);
                    return symbol is not null;
                case TyhpStructPropertyAst structProperty:
                    IBase2Ast named = structProperty.Property as IBase2Ast ?? structProperty;
                    symbol = CreateNamedSymbol(named, content, out childRoot, structProperty);
                    return symbol is not null;
                case TyhpOperatorOverloadAst overload:
                    symbol = CreateNamedSymbol(overload, content, out childRoot);
                    return symbol is not null;
                default:
                    return false;
            }
        }

        private static DocumentSymbol? CreateNamedSymbol(
            IBase2Ast node,
            string content,
            out IBase2Ast? childRoot,
            IBase2Ast? detailNode = null)
        {
            childRoot = null;
            string name = DisplayName(node);
            if (IsSkippedName(name))
            {
                return null;
            }

            SymbolKind kind = ToSymbolKind(node);
            string? detail = Detail(detailNode ?? node);
            ProtocolRange range = PositionUtilities.ToLspRange(node);
            ProtocolRange selection = PositionUtilities.ToIdentifierRange(
                SymbolFinder.PreferIdentifierNode(node),
                name,
                content);
            selection = ClampSelection(range, selection);
            bool deprecated = IsDeprecated(node);

            return new DocumentSymbol
            {
                Name = name,
                Detail = detail,
                Kind = kind,
                Deprecated = deprecated,
                Range = range,
                SelectionRange = selection,
                Children = [],
            };
        }

        private static string DisplayName(IBase2Ast node)
        {
            if (node is PhpConstDeclAst constant)
            {
                return FirstNonEmpty(constant.Identifier, SymbolFinder.GetDisplayName(constant));
            }

            if (node is TyhpOperatorOverloadAst overload)
            {
                string op = SymbolFinder.GetDisplayName(overload.Op as IBase2Ast);
                return string.IsNullOrEmpty(op) ? "operator" : "operator " + op;
            }

            string name = SymbolFinder.GetDisplayName(node);
            if (node is PhpPropertyAst or PhpVariableAst)
            {
                return IdentifierSyntax.EnsureDollar(name);
            }

            return name;
        }

        private static string? Detail(IBase2Ast node)
        {
            return node switch
            {
                PhpFunctionDeclAst function => FormatReturn(function.ReturnType),
                PhpMethodDeclAst method => FormatReturn(method.ReturnType),
                PhpPropertyDeclAst property => SymbolFormatter.FormatType(property.Type),
                TyhpStructPropertyAst structProperty => SymbolFormatter.FormatType(structProperty.TypeExpression),
                PhpConstDeclAst constant => SymbolFormatter.FormatType(constant.Type),
                TyhpTypeAliasAst alias => SymbolFormatter.FormatType(alias.TypeExpression),
                PhpObjectTypeDeclAst obj => ObjectKindLabel(obj),
                TyhpStructDeclAst => "struct",
                TyhpExtensionDeclAst => "extension",
                _ => string.Empty,
            };
        }

        private static string FormatReturn(ITypeExpression? returnType)
        {
            string type = SymbolFormatter.FormatType(returnType);
            return string.IsNullOrEmpty(type) ? string.Empty : type;
        }

        private static SymbolKind ToSymbolKind(IBase2Ast node)
        {
            return node switch
            {
                PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst => SymbolKind.Namespace,
                PhpObjectTypeDeclAst obj => ObjectKind(obj),
                TyhpStructDeclAst => SymbolKind.Struct,
                TyhpExtensionDeclAst => SymbolKind.Class,
                PhpFunctionDeclAst => SymbolKind.Function,
                PhpMethodDeclAst method when IsConstructorName(method.Identifier) => SymbolKind.Constructor,
                PhpMethodDeclAst => SymbolKind.Method,
                PhpPropertyAst or TyhpStructPropertyAst => SymbolKind.Property,
                PhpEnumCaseAst => SymbolKind.EnumMember,
                PhpConstDeclAst => SymbolKind.Constant,
                TyhpTypeAliasAst => SymbolKind.TypeParameter,
                TyhpOperatorOverloadAst => SymbolKind.Operator,
                _ => SymbolKind.Variable,
            };
        }

        private static SymbolKind ObjectKind(PhpObjectTypeDeclAst obj)
        {
            PhpTypeDeclType? kind = PhpTypeDeclTypeExtensions.FromToken(obj.DeclType?.TokenValue ?? -1);
            return kind switch
            {
                PhpTypeDeclType.Interface => SymbolKind.Interface,
                PhpTypeDeclType.Trait => SymbolKind.Class,
                PhpTypeDeclType.Enum => SymbolKind.Enum,
                _ => SymbolKind.Class,
            };
        }

        private static string ObjectKindLabel(PhpObjectTypeDeclAst obj)
        {
            PhpTypeDeclType? kind = PhpTypeDeclTypeExtensions.FromToken(obj.DeclType?.TokenValue ?? -1);
            return kind switch
            {
                PhpTypeDeclType.Interface => "interface",
                PhpTypeDeclType.Trait => "trait",
                PhpTypeDeclType.Enum => "enum",
                _ => "class",
            };
        }

        private static void AddSibling(
            List<DocumentSymbol> siblings,
            DocumentSymbol? currentNamespace,
            DocumentSymbol symbol)
        {
            if (currentNamespace is null)
            {
                siblings.Add(symbol);
                return;
            }

            var children = currentNamespace.Children is { Length: > 0 } existing
                ? existing.ToList()
                : [];
            children.Add(symbol);
            currentNamespace.Children = [.. children];
        }

        private static bool IsBraceLessNamespace(IBase2Ast node)
        {
            return node is PhpNamespaceDeclAst ns && ns.TopStatements is null
                || node is PhpBlockNamespaceDeclAst block && block.TopStatements is null;
        }

        private static bool IsConstructorName(string? name)
            => string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase);

        private static bool IsSkippedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "<error>")
            {
                return true;
            }

            return name.StartsWith("anonClass@", StringComparison.Ordinal)
                || name.StartsWith("anonStruct@", StringComparison.Ordinal);
        }

        private static bool IsDeprecated(IBase2Ast node)
        {
            if (node.BoundSymbol is BaseSymbol symbol && (symbol.IsDeprecated || symbol.IsObsolete))
            {
                return true;
            }

            string? doc = node.DocComment;
            return !string.IsNullOrEmpty(doc)
                && doc.Contains("@deprecated", StringComparison.OrdinalIgnoreCase);
        }

        private static ProtocolRange ClampSelection(ProtocolRange range, ProtocolRange selection)
        {
            if (range.Start is null || range.End is null || selection.Start is null || selection.End is null)
            {
                return range;
            }

            if (IsBefore(selection.Start, range.Start) || IsBefore(range.End, selection.End))
            {
                return range;
            }

            return selection;
        }

        private static bool IsBefore(Position left, Position right)
        {
            if (left.Line != right.Line)
            {
                return left.Line < right.Line;
            }

            return left.Character < right.Character;
        }

        private static IEnumerable<IBase2Ast> EnumerateChildren(IBase2Ast node)
        {
            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    yield return child;
                }
            }
        }

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
