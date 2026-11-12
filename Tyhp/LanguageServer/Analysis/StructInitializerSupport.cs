namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.CLI;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;
    using Tyhp.LanguageServer.Handlers;
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
    using Tyhp.TyhpLang.Parser;
    using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Struct initializer and struct <c>with</c> list support for completion, hover,
    /// semantic tokens, and code actions.
    /// </summary>
    internal static class StructInitializerSupport
    {
        internal readonly record struct StructFieldOption(
            ObjectPropertySymbol Property,
            string Name,
            bool Required);

        /// <summary>
        /// True when <paramref name="cursor"/> is in a key slot of <c>new Name[</c> or <c>with [</c>.
        /// </summary>
        internal static bool TryFindKeySite(string content, int cursor, out int openBracket, out bool withList)
        {
            openBracket = -1;
            withList = false;
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            cursor = Math.Clamp(cursor, 0, content.Length);
            int identStart = IdentifierStart(content, cursor);
            if (!TryInnermostUnclosedBracket(content, identStart, out openBracket))
            {
                return false;
            }

            if (!IsKeySlot(content, openBracket, identStart))
            {
                return false;
            }

            if (IsWithBracket(content, openBracket))
            {
                withList = true;
                return true;
            }

            return TryReadNewTarget(content, openBracket, out _, out _);
        }

        /// <summary>
        /// Unset fields of the struct under construction at <paramref name="cursor"/>,
        /// required fields first. Null when the bracket list is not a struct list.
        /// </summary>
        internal static IReadOnlyList<StructFieldOption>? FieldsForCompletion(
            string content,
            int cursor,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? tree,
            SymbolFinder finder,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType)
        {
            if (!TryFindKeySite(content, cursor, out int openBracket, out bool withList))
            {
                return null;
            }

            ObjectDeclarationSymbol? target = ResolveCompletionTarget(
                content,
                cursor,
                openBracket,
                withList,
                ast,
                globalScope,
                tree,
                finder,
                fromScope ?? globalScope,
                getInferredType);
            if (target is not { IsStruct: true })
            {
                return null;
            }

            NameResolver? resolver = CreateResolver(globalScope, tree);
            IBaseScope? scope = fromScope ?? globalScope ?? target.ContainingScope;
            if (scope is null)
            {
                return null;
            }

            var present = KeysAlreadyPresent(content, openBracket, cursor);
            AddSpreadGuaranteedKeys(
                present,
                content,
                openBracket,
                ast,
                target,
                resolver,
                scope,
                globalScope,
                tree,
                fromScope ?? globalScope,
                getInferredType);
            var required = new List<StructFieldOption>();
            var optional = new List<StructFieldOption>();
            foreach (StructFieldOption field in EnumerateFields(target, resolver, scope))
            {
                if (present.Contains(field.Name))
                {
                    continue;
                }

                if (field.Required)
                {
                    required.Add(field);
                }
                else
                {
                    optional.Add(field);
                }
            }

            required.AddRange(optional);
            return required;
        }

        /// <summary>
        /// Adds the fields each spread in the list guarantees, the way the checker counts them:
        /// the target declares the field under the same array key, and the source struct either
        /// requires the field or gives it a default. A nullable source field with no default may
        /// be absent from the array, so it stays offered.
        /// </summary>
        private static void AddSpreadGuaranteedKeys(
            HashSet<string> present,
            string content,
            int openBracket,
            SrcFileAst? ast,
            ObjectDeclarationSymbol target,
            NameResolver? resolver,
            IBaseScope scope,
            GlobalScope? globalScope,
            SymbolTree? tree,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType)
        {
            if (ast is null)
            {
                return;
            }

            PhpArrayPairListAst? list = null;
            foreach (PhpBinaryOpAst withExpr in EnumerateWithExpressions(ast))
            {
                if (PairListOf(withExpr.Right) is PhpArrayPairListAst candidate
                    && ListOwnsBracket(content, candidate, openBracket))
                {
                    list = candidate;
                    break;
                }
            }

            if (list is null)
            {
                return;
            }

            var targetFields = EnumerateFields(target, resolver, scope).ToList();
            foreach (PhpArrayPairAst pair in list.GetAllNotNull())
            {
                if (!pair.IsExpansion || pair.ValueExpr is null)
                {
                    continue;
                }

                ObjectDeclarationSymbol? source = ResolveReceiver(
                    pair.ValueExpr,
                    globalScope,
                    tree,
                    fromScope,
                    getInferredType);
                if (source is not { IsStruct: true })
                {
                    continue;
                }

                foreach (StructFieldOption sourceField in EnumerateFields(source, resolver, scope))
                {
                    StructFieldOption? match = null;
                    foreach (StructFieldOption targetField in targetFields)
                    {
                        if (string.Equals(targetField.Name, sourceField.Name, StringComparison.Ordinal))
                        {
                            match = targetField;
                            break;
                        }
                    }

                    if (match is null
                        || !string.Equals(
                            ArrayKeyOf(sourceField.Property),
                            ArrayKeyOf(match.Value.Property),
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (sourceField.Property.DefaultValue is not null || !IsNullableOrUntyped(sourceField.Property))
                    {
                        present.Add(sourceField.Name);
                    }
                }
            }
        }

        private static bool IsNullableOrUntyped(ObjectPropertySymbol property)
        {
            return property.DeclaredType is null || IsNullableType(property.DeclaredType);
        }

        private static string ArrayKeyOf(ObjectPropertySymbol property)
        {
            if (StructTypeHelper.TryGetIntegerKeyAlias(property) is int integerKey)
            {
                return integerKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            string? stringAlias = StructTypeHelper.TryGetStringKeyAlias(property);
            return string.IsNullOrEmpty(stringAlias) ? BareName(property.Name) : stringAlias;
        }

        /// <summary>
        /// The struct field whose key contains the cursor, when the list belongs to a struct.
        /// </summary>
        internal static bool TryGetHoverField(
            IReadOnlyList<IBase2Ast> path,
            GlobalScope? globalScope,
            SymbolTree? tree,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType,
            out ObjectPropertySymbol? property,
            out IBase2Ast? keyNode)
        {
            property = null;
            keyNode = null;
            if (!TryKeyOfStructList(path, globalScope, tree, fromScope, getInferredType, out keyNode, out ObjectDeclarationSymbol? target)
                || target is null
                || keyNode is null)
            {
                return false;
            }

            string? name = PropertyNameFromKey(keyNode);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            NameResolver? resolver = CreateResolver(globalScope, tree);
            IBaseScope? scope = fromScope ?? globalScope ?? target.ContainingScope;
            if (scope is null)
            {
                return false;
            }

            property = FindField(target, name, resolver, scope);
            return property is not null;
        }

        /// <summary>
        /// True when <paramref name="node"/> is a key in a struct initializer or a struct <c>with</c> list.
        /// Initializer keys are included even when the name is not a struct.
        /// </summary>
        internal static bool IsPropertyKey(
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            GlobalScope? globalScope,
            SymbolTree? tree,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?>? getInferredType)
        {
            if (path.Count < 2 || path[^2] is not PhpArrayPairAst pair)
            {
                return false;
            }

            if (pair.KeyExpr is not IBase2Ast key || !ReferenceEquals(key, node))
            {
                return false;
            }

            if (!TryEnclosingWith(path, pair, out PhpBinaryOpAst? withExpr) || withExpr is null)
            {
                return false;
            }

            if (withExpr.Left is PhpNewAst { IsStructInitializer: true })
            {
                return true;
            }

            return ResolveReceiver(withExpr.Left, globalScope, tree, fromScope, getInferredType) is { IsStruct: true };
        }

        /// <summary>Source text of a list key, without quotes or a leading <c>$</c>.</summary>
        internal static string PropertyKeyText(IBase2Ast node)
        {
            return PropertyNameFromKey(node) ?? string.Empty;
        }

        internal static IEnumerable<CodeAction> UseInitializerActions(
            Uri uri,
            string content,
            SrcFileAst ast,
            ProtocolRange range,
            GlobalScope? globalScope,
            IBaseScope? fromScope)
        {
            NameResolver? resolver = CreateResolver(globalScope, null);
            IBaseScope? scope = fromScope ?? globalScope;
            List<PhpBinaryOpAst> withExpressions = EnumerateWithExpressions(ast).ToList();
            var feedsAnotherWith = new HashSet<PhpBinaryOpAst>(ReferenceEqualityComparer.Instance);
            foreach (PhpBinaryOpAst outer in withExpressions)
            {
                if (outer.Left is PhpBinaryOpAst inner && IsWithOperator(inner))
                {
                    feedsAnotherWith.Add(inner);
                }
            }

            foreach (PhpBinaryOpAst withExpr in withExpressions)
            {
                // `new X() with [a] with [b]` becomes `new X[a] with [b]`, which is not valid:
                // an initializer takes every entry in its brackets.
                if (feedsAnotherWith.Contains(withExpr)
                    || withExpr.Left is not PhpNewAst { IsStructInitializer: false } newExpr
                    || PairListOf(withExpr.Right) is not PhpArrayPairListAst list)
                {
                    continue;
                }

                if (!OverlapsAny(range, withExpr, newExpr, list))
                {
                    continue;
                }

                if (!HasEmptyArgumentList(content, newExpr))
                {
                    continue;
                }

                if (!IsStructConstruction(content, newExpr, resolver, scope))
                {
                    continue;
                }

                TextEdit? edit = TryUseInitializerEdit(content, newExpr, list);
                if (edit is null)
                {
                    continue;
                }

                yield return new CodeAction
                {
                    Title = Message.Localize("CLI_LspCodeActionUseStructInitializer"),
                    Kind = CodeActionKind.Refactor,
                    Edit = WorkspaceEditFor(uri, [edit]),
                };
            }
        }

        internal static CodeAction? TryNonStructQuickFix(
            Uri uri,
            string content,
            SrcFileAst ast,
            IDiagnostic diagnostic,
            ProtocolRange range)
        {
            if (diagnostic.Code != MessageCode.CheckerStructInitializerOnNonStruct)
            {
                return null;
            }

            // The checker fills the third format argument only when `new Name() with [...]`
            // is legal. An empty suffix means that rewrite would not compile: a type alias,
            // interface, enum, trait, object shape, or abstract class.
            if (diagnostic.FormatParams.Length < 3
                || diagnostic.FormatParams[2] is not string hint
                || hint.Length == 0)
            {
                return null;
            }

            ProtocolRange diagnosticRange = PositionUtilities.ToLspRange(diagnostic);
            if (!RangesOverlap(range, diagnosticRange))
            {
                return null;
            }

            PhpBinaryOpAst? match = null;
            int matchSpan = int.MaxValue;
            foreach (PhpBinaryOpAst withExpr in EnumerateWithExpressions(ast))
            {
                if (withExpr.Left is not PhpNewAst { IsStructInitializer: true }
                    || PairListOf(withExpr.Right) is null)
                {
                    continue;
                }

                // The checker reports TYHP4412 on the whole initializer expression. An
                // initializer nested inside its list, or a neighbor on the same lines, starts
                // elsewhere and may be a valid struct.
                ProtocolRange withRange = PositionUtilities.ToLspRange(withExpr);
                if (withRange.Start is null
                    || diagnosticRange.Start is null
                    || Compare(withRange.Start, diagnosticRange.Start) != 0)
                {
                    continue;
                }

                int span = SpanLength(withExpr);
                if (span < matchSpan)
                {
                    match = withExpr;
                    matchSpan = span;
                }
            }

            if (match?.Left is not PhpNewAst initializer || PairListOf(match.Right) is not PhpArrayPairListAst pairs)
            {
                return null;
            }

            TextEdit? edit = TryWithListEdit(content, initializer, pairs);
            if (edit is null)
            {
                return null;
            }

            string written = WrittenNewHead(content, initializer);
            return new CodeAction
            {
                Title = Message.Localize("CLI_LspCodeActionUseWithList", written),
                Kind = CodeActionKind.QuickFix,
                Diagnostics = [DiagnosticsPublisher.ToLspDiagnostic(diagnostic)],
                Edit = WorkspaceEditFor(uri, [edit]),
            };
        }

        private static ObjectDeclarationSymbol? ResolveCompletionTarget(
            string content,
            int cursor,
            int openBracket,
            bool withList,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? tree,
            SymbolFinder finder,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType)
        {
            NameResolver? resolver = CreateResolver(globalScope, tree);
            if (!withList)
            {
                if (TryReadNewTarget(content, openBracket, out string? typeName, out bool anonymous))
                {
                    if (!anonymous && !string.IsNullOrEmpty(typeName))
                    {
                        ObjectDeclarationSymbol? named = ResolveTypeName(typeName, resolver, fromScope);
                        if (named is { IsStruct: true })
                        {
                            return named;
                        }

                        if (named is not null)
                        {
                            return null;
                        }
                    }
                }

                return StructFromAstBracket(content, ast, openBracket, globalScope, tree, fromScope, getInferredType);
            }

            if (!TryReadWithReceiver(content, openBracket, out string? withType, out string? variable, out bool withAnonymous))
            {
                return StructFromAstBracket(content, ast, openBracket, globalScope, tree, fromScope, getInferredType);
            }

            if (!withAnonymous && !string.IsNullOrEmpty(withType))
            {
                ObjectDeclarationSymbol? named = ResolveTypeName(withType, resolver, fromScope);
                return named is { IsStruct: true } ? named : null;
            }

            if (!string.IsNullOrEmpty(variable))
            {
                ObjectDeclarationSymbol? fromVariable = StructFromVariable(
                    content,
                    cursor,
                    variable,
                    ast,
                    globalScope,
                    tree,
                    finder,
                    fromScope,
                    getInferredType,
                    resolver);
                if (fromVariable is { IsStruct: true })
                {
                    return fromVariable;
                }
            }

            return StructFromAstBracket(content, ast, openBracket, globalScope, tree, fromScope, getInferredType);
        }

        private static ObjectDeclarationSymbol? StructFromVariable(
            string content,
            int cursor,
            string variable,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? tree,
            SymbolFinder finder,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType,
            NameResolver? resolver)
        {
            if (ast is not null)
            {
                var position = PositionUtilities.GetPosition(content, Math.Clamp(cursor, 0, content.Length));
                var (line, column) = PositionUtilities.FromLspPosition(position);
                BaseSymbol? local = finder.FindLocalVariableByName(
                    ast,
                    variable,
                    line,
                    column,
                    out IBase2Ast? inferredNode);
                ObjectDeclarationSymbol? fromDeclared = ObjectFromSymbol(local, resolver, fromScope);
                if (fromDeclared is { IsStruct: true })
                {
                    return fromDeclared;
                }

                if (inferredNode is not null)
                {
                    ObjectDeclarationSymbol? fromInferred = ObjectFromCheckedType(getInferredType(inferredNode));
                    if (fromInferred is { IsStruct: true })
                    {
                        return fromInferred;
                    }
                }
            }

            string? declared = DeclaredTypeNameInText(content, variable, cursor);
            ObjectDeclarationSymbol? fromText = ResolveTypeName(declared, resolver, fromScope);
            return fromText is { IsStruct: true } ? fromText : null;
        }

        private static ObjectDeclarationSymbol? StructFromAstBracket(
            string content,
            SrcFileAst? ast,
            int openBracket,
            GlobalScope? globalScope,
            SymbolTree? tree,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?>? getInferredType)
        {
            if (ast is null)
            {
                return null;
            }

            foreach (PhpBinaryOpAst withExpr in EnumerateWithExpressions(ast))
            {
                if (PairListOf(withExpr.Right) is not PhpArrayPairListAst list
                    || !ListOwnsBracket(content, list, openBracket))
                {
                    continue;
                }

                return ResolveReceiver(withExpr.Left, globalScope, tree, fromScope, getInferredType);
            }

            return null;
        }

        private static PhpArrayPairListAst? PairListOf(IExpression? expression)
        {
            if (expression is PhpArrayPairListAst list)
            {
                return list;
            }

            if (expression is PhpArrayAst array)
            {
                return array.ArrayPairs;
            }

            return null;
        }

        /// <summary>
        /// A user-written <c>with [</c> list spans the pairs, not the brackets.
        /// An initializer list spans the brackets themselves.
        /// </summary>
        private static bool ListOwnsBracket(string content, PhpArrayPairListAst list, int openBracket)
        {
            if (openBracket < 0 || openBracket >= content.Length || content[openBracket] != '[')
            {
                return false;
            }

            if (list.StartIndex == openBracket)
            {
                return true;
            }

            if (list.StartIndex < 0 || openBracket >= list.StartIndex)
            {
                return false;
            }

            for (int i = openBracket + 1; i < list.StartIndex && i < content.Length; i++)
            {
                if (!char.IsWhiteSpace(content[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryKeyOfStructList(
            IReadOnlyList<IBase2Ast> path,
            GlobalScope? globalScope,
            SymbolTree? tree,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?> getInferredType,
            out IBase2Ast? keyNode,
            out ObjectDeclarationSymbol? target)
        {
            keyNode = null;
            target = null;
            PhpArrayPairAst? pair = null;
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (path[i] is PhpArrayPairAst candidate
                    && candidate.KeyExpr is IBase2Ast candidateKey
                    && ReferenceEquals(path[i + 1], candidateKey))
                {
                    pair = candidate;
                    keyNode = candidateKey;
                }
            }

            if (pair is null || keyNode is null || !TryEnclosingWith(path, pair, out PhpBinaryOpAst? withExpr) || withExpr is null)
            {
                return false;
            }

            target = ResolveReceiver(withExpr.Left, globalScope, tree, fromScope, getInferredType);
            return target is { IsStruct: true };
        }

        private static bool TryEnclosingWith(
            IReadOnlyList<IBase2Ast> path,
            PhpArrayPairAst pair,
            out PhpBinaryOpAst? withExpr)
        {
            withExpr = null;
            int pairIndex = -1;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(path[i], pair))
                {
                    pairIndex = i;
                    break;
                }
            }

            if (pairIndex < 0)
            {
                return false;
            }

            // The pair belongs to a `with` only when the `with` owns the pair's own list.
            // A nested array inside an entry value, or an array on the left of `with`,
            // sits under a `with` without being its entry list.
            if (pairIndex == 0 || path[pairIndex - 1] is not PhpArrayPairListAst owningList)
            {
                return false;
            }

            for (int i = pairIndex - 1; i >= 0; i--)
            {
                if (path[i] is PhpBinaryOpAst binary
                    && IsWithOperator(binary)
                    && ReferenceEquals(PairListOf(binary.Right), owningList))
                {
                    withExpr = binary;
                    return true;
                }
            }

            return false;
        }

        private static ObjectDeclarationSymbol? ResolveReceiver(
            IExpression? expression,
            GlobalScope? globalScope,
            SymbolTree? tree,
            IBaseScope? fromScope,
            Func<IBase2Ast, ICheckedType?>? getInferredType)
        {
            if (expression is null)
            {
                return null;
            }

            NameResolver? resolver = CreateResolver(globalScope, tree);
            IBaseScope? scope = fromScope ?? globalScope;
            if (expression is PhpNewAst newExpr)
            {
                return ResolveNewDeclaration(string.Empty, newExpr, resolver, scope) is { IsStruct: true } declared
                    ? declared
                    : null;
            }

            if (expression is PhpUnaryOpAst unary && IsClone(unary) && unary.Operand is IExpression operand)
            {
                return ResolveReceiver(operand, globalScope, tree, fromScope, getInferredType);
            }

            if (expression is PhpVariableAst variable)
            {
                if (variable.BoundSymbol is VariableSymbol bound)
                {
                    ObjectDeclarationSymbol? fromDeclared = ObjectFromSymbol(bound, resolver, scope);
                    if (fromDeclared is { IsStruct: true })
                    {
                        return fromDeclared;
                    }
                }

                if (getInferredType is not null)
                {
                    ObjectDeclarationSymbol? inferred = ObjectFromCheckedType(getInferredType(variable));
                    if (inferred is { IsStruct: true })
                    {
                        return inferred;
                    }
                }
            }

            if (getInferredType is not null && expression is IBase2Ast node)
            {
                ObjectDeclarationSymbol? inferred = ObjectFromCheckedType(getInferredType(node));
                if (inferred is { IsStruct: true })
                {
                    return inferred;
                }
            }

            return null;
        }

        private static IEnumerable<StructFieldOption> EnumerateFields(
            ObjectDeclarationSymbol target,
            NameResolver? resolver,
            IBaseScope scope)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectDeclarationSymbol type in Ancestors(target, resolver, scope))
            {
                foreach (ObjectPropertySymbol property in PropertiesInOrder(type))
                {
                    string name = BareName(property.Name);
                    if (string.IsNullOrEmpty(name) || !seen.Add(name))
                    {
                        continue;
                    }

                    yield return new StructFieldOption(property, name, IsRequired(property));
                }
            }
        }

        private static ObjectPropertySymbol? FindField(
            ObjectDeclarationSymbol target,
            string name,
            NameResolver? resolver,
            IBaseScope scope)
        {
            foreach (StructFieldOption field in EnumerateFields(target, resolver, scope))
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    return field.Property;
                }
            }

            return null;
        }

        private static IEnumerable<ObjectDeclarationSymbol> Ancestors(
            ObjectDeclarationSymbol target,
            NameResolver? resolver,
            IBaseScope scope)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            ObjectDeclarationSymbol? current = target;
            int depth = 0;
            while (current is not null && depth++ < 64 && seen.Add(current))
            {
                yield return current;
                if (current.ExtendsType is null || resolver is null)
                {
                    yield break;
                }

                IBaseScope parentScope = current.ContainingScope ?? scope;
                current = resolver.ResolveType(current.ExtendsType, parentScope) as ObjectDeclarationSymbol;
            }
        }

        private static IEnumerable<ObjectPropertySymbol> PropertiesInOrder(ObjectDeclarationSymbol type)
        {
            TyhpStructPropertyListAst? list = type.DeclaringAstNode switch
            {
                TyhpStructDeclAst declaration => declaration.PropertyList,
                TyhpTypeAliasAst alias => alias.StructShape?.PropertyList,
                _ => null,
            };

            if (list is not null)
            {
                foreach (TyhpStructPropertyAst property in list.GetAllNotNull())
                {
                    string raw = property.Property?.Identifier ?? property.Identifier;
                    if (FindMember(type, BareName(raw)) is ObjectPropertySymbol symbol)
                    {
                        yield return symbol;
                    }
                }

                yield break;
            }

            foreach (IBaseSymbol member in type.Members.Values)
            {
                if (member is ObjectPropertySymbol property)
                {
                    yield return property;
                }
            }
        }

        private static ObjectPropertySymbol? FindMember(ObjectDeclarationSymbol type, string bareName)
        {
            if (string.IsNullOrEmpty(bareName))
            {
                return null;
            }

            if (type.Members.TryGetValue(bareName, out IBaseSymbol? found) && found is ObjectPropertySymbol direct)
            {
                return direct;
            }

            if (type.Members.TryGetValue("$" + bareName, out IBaseSymbol? dollar) && dollar is ObjectPropertySymbol withDollar)
            {
                return withDollar;
            }

            foreach (IBaseSymbol member in type.Members.Values)
            {
                if (member is ObjectPropertySymbol property
                    && string.Equals(BareName(property.Name), bareName, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private static bool IsRequired(ObjectPropertySymbol property)
        {
            if (property.DefaultValue is not null || property.DeclaredType is null)
            {
                return false;
            }

            return !IsNullableType(property.DeclaredType);
        }

        private static bool IsNullableType(ITypeExpression type)
        {
            if (type is PhpTypeExpressionAst expression)
            {
                if (expression.IsNullable)
                {
                    return true;
                }

                if (expression.TypeKind == PhpTypeKind.Union)
                {
                    foreach (ITypeExpression member in expression.Types?.GetAllNotNull() ?? [])
                    {
                        if (IsNullName(member))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            return IsNullName(type);
        }

        private static bool IsNullName(ITypeExpression type)
        {
            string name = FirstNonEmpty(type.Identifier, type.ValueString);
            return string.Equals(name, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStructConstruction(
            string content,
            PhpNewAst newExpr,
            NameResolver? resolver,
            IBaseScope? scope)
        {
            if (IsAnonymousNew(content, newExpr))
            {
                return true;
            }

            ObjectDeclarationSymbol? declaration = ResolveNewDeclaration(content, newExpr, resolver, scope);
            return declaration is { IsStruct: true };
        }

        private static ObjectDeclarationSymbol? ResolveNewDeclaration(
            string content,
            PhpNewAst newExpr,
            NameResolver? resolver,
            IBaseScope? scope)
        {
            if (newExpr.ClassName is IBase2Ast nameNode)
            {
                ObjectDeclarationSymbol? bound = BoundObject(nameNode);
                if (bound is not null)
                {
                    return bound;
                }

                string identifier = FirstNonEmpty(nameNode.ValueString, nameNode.Identifier);
                if (identifier.StartsWith("anonStruct@", StringComparison.Ordinal))
                {
                    ObjectDeclarationSymbol? anonymous = ResolveTypeName(identifier, resolver, scope);
                    if (anonymous is not null)
                    {
                        return anonymous;
                    }
                }

                string stripped = StripGenerics(identifier);
                ObjectDeclarationSymbol? resolved = ResolveTypeName(stripped, resolver, scope);
                if (resolved is not null)
                {
                    return resolved;
                }
            }

            if (!string.IsNullOrEmpty(content))
            {
                string? written = TypeNameFromNewHead(WrittenNewHead(content, newExpr));
                return ResolveTypeName(written, resolver, scope);
            }

            return null;
        }

        private static ObjectDeclarationSymbol? BoundObject(IBase2Ast node)
        {
            if (node.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                return bound;
            }

            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is null)
                {
                    continue;
                }

                ObjectDeclarationSymbol? found = BoundObject(child);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ObjectDeclarationSymbol? ResolveTypeName(
            string? typeName,
            NameResolver? resolver,
            IBaseScope? scope)
        {
            if (resolver is null || scope is null || string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            string trimmed = StripGenerics(typeName.Trim().TrimStart('\\'));
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            if (trimmed.Contains('\\', StringComparison.Ordinal))
            {
                string[] parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                return resolver.ResolveQualifiedName(parts) as ObjectDeclarationSymbol
                    ?? resolver.ResolveRelativeName(parts, scope) as ObjectDeclarationSymbol;
            }

            return resolver.ResolveSymbol(trimmed, scope) as ObjectDeclarationSymbol
                ?? resolver.ResolveRelativeName([trimmed], scope) as ObjectDeclarationSymbol;
        }

        private static ObjectDeclarationSymbol? ObjectFromSymbol(
            BaseSymbol? symbol,
            NameResolver? resolver,
            IBaseScope? scope)
        {
            if (symbol is ObjectDeclarationSymbol obj)
            {
                return obj;
            }

            if (symbol is VariableSymbol variable && variable.DeclaredType is ITypeExpression declared && scope is not null)
            {
                if (resolver is null)
                {
                    return declared.BoundSymbol as ObjectDeclarationSymbol;
                }

                return resolver.ResolveType(declared, scope) as ObjectDeclarationSymbol
                    ?? declared.BoundSymbol as ObjectDeclarationSymbol;
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

        private static HashSet<string> KeysAlreadyPresent(string content, int openBracket, int cursor)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            int square = 0;
            int paren = 0;
            int brace = 0;
            int entryStart = openBracket + 1;
            var scan = default(TextScan);
            int end = content.Length;
            for (int i = openBracket + 1; i < content.Length; i++)
            {
                if (Consume(content, ref i, ref scan))
                {
                    continue;
                }

                char c = content[i];
                bool top = square == 0 && paren == 0 && brace == 0;
                if (top && c == ']')
                {
                    end = i;
                    break;
                }

                if (top && c == ',')
                {
                    AddKey(keys, content, entryStart, i, cursor);
                    entryStart = i + 1;
                    continue;
                }

                switch (c)
                {
                    case '[':
                        square++;
                        break;
                    case ']':
                        square--;
                        break;
                    case '(':
                        paren++;
                        break;
                    case ')':
                        paren--;
                        break;
                    case '{':
                        brace++;
                        break;
                    case '}':
                        brace--;
                        break;
                }
            }

            if (end == content.Length || end > entryStart)
            {
                AddKey(keys, content, entryStart, end, cursor);
            }

            return keys;
        }

        private static void AddKey(HashSet<string> keys, string content, int start, int end, int cursor)
        {
            if (start >= end || start < 0 || end > content.Length)
            {
                return;
            }

            int arrow = IndexOfArrow(content, start, end);
            if (arrow < 0)
            {
                return;
            }

            if (cursor >= start && cursor <= arrow)
            {
                return;
            }

            string? name = ParseKeyText(content[start..arrow]);
            if (!string.IsNullOrEmpty(name))
            {
                keys.Add(name);
            }
        }

        private static string? ParseKeyText(string raw)
        {
            string text = raw.Trim();
            if (text.StartsWith("...", StringComparison.Ordinal))
            {
                return null;
            }

            if (text.Length >= 2
                && (text[0] == '\'' || text[0] == '"')
                && text[^1] == text[0])
            {
                text = text[1..^1];
            }

            if (text.StartsWith('$'))
            {
                text = text[1..];
            }

            if (text.Length == 0 || !IdentifierSyntax.IsIdentifierStart(text[0]))
            {
                return null;
            }

            for (int i = 1; i < text.Length; i++)
            {
                if (!IdentifierSyntax.IsIdentifierChar(text[i]))
                {
                    return null;
                }
            }

            return text;
        }

        private static string? PropertyNameFromKey(IBase2Ast key)
        {
            string raw = key switch
            {
                PhpNameAst name => FirstNonEmpty(name.ValueString, name.Identifier),
                PhpScalarAst scalar => FirstNonEmpty(scalar.ValueString, scalar.Identifier),
                PhpBuiltinTypeAst builtin => FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                TokenValueAst token => FirstNonEmpty(token.ValueString, token.Identifier),
                _ => FirstNonEmpty(key.ValueString, key.Identifier),
            };
            return ParseKeyText(raw);
        }

        private static bool IsKeySlot(string content, int openBracket, int identStart)
        {
            int square = 0;
            int paren = 0;
            int brace = 0;
            int entryStart = openBracket + 1;
            bool arrow = false;
            var scan = default(TextScan);
            for (int i = openBracket + 1; i < identStart; i++)
            {
                if (Consume(content, ref i, ref scan))
                {
                    continue;
                }

                char c = content[i];
                bool top = square == 0 && paren == 0 && brace == 0;
                if (top && c == ']')
                {
                    return false;
                }

                if (top && c == ',')
                {
                    entryStart = i + 1;
                    arrow = false;
                    continue;
                }

                if (top && c == '=' && i + 1 < identStart && content[i + 1] == '>')
                {
                    arrow = true;
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '[':
                        square++;
                        break;
                    case ']':
                        square--;
                        break;
                    case '(':
                        paren++;
                        break;
                    case ')':
                        paren--;
                        break;
                    case '{':
                        brace++;
                        break;
                    case '}':
                        brace--;
                        break;
                }
            }

            if (scan.InString || scan.InLineComment || scan.InBlockComment || arrow)
            {
                return false;
            }

            return !StartsWithSpread(content, entryStart, identStart);
        }

        private static bool TryInnermostUnclosedBracket(string content, int limit, out int openBracket)
        {
            openBracket = -1;
            var stack = new Stack<int>();
            var scan = default(TextScan);
            int end = Math.Clamp(limit, 0, content.Length);
            for (int i = 0; i < end; i++)
            {
                if (Consume(content, ref i, ref scan))
                {
                    continue;
                }

                char c = content[i];
                if (c == '[')
                {
                    stack.Push(i);
                }
                else if (c == ']' && stack.Count > 0)
                {
                    stack.Pop();
                }
            }

            if (scan.InString || scan.InLineComment || scan.InBlockComment || stack.Count == 0)
            {
                return false;
            }

            openBracket = stack.Peek();
            return true;
        }

        private static bool IsWithBracket(string content, int openBracket)
        {
            int i = SkipTriviaBack(content, openBracket);
            return i >= 0 && EndsAtKeyword(content, i, "with");
        }

        private static bool TryReadWithReceiver(
            string content,
            int openBracket,
            out string? typeName,
            out string? variableName,
            out bool anonymous)
        {
            typeName = null;
            variableName = null;
            anonymous = false;
            int i = SkipTriviaBack(content, openBracket);
            if (i < 0 || !EndsAtKeyword(content, i, "with"))
            {
                return false;
            }

            int withStart = i - "with".Length + 1;
            i = SkipTriviaBack(content, withStart);
            if (i < 0)
            {
                return false;
            }

            if (content[i] == ')')
            {
                int openParen = SkipBalancedBack(content, i, '(', ')');
                if (openParen < 0)
                {
                    return false;
                }

                string inside = content[(openParen + 1)..i];
                if (string.IsNullOrWhiteSpace(inside))
                {
                    return TryReadNewTarget(content, openParen, out typeName, out anonymous);
                }

                return TryReadTrailingVariable(inside, out variableName);
            }

            return TryReadTrailingVariable(content[..(i + 1)], out variableName);
        }

        private static bool TryReadNewTarget(string content, int anchor, out string? typeName, out bool anonymous)
        {
            typeName = null;
            anonymous = false;
            int i = SkipTriviaBack(content, anchor);
            if (i < 0)
            {
                return false;
            }

            if (content[i] == '>')
            {
                int open = SkipBalancedBack(content, i, '<', '>');
                if (open < 0)
                {
                    return false;
                }

                i = SkipTriviaBack(content, open);
                if (i < 0)
                {
                    return false;
                }
            }
            else if (content[i] == '}')
            {
                int open = SkipBalancedBack(content, i, '{', '}');
                if (open < 0)
                {
                    return false;
                }

                i = SkipTriviaBack(content, open);
                if (i < 0)
                {
                    return false;
                }

                if (!EndsAtKeyword(content, i, "struct"))
                {
                    while (i >= 0 && (IdentifierSyntax.IsIdentifierChar(content[i]) || content[i] == '\\'))
                    {
                        i--;
                    }

                    i = i >= 0 ? SkipTriviaBack(content, i + 1) : -1;
                    if (i < 0 || !EndsAtKeyword(content, i, "extends"))
                    {
                        return false;
                    }

                    i = SkipTriviaBack(content, i - "extends".Length + 1);
                    if (i < 0 || !EndsAtKeyword(content, i, "struct"))
                    {
                        return false;
                    }
                }

                anonymous = true;
                int structStart = i - "struct".Length + 1;
                i = SkipTriviaBack(content, structStart);
                return i >= 0 && EndsAtKeyword(content, i, "new");
            }

            if (!IdentifierSyntax.IsIdentifierChar(content[i]) && content[i] != '\\')
            {
                return false;
            }

            int nameEnd = i + 1;
            int nameStart = nameEnd;
            while (nameStart > 0
                && (IdentifierSyntax.IsIdentifierChar(content[nameStart - 1]) || content[nameStart - 1] == '\\'))
            {
                nameStart--;
            }

            typeName = content[nameStart..nameEnd].Trim('\\');
            i = SkipTriviaBack(content, nameStart);
            return i >= 0 && EndsAtKeyword(content, i, "new") && !string.IsNullOrEmpty(typeName);
        }

        private static bool TryReadTrailingVariable(string text, out string? name)
        {
            name = null;
            int i = text.Length - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i]))
            {
                i--;
            }

            if (i < 0 || !IdentifierSyntax.IsIdentifierChar(text[i]))
            {
                return false;
            }

            int end = i + 1;
            while (i >= 0 && IdentifierSyntax.IsIdentifierChar(text[i]))
            {
                i--;
            }

            if (i < 0 || text[i] != '$')
            {
                return false;
            }

            name = text[(i + 1)..end];
            return name.Length > 0 && IdentifierSyntax.IsIdentifierStart(name[0]);
        }

        private static string? DeclaredTypeNameInText(string content, string variable, int cursor)
        {
            string needle = "$" + variable;
            int from = 0;
            string? found = null;
            int limit = Math.Clamp(cursor, 0, content.Length);
            while (from < limit)
            {
                int at = content.IndexOf(needle, from, StringComparison.Ordinal);
                if (at < 0 || at >= limit)
                {
                    break;
                }

                int after = at + needle.Length;
                if (after < content.Length && IdentifierSyntax.IsIdentifierChar(content[after]))
                {
                    from = after;
                    continue;
                }

                int i = SkipTriviaBack(content, at);
                if (i >= 0 && (IdentifierSyntax.IsIdentifierChar(content[i]) || content[i] == '\\'))
                {
                    int nameEnd = i + 1;
                    int nameStart = nameEnd;
                    while (nameStart > 0
                        && (IdentifierSyntax.IsIdentifierChar(content[nameStart - 1]) || content[nameStart - 1] == '\\'))
                    {
                        nameStart--;
                    }

                    string candidate = content[nameStart..nameEnd].Trim('\\');
                    if (!IdentifierSyntax.IsKeyword(candidate))
                    {
                        found = candidate;
                    }
                }

                from = after;
            }

            return found;
        }

        private static bool HasEmptyArgumentList(string content, PhpNewAst newExpr)
        {
            if (newExpr.Arguments is not null)
            {
                foreach (PhpArgumentAst argument in newExpr.Arguments.GetAllNotNull())
                {
                    if (argument.Expression is not null)
                    {
                        return false;
                    }
                }
            }

            // An empty `()` is not stored as an argument list. An anonymous
            // `new struct { … } with [` has no argument list at all.
            if (TryConstructorParens(content, newExpr, out int open, out int close))
            {
                return string.IsNullOrWhiteSpace(content[(open + 1)..close]);
            }

            return IsAnonymousNew(content, newExpr);
        }

        private static bool TryConstructorParens(string content, PhpNewAst newExpr, out int open, out int close)
        {
            open = -1;
            close = -1;
            int newStart = StartOffset(content, newExpr);
            int newEnd = EndOffset(content, newExpr);
            if (newStart < 0 || newEnd < newStart || newEnd > content.Length)
            {
                return false;
            }

            int scanEnd = Math.Min(content.Length, newEnd + 48);
            int withAt = IndexOfWholeKeyword(content, "with", newEnd, scanEnd);
            int closeLimit = withAt >= 0 ? withAt : newEnd;
            if (closeLimit <= newStart)
            {
                return false;
            }

            close = content.LastIndexOf(')', closeLimit - 1);
            if (close < newStart)
            {
                return false;
            }

            open = content.LastIndexOf('(', close - 1);
            return open >= newStart;
        }

        private static int IndexOfWholeKeyword(string content, string keyword, int start, int end)
        {
            start = Math.Max(0, start);
            end = Math.Min(content.Length, end);
            while (start < end)
            {
                int at = content.IndexOf(keyword, start, end - start, StringComparison.Ordinal);
                if (at < 0)
                {
                    return -1;
                }

                int after = at + keyword.Length;
                bool leftOk = at == 0 || !IdentifierSyntax.IsIdentifierChar(content[at - 1]);
                bool rightOk = after >= content.Length || !IdentifierSyntax.IsIdentifierChar(content[after]);
                if (leftOk && rightOk)
                {
                    return at;
                }

                start = at + keyword.Length;
            }

            return -1;
        }

        private static TextEdit? TryUseInitializerEdit(string content, PhpNewAst newExpr, PhpArrayPairListAst list)
        {
            int listStart = StartOffset(content, list);
            int newStart = StartOffset(content, newExpr);
            if (listStart < 0 || newStart < 0 || listStart > content.Length)
            {
                return null;
            }

            int bracket = listStart < content.Length && content[listStart] == '['
                ? listStart
                : content.LastIndexOf('[', Math.Max(0, Math.Min(listStart, content.Length) - 1));
            if (bracket < newStart || bracket >= content.Length || content[bracket] != '[')
            {
                return null;
            }

            if (TryConstructorParens(content, newExpr, out int paren, out int close)
                && close < bracket
                && string.IsNullOrWhiteSpace(content[(paren + 1)..close])
                && content[(close + 1)..bracket].Contains("with", StringComparison.Ordinal))
            {
                return Edit(content, paren, bracket, string.Empty);
            }

            int newEnd = EndOffset(content, newExpr);
            if (newEnd < newStart || newEnd > bracket || !IsAnonymousNew(content, newExpr))
            {
                return null;
            }

            string gap = content[newEnd..bracket];
            return gap.Trim().Equals("with", StringComparison.Ordinal)
                ? Edit(content, newEnd, bracket, string.Empty)
                : null;
        }

        private static TextEdit? TryWithListEdit(string content, PhpNewAst newExpr, PhpArrayPairListAst list)
        {
            int nameEnd = EndOffset(content, newExpr);
            int listStart = StartOffset(content, list);
            if (nameEnd < 0 || listStart < nameEnd || listStart > content.Length)
            {
                return null;
            }

            if (listStart < content.Length && content[listStart] != '[')
            {
                return null;
            }

            string gap = content[nameEnd..listStart];
            if (gap.Contains('(') || gap.Contains("with", StringComparison.Ordinal))
            {
                return null;
            }

            return Edit(content, nameEnd, listStart, "() with ");
        }

        private static bool IsAnonymousNew(string content, PhpNewAst newExpr)
        {
            if (newExpr.ClassName is IBase2Ast name
                && FirstNonEmpty(name.Identifier, name.ValueString).StartsWith("anonStruct@", StringComparison.Ordinal))
            {
                return true;
            }

            string head = WrittenNewHead(content, newExpr);
            return head.StartsWith("struct", StringComparison.Ordinal)
                && (head.Length == "struct".Length || !IdentifierSyntax.IsIdentifierChar(head["struct".Length]));
        }

        private static string WrittenNewHead(string content, PhpNewAst newExpr)
        {
            int start = StartOffset(content, newExpr);
            int end = newExpr.Arguments is not null
                ? StartOffset(content, newExpr.Arguments)
                : EndOffset(content, newExpr);
            if (start < 0 || end <= start || end > content.Length)
            {
                end = EndOffset(content, newExpr);
            }

            if (start < 0 || end <= start || end > content.Length)
            {
                return string.Empty;
            }

            string text = content[start..end].Trim();
            if (text.StartsWith("new", StringComparison.Ordinal)
                && (text.Length == 3 || !IdentifierSyntax.IsIdentifierChar(text[3])))
            {
                text = text[3..].Trim();
            }

            return text;
        }

        private static string? TypeNameFromNewHead(string head)
        {
            if (string.IsNullOrWhiteSpace(head) || head.StartsWith("struct", StringComparison.Ordinal))
            {
                return null;
            }

            int i = 0;
            if (head[0] == '\\')
            {
                i++;
            }

            int start = 0;
            while (i < head.Length && (IdentifierSyntax.IsIdentifierChar(head[i]) || head[i] == '\\'))
            {
                i++;
            }

            string name = head[start..i].Trim('\\');
            return string.IsNullOrEmpty(name) ? null : name;
        }

        private static string StripGenerics(string name)
        {
            int lt = name.IndexOf('<');
            return lt >= 0 ? name[..lt].Trim() : name;
        }

        private static IEnumerable<PhpBinaryOpAst> EnumerateWithExpressions(IBase2Ast root)
        {
            var stack = new Stack<IBase2Ast>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IBase2Ast node = stack.Pop();
                if (node is PhpBinaryOpAst binary && IsWithOperator(binary))
                {
                    yield return binary;
                }

                IReadOnlyList<IBase2Ast?> children = node.AstChildren;
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    if (children[i] is IBase2Ast child)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        private static bool IsWithOperator(PhpBinaryOpAst binary)
        {
            TokenValueAst? op = binary.Operator;
            if (op is null)
            {
                return false;
            }

            if (op.TokenValue == TyhpParser.T_TYHP_WITH)
            {
                return true;
            }

            return string.Equals(op.ValueString, "with", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "with", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClone(PhpUnaryOpAst unary)
        {
            TokenValueAst? op = unary.Operator;
            if (op is null)
            {
                return false;
            }

            if (op.TokenValue == TyhpParser.T_CLONE)
            {
                return true;
            }

            return string.Equals(op.ValueString, "clone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "clone", StringComparison.OrdinalIgnoreCase);
        }

        private static int SpanLength(IBase2Ast node)
        {
            int start = node.StartIndex >= 0 ? node.StartIndex : node.Line;
            int end = node.EndIndex >= 0 ? node.EndIndex : node.EndLine;
            return Math.Max(0, end - start);
        }

        private static bool OverlapsAny(ProtocolRange range, params IBase2Ast?[] nodes)
        {
            foreach (IBase2Ast? node in nodes)
            {
                if (node is not null && RangesOverlap(range, PositionUtilities.ToLspRange(node)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RangesOverlap(ProtocolRange a, ProtocolRange b)
        {
            if (a.Start is null || a.End is null || b.Start is null || b.End is null)
            {
                return false;
            }

            if (Compare(a.Start, b.End) <= 0 && Compare(b.Start, a.End) <= 0)
            {
                return true;
            }

            return a.Start.Line <= b.End.Line && b.Start.Line <= a.End.Line;
        }

        private static int Compare(Position left, Position right)
        {
            int line = left.Line.CompareTo(right.Line);
            return line != 0 ? line : left.Character.CompareTo(right.Character);
        }

        private static TextEdit Edit(string content, int start, int end, string newText)
        {
            start = Math.Clamp(start, 0, content.Length);
            end = Math.Clamp(end, start, content.Length);
            return new TextEdit
            {
                Range = new ProtocolRange
                {
                    Start = PositionUtilities.GetPosition(content, start),
                    End = PositionUtilities.GetPosition(content, end),
                },
                NewText = newText,
            };
        }

        private static WorkspaceEdit WorkspaceEditFor(Uri uri, TextEdit[] edits)
        {
            string key = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
            return new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>(StringComparer.Ordinal)
                {
                    [key] = edits,
                },
            };
        }

        private static int StartOffset(string content, IBase2Ast node)
        {
            if (string.IsNullOrEmpty(content))
            {
                return node.StartIndex;
            }

            ProtocolRange range = PositionUtilities.ToLspRange(node);
            int fromPosition = PositionUtilities.GetOffset(content, range.Start);
            if (node.StartIndex >= 0
                && node.StartIndex <= content.Length
                && Math.Abs(node.StartIndex - fromPosition) <= 1)
            {
                return node.StartIndex;
            }

            return fromPosition;
        }

        private static int EndOffset(string content, IBase2Ast node)
        {
            if (string.IsNullOrEmpty(content))
            {
                return node.EndIndex >= 0 ? node.EndIndex + 1 : node.StartIndex;
            }

            ProtocolRange range = PositionUtilities.ToLspRange(node);
            int fromPosition = PositionUtilities.GetOffset(content, range.End);
            int fromIndex = node.EndIndex >= 0 ? node.EndIndex + 1 : -1;
            if (fromIndex >= 0 && fromIndex <= content.Length && Math.Abs(fromIndex - fromPosition) <= 1)
            {
                return fromIndex;
            }

            if (fromPosition == PositionUtilities.GetOffset(content, range.Start) && fromIndex > fromPosition)
            {
                return fromIndex;
            }

            return fromPosition;
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

        private static int IdentifierStart(string content, int offset)
        {
            int i = Math.Clamp(offset, 0, content.Length);
            while (i > 0 && IdentifierSyntax.IsIdentifierChar(content[i - 1]))
            {
                i--;
            }

            return i;
        }

        private static int SkipTriviaBack(string content, int index)
        {
            int i = index - 1;
            while (i >= 0)
            {
                char c = content[i];
                if (char.IsWhiteSpace(c))
                {
                    i--;
                    continue;
                }

                if (c == '/' && i > 0 && content[i - 1] == '*')
                {
                    i -= 2;
                    while (i > 0 && !(content[i] == '*' && content[i - 1] == '/'))
                    {
                        i--;
                    }

                    i -= 2;
                    continue;
                }

                int lineStart = 0;
                int newline = i == 0 ? -1 : content.LastIndexOf('\n', i);
                if (newline >= 0)
                {
                    lineStart = newline + 1;
                }

                int comment = -1;
                for (int j = lineStart; j < i; j++)
                {
                    if (content[j] == '#')
                    {
                        comment = j;
                        break;
                    }

                    if (content[j] == '/' && j + 1 < content.Length && content[j + 1] == '/')
                    {
                        comment = j;
                        break;
                    }
                }

                if (comment >= 0)
                {
                    i = lineStart - 1;
                    continue;
                }

                return i;
            }

            return -1;
        }

        private static int SkipBalancedBack(string content, int closeIndex, char open, char close)
        {
            int depth = 0;
            for (int i = closeIndex; i >= 0; i--)
            {
                char c = content[i];
                if (c == close)
                {
                    depth++;
                }
                else if (c == open)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static bool EndsAtKeyword(string content, int endInclusive, string keyword)
        {
            int start = endInclusive - keyword.Length + 1;
            if (start < 0 || endInclusive >= content.Length)
            {
                return false;
            }

            if (!content.AsSpan(start, keyword.Length).Equals(keyword, StringComparison.Ordinal))
            {
                return false;
            }

            return start == 0 || !IdentifierSyntax.IsIdentifierChar(content[start - 1]);
        }

        private static bool StartsWithSpread(string content, int start, int end)
        {
            int i = start;
            while (i < end && char.IsWhiteSpace(content[i]))
            {
                i++;
            }

            return end - i >= 3
                && content[i] == '.'
                && content[i + 1] == '.'
                && content[i + 2] == '.';
        }

        private static int IndexOfArrow(string content, int start, int end)
        {
            int square = 0;
            int paren = 0;
            int brace = 0;
            var scan = default(TextScan);
            for (int i = start; i < end; i++)
            {
                if (Consume(content, ref i, ref scan))
                {
                    continue;
                }

                char c = content[i];
                bool top = square == 0 && paren == 0 && brace == 0;
                if (top && c == '=' && i + 1 < end && content[i + 1] == '>')
                {
                    return i;
                }

                switch (c)
                {
                    case '[':
                        square++;
                        break;
                    case ']':
                        square--;
                        break;
                    case '(':
                        paren++;
                        break;
                    case ')':
                        paren--;
                        break;
                    case '{':
                        brace++;
                        break;
                    case '}':
                        brace--;
                        break;
                }
            }

            return -1;
        }

        private static bool Consume(string content, ref int i, ref TextScan scan)
        {
            char c = content[i];
            if (scan.InLineComment)
            {
                if (c == '\n')
                {
                    scan.InLineComment = false;
                }

                return true;
            }

            if (scan.InBlockComment)
            {
                if (c == '*' && i + 1 < content.Length && content[i + 1] == '/')
                {
                    scan.InBlockComment = false;
                    i++;
                }

                return true;
            }

            if (scan.InString)
            {
                if (c == '\\' && i + 1 < content.Length)
                {
                    i++;
                    return true;
                }

                if (c == scan.Quote)
                {
                    scan.InString = false;
                }

                return true;
            }

            if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                scan.InLineComment = true;
                i++;
                return true;
            }

            if (c == '#')
            {
                scan.InLineComment = true;
                return true;
            }

            if (c == '/' && i + 1 < content.Length && content[i + 1] == '*')
            {
                scan.InBlockComment = true;
                i++;
                return true;
            }

            if (c == '\'' || c == '"')
            {
                scan.InString = true;
                scan.Quote = c;
                return true;
            }

            return false;
        }

        private static string BareName(string name)
        {
            return name.StartsWith('$') ? name[1..] : name;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private struct TextScan
        {
            public bool InLineComment;
            public bool InBlockComment;
            public bool InString;
            public char Quote;
        }
    }
}
