namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using Tyhp.TyhpLang.Enum;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Walks a document AST and encodes LSP semantic tokens (relative 5-tuples).
    /// </summary>
    internal static class SemanticTokenCollector
    {
        internal static readonly string[] TokenTypes =
        [
            SemanticTokenTypes.Namespace,
            SemanticTokenTypes.Type,
            SemanticTokenTypes.Class,
            SemanticTokenTypes.Enum,
            SemanticTokenTypes.Interface,
            SemanticTokenTypes.Struct,
            SemanticTokenTypes.TypeParameter,
            SemanticTokenTypes.Parameter,
            SemanticTokenTypes.Variable,
            SemanticTokenTypes.Property,
            SemanticTokenTypes.EnumMember,
            SemanticTokenTypes.Function,
            SemanticTokenTypes.Method,
            SemanticTokenTypes.Keyword,
            SemanticTokenTypes.Modifier,
            SemanticTokenTypes.Comment,
            SemanticTokenTypes.String,
            SemanticTokenTypes.Number,
            SemanticTokenTypes.Operator,
        ];

        internal static readonly string[] TokenModifiers = [.. SemanticTokenModifiers.AllModifiers];

        internal static SemanticTokensLegend Legend { get; } = new()
        {
            TokenTypes = TokenTypes,
            TokenModifiers = TokenModifiers,
        };

        private const int TypeNamespace = 0;
        private const int TypeType = 1;
        private const int TypeClass = 2;
        private const int TypeEnum = 3;
        private const int TypeInterface = 4;
        private const int TypeStruct = 5;
        private const int TypeTypeParameter = 6;
        private const int TypeParameter = 7;
        private const int TypeVariable = 8;
        private const int TypeProperty = 9;
        private const int TypeEnumMember = 10;
        private const int TypeFunction = 11;
        private const int TypeMethod = 12;

        private const int ModDeclaration = 1 << 0;
        private const int ModDefinition = 1 << 1;
        private const int ModReadonly = 1 << 2;
        private const int ModStatic = 1 << 3;
        private const int ModDeprecated = 1 << 4;
        private const int ModAbstract = 1 << 5;
        private const int ModAsync = 1 << 6;
        private const int ModDefaultLibrary = 1 << 9;

        /// <summary>
        /// Collects and encodes semantic tokens for <paramref name="ast"/>.
        /// </summary>
        public static int[] CollectData(
            SrcFileAst ast,
            string content,
            GlobalScope? scope,
            SymbolTree? tree,
            SymbolFinder finder)
        {
            ArgumentNullException.ThrowIfNull(ast);
            ArgumentNullException.ThrowIfNull(finder);
            content ??= string.Empty;

            var tokens = new List<SemanticTokenSpan>();
            var path = new List<IBase2Ast>();
            Walk(ast, ast, content, scope, tree, finder, path, tokens);
            return Encode(tokens);
        }

        /// <summary>
        /// Diffs two encoded token arrays into a single LSP edit covering the changed middle.
        /// </summary>
        public static SemanticTokensEdit[] ComputeDelta(int[] previous, int[] current)
        {
            previous ??= [];
            current ??= [];
            int prefix = 0;
            int maxPrefix = Math.Min(previous.Length, current.Length);
            while (prefix < maxPrefix && previous[prefix] == current[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            int maxSuffixPrev = previous.Length - prefix;
            int maxSuffixCur = current.Length - prefix;
            while (suffix < maxSuffixPrev
                && suffix < maxSuffixCur
                && previous[previous.Length - 1 - suffix] == current[current.Length - 1 - suffix])
            {
                suffix++;
            }

            int deleteCount = previous.Length - prefix - suffix;
            int insertLength = current.Length - prefix - suffix;
            int[] insert = insertLength <= 0
                ? []
                : current[prefix..(prefix + insertLength)];
            if (deleteCount == 0 && insert.Length == 0)
            {
                return [];
            }

            return
            [
                new SemanticTokensEdit
                {
                    Start = prefix,
                    DeleteCount = deleteCount,
                    Data = insert,
                },
            ];
        }

        /// <summary>
        /// Decodes relative 5-tuples into absolute positions plus legend names (tests).
        /// </summary>
        internal static IReadOnlyList<DecodedSemanticToken> Decode(int[] data)
        {
            data ??= [];
            var result = new List<DecodedSemanticToken>();
            int line = 0;
            int character = 0;
            for (int i = 0; i + 4 < data.Length; i += 5)
            {
                int deltaLine = data[i];
                int deltaStart = data[i + 1];
                int length = data[i + 2];
                int typeIndex = data[i + 3];
                int modifiers = data[i + 4];
                line += deltaLine;
                character = deltaLine == 0 ? character + deltaStart : deltaStart;
                string type = typeIndex >= 0 && typeIndex < TokenTypes.Length
                    ? TokenTypes[typeIndex]
                    : "unknown";
                result.Add(new DecodedSemanticToken(line, character, length, type, DecodeModifiers(modifiers)));
            }

            return result;
        }

        private static void Walk(
            SrcFileAst ast,
            IBase2Ast node,
            string content,
            GlobalScope? scope,
            SymbolTree? tree,
            SymbolFinder finder,
            List<IBase2Ast> path,
            List<SemanticTokenSpan> tokens)
        {
            path.Add(node);
            TryEmit(ast, node, content, scope, tree, finder, path, tokens);
            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    Walk(ast, child, content, scope, tree, finder, path, tokens);
                }
            }

            path.RemoveAt(path.Count - 1);
        }

        private static void TryEmit(
            SrcFileAst ast,
            IBase2Ast node,
            string content,
            GlobalScope? scope,
            SymbolTree? tree,
            SymbolFinder finder,
            List<IBase2Ast> path,
            List<SemanticTokenSpan> tokens)
        {
            if (node is TokenValueAst && node is not PhpNameAst)
            {
                return;
            }

            if (node is PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpObjectTypeDeclAst
                or TyhpStructDeclAst
                or TyhpExtensionDeclAst
                or PhpPropertyAst
                or PhpParameterAst
                or PhpConstDeclAst
                or PhpNamespaceDeclAst
                or PhpBlockNamespaceDeclAst)
            {
                EmitDeclarationName(ast, node, content, scope, tree, finder, path, tokens);
                return;
            }

            if (node is PhpVariableAst variable)
            {
                EmitVariable(ast, variable, content, scope, tree, finder, path, tokens);
                return;
            }

            if (node is PhpImportDeclAst importDecl)
            {
                EmitImportName(importDecl, content, tokens);
                return;
            }

            if (node is PhpNameAst or PhpBuiltinTypeAst)
            {
                EmitName(ast, node, content, scope, tree, finder, path, tokens);
            }
        }

        /// <summary>
        /// <c>use App\Models\User;</c> (and <c>use function</c>/<c>use const</c>) carry their
        /// imported name as a plain <see cref="PhpImportDeclAst.NamespaceName"/> string, not as
        /// a nested <see cref="PhpNameAst"/> child, so they never reach <see cref="EmitName"/>.
        /// Locate the name within the declaration's own span and colorize it the same way a
        /// qualified name reference would be.
        /// </summary>
        private static void EmitImportName(
            PhpImportDeclAst importDecl,
            string content,
            List<SemanticTokenSpan> tokens)
        {
            string name = importDecl.NamespaceName ?? string.Empty;
            if (IsSkippedName(name))
            {
                return;
            }

            int lastType = importDecl.UseType?.ValueString?.ToLowerInvariant() switch
            {
                "function" => TypeFunction,
                "const" => TypeVariable,
                _ => TypeType,
            };

            EmitQualified(importDecl, content, name, lastType, 0, TypeNamespace, 0, tokens);
        }

        private static void EmitDeclarationName(
            SrcFileAst ast,
            IBase2Ast node,
            string content,
            GlobalScope? scope,
            SymbolTree? tree,
            SymbolFinder finder,
            List<IBase2Ast> path,
            List<SemanticTokenSpan> tokens)
        {
            string name = SymbolFinder.GetDisplayName(node);
            if (IsSkippedName(name))
            {
                return;
            }

            if (node is PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst)
            {
                EmitQualified(
                    node,
                    content,
                    name,
                    TypeNamespace,
                    ModDeclaration | ModDefinition,
                    TypeNamespace,
                    ModDeclaration | ModDefinition,
                    tokens);
                return;
            }

            BaseSymbol? symbol = AsUseful(node.BoundSymbol)
                ?? finder.ResolveNode(ast, node, path, scope, tree);
            var (type, modifiers) = Classify(symbol, node, path, isDeclaration: true);
            if (node is PhpParameterAst)
            {
                type = TypeParameter;
            }

            EmitIdentifier(node, name, content, type, modifiers, tokens);
        }

        private static void EmitVariable(
            SrcFileAst ast,
            PhpVariableAst variable,
            string content,
            GlobalScope? scope,
            SymbolTree? tree,
            SymbolFinder finder,
            List<IBase2Ast> path,
            List<SemanticTokenSpan> tokens)
        {
            string name = SymbolFinder.GetDisplayName(variable);
            if (IsSkippedName(name))
            {
                return;
            }

            BaseSymbol? symbol = AsUseful(variable.BoundSymbol)
                ?? finder.ResolveNode(ast, variable, path, scope, tree);
            bool isDeclaration = IsVariableDeclaration(variable, path, symbol);
            var (type, modifiers) = Classify(symbol, variable, path, isDeclaration);
            if (type != TypeParameter)
            {
                type = TypeVariable;
            }

            if (symbol is VariableSymbol { IsParameter: true })
            {
                type = TypeParameter;
            }

            EmitIdentifier(variable, IdentifierSyntax.EnsureDollar(name), content, type, modifiers, tokens);
        }

        private static void EmitName(
            SrcFileAst ast,
            IBase2Ast node,
            string content,
            GlobalScope? scope,
            SymbolTree? tree,
            SymbolFinder finder,
            List<IBase2Ast> path,
            List<SemanticTokenSpan> tokens)
        {
            IBase2Ast resolveTarget = PreferResolveTarget(node, path);
            string name = SymbolFinder.GetDisplayName(node);
            if (IsSkippedName(name) || IdentifierSyntax.IsSelfStaticParent(name))
            {
                return;
            }

            if (node is PhpBuiltinTypeAst)
            {
                EmitIdentifier(
                    node,
                    name,
                    content,
                    TypeType,
                    ModDefaultLibrary,
                    tokens);
                return;
            }

            BaseSymbol? symbol = AsUseful(resolveTarget.BoundSymbol)
                ?? finder.ResolveNode(ast, resolveTarget, path, scope, tree)
                ?? AsUseful(node.BoundSymbol)
                ?? finder.ResolveNode(ast, node, path, scope, tree);

            bool isDeclaration = IsNameDeclaration(path);
            var (type, modifiers) = Classify(symbol, node, path, isDeclaration);
            if (IsTypeAnnotationContext(path) && type != TypeTypeParameter)
            {
                type = TypeType;
            }

            if (name.Contains('\\', StringComparison.Ordinal))
            {
                EmitQualified(node, content, name, type, modifiers, TypeNamespace, 0, tokens);
                return;
            }

            EmitIdentifier(node, name, content, type, modifiers, tokens);
        }

        private static IBase2Ast PreferResolveTarget(IBase2Ast node, List<IBase2Ast> path)
        {
            if (path.Count < 2)
            {
                return node;
            }

            IBase2Ast parent = path[^2];
            if (parent is PhpInstanceMemberAccessAst
                or PhpStaticMemberAccessAst
                or PhpClassConstantAccessAst
                or PhpNamedTypeAst
                or PhpNewAst)
            {
                return parent;
            }

            return node;
        }

        private static (int Type, int Modifiers) Classify(
            BaseSymbol? symbol,
            IBase2Ast node,
            List<IBase2Ast> path,
            bool isDeclaration)
        {
            int modifiers = 0;
            if (isDeclaration)
            {
                modifiers |= ModDeclaration | ModDefinition;
            }

            if (symbol is not null)
            {
                modifiers |= ModifiersFromSymbol(symbol);
            }
            else if (IsDeprecatedNode(node))
            {
                modifiers |= ModDeprecated;
            }

            int type = TypeFromSymbol(symbol, node, path);
            return (type, modifiers);
        }

        private static int TypeFromSymbol(BaseSymbol? symbol, IBase2Ast node, List<IBase2Ast> path)
        {
            if (path.Count >= 2 && path[^2] is TyhpGenericsTypeArgumentAst)
            {
                return TypeTypeParameter;
            }

            if (symbol is GenericTypeParameterSymbol)
            {
                return TypeTypeParameter;
            }

            if (symbol is VariableSymbol { IsParameter: true })
            {
                return TypeParameter;
            }

            if (symbol is VariableSymbol or SuperGlobalSymbol)
            {
                return TypeVariable;
            }

            if (symbol is ObjectPropertySymbol)
            {
                return TypeProperty;
            }

            if (symbol is FunctionDeclarationSymbol or BuiltInFunctionSymbol or AnonymousFunctionSymbol)
            {
                return TypeFunction;
            }

            if (symbol is ObjectMethodSymbol)
            {
                return TypeMethod;
            }

            if (symbol is ObjectConstantSymbol { IsEnumCase: true })
            {
                return TypeEnumMember;
            }

            if (symbol is ObjectConstantSymbol or ConstantSymbol or MagicConstantSymbol)
            {
                return TypeVariable;
            }

            if (symbol is TypeAliasSymbol or ObjectTypeAliasSymbol or BuiltInTypeSymbol or BuiltInUtilityTypeSymbol)
            {
                return TypeType;
            }

            if (symbol is NamespaceSymbol)
            {
                return TypeNamespace;
            }

            if (symbol is ObjectDeclarationSymbol obj)
            {
                if (obj.IsStruct)
                {
                    return TypeStruct;
                }

                return obj.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => TypeInterface,
                    PhpTypeDeclType.Enum => TypeEnum,
                    _ => TypeClass,
                };
            }

            return node switch
            {
                PhpFunctionDeclAst => TypeFunction,
                PhpMethodDeclAst => TypeMethod,
                PhpPropertyAst or TyhpStructPropertyAst => TypeProperty,
                PhpParameterAst => TypeParameter,
                PhpEnumCaseAst => TypeEnumMember,
                TyhpStructDeclAst => TypeStruct,
                PhpObjectTypeDeclAst objDecl => ObjectDeclType(objDecl),
                PhpConstDeclAst => TypeVariable,
                PhpBuiltinTypeAst => TypeType,
                TyhpTypeAliasAst => TypeType,
                PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst => TypeNamespace,
                _ => TypeClass,
            };
        }

        private static int ObjectDeclType(PhpObjectTypeDeclAst obj)
        {
            PhpTypeDeclType? kind = PhpTypeDeclTypeExtensions.FromToken(obj.DeclType?.TokenValue ?? -1);
            return kind switch
            {
                PhpTypeDeclType.Interface => TypeInterface,
                PhpTypeDeclType.Enum => TypeEnum,
                _ => TypeClass,
            };
        }

        private static int ModifiersFromSymbol(BaseSymbol symbol)
        {
            int modifiers = 0;
            MemberModifier visibility = symbol.Visibility;
            if ((visibility & MemberModifier.Static) != 0
                || symbol.SymbolType is SymbolType.StaticObjectMethod
                    or SymbolType.StaticObjectProperty
                    or SymbolType.StaticObjectAccessorMethod)
            {
                modifiers |= ModStatic;
            }

            if ((visibility & MemberModifier.Abstract) != 0)
            {
                modifiers |= ModAbstract;
            }

            if ((visibility & MemberModifier.Readonly) != 0
                || symbol is ConstantSymbol
                or ObjectConstantSymbol
                or MagicConstantSymbol)
            {
                modifiers |= ModReadonly;
            }

            if ((visibility & MemberModifier.Async) != 0)
            {
                modifiers |= ModAsync;
            }

            if (symbol.IsDeprecated
                || symbol.IsObsolete
                || (!string.IsNullOrEmpty(symbol.DocComment)
                    && symbol.DocComment.Contains("@deprecated", StringComparison.OrdinalIgnoreCase)))
            {
                modifiers |= ModDeprecated;
            }

            if (symbol is BuiltInTypeSymbol
                or BuiltInUtilityTypeSymbol
                or BuiltInFunctionSymbol
                or MagicConstantSymbol
                or SuperGlobalSymbol)
            {
                modifiers |= ModDefaultLibrary;
            }

            return modifiers;
        }

        private static bool IsTypeAnnotationContext(List<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                IBase2Ast node = path[i];
                if (node is PhpNewAst
                    or PhpInstanceMemberAccessAst
                    or PhpStaticMemberAccessAst
                    or PhpClassConstantAccessAst)
                {
                    return false;
                }

                if (node is ITypeExpression or PhpNamedTypeAst or PhpBuiltinTypeAst or PhpParameterAst)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNameDeclaration(List<IBase2Ast> path)
        {
            if (path.Count < 2)
            {
                return false;
            }

            return path[^2] is TyhpTypeAliasAst
                or TyhpGenericsTypeArgumentAst
                or PhpEnumCaseAst;
        }

        private static bool IsVariableDeclaration(PhpVariableAst variable, List<IBase2Ast> path, BaseSymbol? symbol)
        {
            if (symbol?.DeclaringAstNode is IBase2Ast declaring
                && (ReferenceEquals(declaring, variable)
                    || (declaring.Line == variable.Line && declaring.Column == variable.Column)))
            {
                return true;
            }

            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is PhpParameterAst)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDeprecatedNode(IBase2Ast node)
        {
            if (node.BoundSymbol is BaseSymbol symbol && (symbol.IsDeprecated || symbol.IsObsolete))
            {
                return true;
            }

            string? doc = node.DocComment;
            return !string.IsNullOrEmpty(doc)
                && doc.Contains("@deprecated", StringComparison.OrdinalIgnoreCase);
        }

        private static void EmitIdentifier(
            IBase2Ast node,
            string name,
            string content,
            int type,
            int modifiers,
            List<SemanticTokenSpan> tokens)
        {
            if (IsSkippedName(name))
            {
                return;
            }

            ProtocolRange range = PositionUtilities.ToIdentifierRange(node, name, content);
            EmitRange(range, name, type, modifiers, tokens);
        }

        private static void EmitQualified(
            IBase2Ast node,
            string content,
            string name,
            int lastType,
            int lastModifiers,
            int nsType,
            int nsModifiers,
            List<SemanticTokenSpan> tokens)
        {
            string[] parts = name.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            if (parts.Length == 1)
            {
                EmitIdentifier(node, parts[0], content, lastType, lastModifiers, tokens);
                return;
            }

            ProtocolRange span = PositionUtilities.ToIdentifierRange(node, name, content);
            int searchFrom = PositionUtilities.GetOffset(content, span.Start);
            int searchEnd = PositionUtilities.GetOffset(content, span.End);
            searchFrom = Math.Clamp(searchFrom, 0, content.Length);
            searchEnd = Math.Clamp(searchEnd, searchFrom, content.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int hit = content.IndexOf(part, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (hit < 0 || hit >= searchEnd)
                {
                    if (i == parts.Length - 1)
                    {
                        EmitIdentifier(node, part, content, lastType, lastModifiers, tokens);
                    }

                    continue;
                }

                int type = i == parts.Length - 1 ? lastType : nsType;
                int mods = i == parts.Length - 1 ? lastModifiers : nsModifiers;
                Position start = PositionUtilities.GetPosition(content, hit);
                tokens.Add(new SemanticTokenSpan(start.Line, start.Character, part.Length, type, mods));
                searchFrom = hit + part.Length;
            }
        }

        private static void EmitRange(
            ProtocolRange range,
            string name,
            int type,
            int modifiers,
            List<SemanticTokenSpan> tokens)
        {
            if (range.Start is null || range.End is null)
            {
                return;
            }

            int length;
            if (range.Start.Line == range.End.Line)
            {
                length = range.End.Character - range.Start.Character;
            }
            else
            {
                length = Math.Max(1, name.Length);
            }

            if (length <= 0)
            {
                return;
            }

            tokens.Add(new SemanticTokenSpan(
                range.Start.Line,
                range.Start.Character,
                length,
                type,
                modifiers));
        }

        private static int[] Encode(List<SemanticTokenSpan> tokens)
        {
            tokens.Sort(static (left, right) =>
            {
                int line = left.Line.CompareTo(right.Line);
                if (line != 0)
                {
                    return line;
                }

                int character = left.Character.CompareTo(right.Character);
                return character != 0 ? character : right.Length.CompareTo(left.Length);
            });

            var data = new List<int>(tokens.Count * 5);
            int prevLine = 0;
            int prevCharacter = 0;
            int prevEnd = 0;
            int prevTokenLine = -1;
            foreach (SemanticTokenSpan token in tokens)
            {
                if (token.Length <= 0 || token.Line < 0 || token.Character < 0)
                {
                    continue;
                }

                if (token.Line == prevTokenLine && token.Character < prevEnd)
                {
                    continue;
                }

                int deltaLine = token.Line - prevLine;
                int deltaStart = deltaLine == 0 ? token.Character - prevCharacter : token.Character;
                if (deltaLine < 0 || deltaStart < 0)
                {
                    continue;
                }

                data.Add(deltaLine);
                data.Add(deltaStart);
                data.Add(token.Length);
                data.Add(token.Type);
                data.Add(token.Modifiers);
                prevLine = token.Line;
                prevCharacter = token.Character;
                prevTokenLine = token.Line;
                prevEnd = token.Character + token.Length;
            }

            return [.. data];
        }

        private static string[] DecodeModifiers(int modifiers)
        {
            var names = new List<string>();
            for (int i = 0; i < TokenModifiers.Length; i++)
            {
                if ((modifiers & (1 << i)) != 0)
                {
                    names.Add(TokenModifiers[i]);
                }
            }

            return [.. names];
        }

        private static BaseSymbol? AsUseful(IBaseSymbol? symbol)
        {
            return symbol is BaseSymbol baseSymbol
                && baseSymbol.SymbolType is not (
                    SymbolType.Root
                    or SymbolType.File
                    or SymbolType.CodeBlock
                    or SymbolType.NamespaceBlock
                    or SymbolType.Statement
                    or SymbolType.DeclareBlock
                    or SymbolType.Label)
                ? baseSymbol
                : null;
        }

        private static bool IsSkippedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "<error>")
            {
                return true;
            }

            return name.StartsWith("anonClass@", StringComparison.Ordinal)
                || name.StartsWith("anonStruct@", StringComparison.Ordinal);
        }

        private readonly record struct SemanticTokenSpan(
            int Line,
            int Character,
            int Length,
            int Type,
            int Modifiers);
    }

    internal readonly record struct DecodedSemanticToken(
        int Line,
        int Character,
        int Length,
        string Type,
        string[] Modifiers);
}
