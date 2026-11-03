using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private ICheckedType ResolveTemplateStringType(
            TyhpTemplateStringTypeAst templateAst,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            if (templateAst.EncapsList is null)
            {
                return new TemplateStringCheckedType(
                    TemplateStringPatternReader.CreateFromSegments([], "\"\""));
            }

            // Hole names resolve in the file that wrote the template, also when an alias body is
            // expanded from another file.
            var holeState = state;
            if (TryGetDeclaringFileResolutionScope(templateAst, out var declaringScope)
                && !SharesFileScope(GetResolutionScope(state), declaringScope))
            {
                holeState = state.Fork();
                holeState.NameResolutionScope = declaringScope;
            }

            var holeDisplays = new Dictionary<IExpression, string>(ReferenceEqualityComparer.Instance);
            var fileName = state.CurrentFileName ?? templateAst.OwningFile?.FileName ?? string.Empty;
            var pattern = TemplateStringPatternReader.TryRead(
                templateAst.EncapsList,
                holeExpr => ResolveTemplateHole(holeExpr, holeState, holeDisplays),
                templateAst,
                fileName,
                _diagnostics,
                (holeExpr, holeType) => holeDisplays.TryGetValue(holeExpr, out var display)
                    ? display
                    : holeType.DisplayName);

            return pattern is null
                ? CheckedTypes.Unresolved
                : new TemplateStringCheckedType(pattern.InlineTemplateHoles());
        }

        /// <summary>
        /// Resolves one template hole and reports TYHP4199 when it is not a supported hole: a type
        /// with a string form (string, <c>int</c>, <c>float</c>, their literals, template and
        /// symbol-name types, and unions of them) written as a name, literal, or union. A hole
        /// never resolves to <c>unresolved</c> without an error reported inside it.
        /// </summary>
        private ICheckedType ResolveTemplateHole(
            IExpression holeExpr,
            CheckerState state,
            Dictionary<IExpression, string> holeDisplays)
        {
            var type = TemplateStringHoleResolver.Resolve(
                holeExpr,
                state,
                ResolveTypeExpressionCore,
                name => ResolveTemplateHoleName(name, state, display =>
                {
                    if (holeExpr is TokenValueAst)
                    {
                        holeDisplays[holeExpr] = display;
                    }
                }),
                unsupported => ReportUnsupportedTemplateHole(
                    unsupported, state, DescribeTemplateHoleExpression(unsupported)));

            if (ContainsUnresolved(type))
            {
                if (!HasErrorWithin(holeExpr, state))
                {
                    ReportUnsupportedTemplateHole(holeExpr, state, DescribeTemplateHoleExpression(holeExpr));
                }

                return CheckedTypes.Unresolved;
            }

            if (FindHoleTypeWithoutStringForm(type, depth: 0) is { } unsupportedType)
            {
                ReportUnsupportedTemplateHole(holeExpr, state, $"`{unsupportedType.DisplayName}`");
                return CheckedTypes.Unresolved;
            }

            return type;
        }

        private void ReportUnsupportedTemplateHole(IBase2Ast node, CheckerState state, string found) =>
            CheckerHelpers.ReportError(
                _diagnostics, state, node, MessageCode.CheckerTemplateStringUnsupportedHole, found);

        private static string DescribeTemplateHoleExpression(IExpression holeExpr) =>
            holeExpr switch
            {
                PhpVariableAst => "a variable",
                PhpDereferenceableAst { Suffix: PhpCallAst } => "a call",
                PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst } => "a class constant",
                PhpEncapsListAst => "an inline template",
                _ => "an expression",
            };

        private static bool ContainsUnresolved(ICheckedType type) =>
            type is UnresolvedCheckedType
            || (type is UnionCheckedType union && union.Members.Any(ContainsUnresolved));

        private bool HasErrorWithin(IBase2Ast node, CheckerState state)
        {
            var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, node);
            var endLine = node.EndLine >= 0 ? node.EndLine : node.Line;
            var endColumn = node.EndColumn >= 0 ? node.EndColumn : int.MaxValue;
            return _diagnostics.Errors.Any(e =>
                string.Equals(e.FileName, fileName, StringComparison.Ordinal)
                && (e.Line > node.Line || (e.Line == node.Line && e.Column >= node.Column))
                && (e.Line < endLine || (e.Line == endLine && e.Column <= endColumn)));
        }

        /// <summary>
        /// The first member of <paramref name="type"/> with no string form a hole can match, or
        /// null. A type parameter qualifies through its constraint; an unconstrained one is checked
        /// where it is instantiated.
        /// </summary>
        private ICheckedType? FindHoleTypeWithoutStringForm(ICheckedType type, int depth)
        {
            switch (type)
            {
                case UnionCheckedType union:
                    return union.Members
                        .Select(member => FindHoleTypeWithoutStringForm(member, depth))
                        .FirstOrDefault(found => found is not null);
                case TemplateStringCheckedType:
                    return null;
                case GenericCheckedType when !SymbolNameTypeHelper.IsSymbolNameType(type):
                    return type;
                case LiteralCheckedType literal:
                    return literal.Value is string || TemplateStringMatcher.TryFormatNumericLiteral(literal, out _)
                        ? null
                        : type;
                case SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol parameter }:
                    return parameter.ResolvedConstraint is { } constraint
                        && (depth >= 16 || FindHoleTypeWithoutStringForm(constraint, depth + 1) is not null)
                        ? type
                        : null;
            }

            if (type.IsMixed
                || type.IsNullable
                || CheckerHelpers.IsBuiltInName(type, "never")
                || CheckerHelpers.IsBuiltInName(type, "void"))
            {
                return type;
            }

            return TypeComparer.IsAssignableTo(
                type,
                CheckedTypes.UnionTypes([CheckedTypes.String, CheckedTypes.Int, CheckedTypes.Float]),
                _symbolTree,
                _globalScope)
                ? null
                : type;
        }

        /// <summary>
        /// Resolves a type name written in a template hole. Builtins, aliases (file-level,
        /// class-level, imported, qualified, generic), classes, enums, and in-scope generic
        /// parameters all resolve; an alias is expanded to its body so the matcher sees the
        /// template, literal, or union it names. An unknown name reports TYHP3003.
        /// </summary>
        private ICheckedType ResolveTemplateHoleName(
            PhpNameAst name,
            CheckerState state,
            Action<string> setDisplay)
        {
            var written = name.ValueString ?? string.Empty;
            if (written.Length == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var typeArgs = GetTemplateHoleTypeArguments(name);
            ICheckedType resolved;
            if (name.BoundSymbol is (TypeAliasSymbol or ObjectTypeAliasSymbol or ObjectDeclarationSymbol) and var bound)
            {
                // Bound by the binder from the template's declaring scope (imports, namespace,
                // enclosing class).
                resolved = typeArgs is null
                    ? ApplyDefaultsForBareGenericReference(bound, name, state)
                    : GenericTypeArgumentValidator.ValidateInstantiation(
                        CheckedTypes.FromSymbol(bound),
                        ResolveGenericTypeArgumentList(typeArgs, state, isReturnTypePosition: false, isUserTypeDeclaration: true),
                        name,
                        state,
                        _symbolTree,
                        _globalScope,
                        _diagnostics,
                        ResolveTypeExpressionCore);
            }
            else
            {
                ITypeExpression typeAst;
                if (!written.Contains('\\') && IsBuiltinTemplateHoleName(written))
                {
                    var builtin = PhpBuiltinTypeAst.Create(written, name);
                    builtin.AddGrammarAddon("typeName", typeArgs);
                    typeAst = builtin;
                }
                else
                {
                    var named = PhpNamedTypeAst.WrapClassName(PhpNameAst.CreateFromContext(written, name), name);
                    named.AddGrammarAddon("typeName", typeArgs);
                    typeAst = named;
                }

                resolved = ResolveTypeExpressionCore(typeAst, state, isReturnTypePosition: false, isUserTypeDeclaration: true);
            }

            if (resolved is UnresolvedCheckedType)
            {
                var display = written.TrimStart('\\');
                CheckerHelpers.ReportErrorWithDidYouMean(
                    _diagnostics,
                    state,
                    name,
                    MessageCode.BinderSymbolNotFound,
                    display,
                    InScopeNameCandidates.CollectTypeNames(GetResolutionScope(state)),
                    display);
                setDisplay(written);
                return resolved;
            }

            setDisplay(typeArgs is null ? written : resolved.DisplayName);
            return ExpandTemplateHoleAlias(resolved, name, state);
        }

        [ThreadStatic]
        private static HashSet<IBaseSymbol>? t_expandingTemplateHoleAliases;

        private ICheckedType ExpandTemplateHoleAlias(ICheckedType resolved, PhpNameAst name, CheckerState state)
        {
            var alias = resolved switch
            {
                SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol or ObjectTypeAliasSymbol } simple => simple.ResolvedSymbol,
                GenericCheckedType { BaseType: SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol or ObjectTypeAliasSymbol } simple } => simple.ResolvedSymbol,
                _ => null,
            };
            if (alias is null)
            {
                return resolved;
            }

            t_expandingTemplateHoleAliases ??= new HashSet<IBaseSymbol>(ReferenceEqualityComparer.Instance);
            if (!t_expandingTemplateHoleAliases.Add(alias))
            {
                CheckerHelpers.ReportError(_diagnostics, state, name, MessageCode.BinderCircularTypeAlias, alias.Name);
                return CheckedTypes.Unresolved;
            }

            try
            {
                return TypeComparer.ExpandTypeAliases(
                    resolved,
                    _symbolTree,
                    _globalScope,
                    (body, bodyAlias) => ResolveTypeExpressionCore(
                        body,
                        CheckerHelpers.WithAliasBodyContext(state, bodyAlias),
                        isReturnTypePosition: false,
                        isUserTypeDeclaration: true));
            }
            finally
            {
                t_expandingTemplateHoleAliases.Remove(alias);
            }
        }

        private bool IsBuiltinTemplateHoleName(string name) =>
            ((Binder.Scopes.Interfaces.IBaseScope)_globalScope).FindChildSymbolByName(name)
                is BuiltInTypeSymbol or BuiltInUtilityTypeSymbol
            || StaticValueTypeHelper.TryGetUnderlyingBuiltinName(name, out _)
            || TryGetBuiltinSingleton(name, out _);

        private static PhpTypeExpressionListAst? GetTemplateHoleTypeArguments(PhpNameAst name)
        {
            foreach (var key in (string[])["identifier", "typeName"])
            {
                if (name.AstGrammarAddons.TryGetValue(key, out var addon)
                    && addon is PhpTypeExpressionListAst list
                    && list.GetAllNotNull().Any())
                {
                    return list;
                }
            }

            return null;
        }

        /// <summary>
        /// Types a double-quoted interpolation as <c>string</c> carrying its literal text and hole
        /// types, so it is assignable to a template string type whose pattern it fits.
        /// </summary>
        private ICheckedType InferInterpolation(PhpEncapsListAst encapsList, CheckerState state)
        {
            if (encapsList.StringType is not (PhpStringType.DoubleQuoted or PhpStringType.BinaryDoubleQuoted))
            {
                return CheckedTypes.String;
            }

            var segments = new List<TemplateStringSegment>();
            var display = new System.Text.StringBuilder("\"");
            foreach (var part in encapsList.GetAllNotNull())
            {
                switch (part)
                {
                    case PhpEncapsStringAst chunk:
                        if (!PhpStringLiteralHelper.TryDecodeQuotedTokenText($"\"{chunk.ValueString}\"", out var text))
                        {
                            return CheckedTypes.String;
                        }

                        if (text.Length > 0)
                        {
                            segments.Add(new TemplateStringSegment.LiteralSegment(text));
                            display.Append(text);
                        }

                        break;
                    case IExpression hole:
                        var holeType = InferInterpolationHoleType(hole, state);
                        segments.Add(new TemplateStringSegment.HoleSegment(holeType, TemplateStringQuantifier.ExactlyOnce));
                        display.Append("${").Append(holeType.DisplayName).Append('}');
                        break;
                    default:
                        return CheckedTypes.String;
                }
            }

            display.Append('"');
            return new SimpleCheckedType(new BuiltInTypeSymbol("string"))
            {
                InterpolationPattern = TemplateStringPatternReader.CreateFromSegments(segments, display.ToString()),
            };
        }

        private ICheckedType InferInterpolationHoleType(IExpression hole, CheckerState state) =>
            hole is PhpNameAst ? CheckedTypes.String : InterpolationHoleTypeOf(InferExpressionType(hole, state));

        /// <summary>
        /// A hole keeps its own type when that type is a string, <c>int</c>, or <c>float</c> type
        /// (including string literals, template and symbol-name types). An int or float literal
        /// becomes its <c>(string)</c> text; any other value converts to some <c>string</c>.
        /// <c>null</c> interpolates as <c>''</c>, so a possibly-null value is its non-null hole type
        /// or <c>''</c> and fits only holes that also accept empty text.
        /// </summary>
        private ICheckedType InterpolationHoleTypeOf(ICheckedType type)
        {
            if (type is LiteralCheckedType numeric
                && TemplateStringMatcher.TryFormatNumericLiteral(numeric, out var numericText))
            {
                return new LiteralCheckedType(numericText, new SimpleCheckedType(new BuiltInTypeSymbol("string")));
            }

            if (type is UnresolvedCheckedType || type.IsMixed)
            {
                return CheckedTypes.String;
            }

            if (type.IsNullable)
            {
                var emptyText = new LiteralCheckedType(string.Empty, new SimpleCheckedType(new BuiltInTypeSymbol("string")));
                if (CheckerHelpers.IsNullType(type))
                {
                    return emptyText;
                }

                var nonNull = CheckerHelpers.WithoutNull(type);
                return nonNull.IsNullable
                    ? CheckedTypes.String
                    : CheckedTypes.UnionTypes([InterpolationHoleTypeOf(nonNull), emptyText]);
            }

            return TypeComparer.IsAssignableTo(type, CheckedTypes.String, _symbolTree, _globalScope)
                || TypeComparer.IsAssignableTo(
                    type,
                    CheckedTypes.UnionTypes([CheckedTypes.String, CheckedTypes.Int, CheckedTypes.Float]),
                    _symbolTree,
                    _globalScope)
                ? type
                : CheckedTypes.String;
        }
    }
}
