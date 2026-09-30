using System.Text;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Spells a Tyhp expression as tyhpdef mapping source: authored shape after library name
    /// resolution, with <c>$this</c> / parameters as the mapping's receiver and parameters.
    /// Fails (does not copy) when the expression depends on a library-private helper.
    /// </summary>
    internal sealed class TyhpdefExpressionSpeller
    {
        private readonly Func<ITypeExpression?, string?> _spellType;
        private readonly NameResolver? _resolver;
        private IBaseScope? _fromScope;
        private bool _privateHelper;

        public TyhpdefExpressionSpeller(
            Func<ITypeExpression?, string?> spellType,
            NameResolver? resolver = null)
        {
            this._spellType = spellType ?? throw new ArgumentNullException(nameof(spellType));
            this._resolver = resolver;
        }

        public bool TrySpell(IExpression? expression, out string text)
            => this.TrySpell(expression, fromScope: null, out text);

        public bool TrySpell(IExpression? expression, IBaseScope? fromScope, out string text)
        {
            this._privateHelper = false;
            this._fromScope = fromScope;
            text = "";
            if (expression is null)
            {
                return false;
            }

            var spelled = this.Spell(expression);
            if (this._privateHelper || string.IsNullOrWhiteSpace(spelled))
            {
                return false;
            }

            text = SanitizeThisAlias(spelled);
            if (text.Contains(GeneratedNames.ExtensionReceiverThisAlias, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private string? Spell(IExpression? expression)
        {
            if (expression is null || this._privateHelper)
            {
                return null;
            }

            this.NotePrivateHelper(expression.BoundSymbol);

            return expression switch
            {
                PhpBinaryOpAst binary => this.SpellBinary(binary),
                PhpUnaryOpAst unary => this.SpellUnary(unary),
                PhpTernaryOpAst ternary => this.SpellTernary(ternary),
                PhpVariableAst variable => this.SpellVariable(variable),
                PhpScalarAst scalar => SpellScalar(scalar),
                PhpStringAst str => this.SpellString(str),
                PhpEncapsStringAst encaps => SpellEncapsString(encaps),
                PhpEncapsListAst encapsList => this.SpellEncapsList(encapsList),
                TyhpGenericIdentifierAst generic => this.SpellGenericName(generic),
                PhpNameAst name => this.SpellName(name),
                PhpMagicConstantAst magic => magic.ValueString ?? "",
                PhpArrayAst array => this.SpellArray(array),
                PhpArrayPairListAst pairs => this.SpellArrayPairList(pairs),
                PhpNewAst newExpr => this.SpellNew(newExpr),
                PhpInlineFunctionAst inlineFn => this.SpellInlineFunction(inlineFn),
                PhpYieldAst yield => this.SpellYield(yield),
                PhpConditionalAst conditional => this.SpellMatch(conditional),
                PhpDereferenceableAst dereferenceable => this.SpellDereferenceable(dereferenceable),
                PhpDereferenceableExpressionAst paren => this.SpellParens(paren),
                PhpIssetStatementAst isset => this.SpellCallLike("isset", isset.Variables),
                PhpEmptyStatementAst empty => empty.Expression == null
                    ? "empty()"
                    : "empty(" + this.Spell(empty.Expression) + ")",
                TyhpNameofAst nameof => "nameof(" + this.Spell(nameof.Expression) + ")",
                TyhpTypeofAst typeofExpr => "typeof(" + (this._spellType(typeofExpr.TypeExpression) ?? "mixed") + ")",
                TyhpDefaultAst defaultExpr => "default(" + (this._spellType(defaultExpr.TypeExpression) ?? "mixed") + ")",
                TyhpVariableExistsAst variableExists => "variable_exists(" + this.Spell(variableExists.Expression) + ")",
                TyhpTypedVarExprAst typedVar => this.SpellTypedVar(typedVar),
                PhpExpressionListAst list => this.JoinExpressions(list.GetAllNotNull(), ", "),
                PhpCallAst call => "(" + this.SpellArgumentList(call.Arguments) + ")",
                EmittedPhpExprAst emitted => SanitizeThisAlias(emitted.PhpText),
                _ => FallbackToken(expression),
            };
        }

        private string? SpellBinary(PhpBinaryOpAst binary)
        {
            var op = binary.Operator?.ValueString ?? "";
            var left = this.ParenthesizeIfNested(binary.Left);
            var right = this.ParenthesizeIfNested(binary.Right);
            if (left is null || right is null)
            {
                return null;
            }

            return string.IsNullOrEmpty(op) ? left + " " + right : left + " " + op + " " + right;
        }

        private string? SpellUnary(PhpUnaryOpAst unary)
        {
            var op = unary.Operator?.ValueString ?? "";
            var operand = this.Spell(unary.Operand);
            if (operand is null)
            {
                return null;
            }

            if (unary.IsPrefix)
            {
                if (op.Length > 0 && (char.IsLetter(op[^1]) || op.EndsWith(')')))
                {
                    return op + " " + operand;
                }

                return op + operand;
            }

            return operand + op;
        }

        private string? SpellTernary(PhpTernaryOpAst ternary)
        {
            var condition = this.ParenthesizeIfNested(ternary.Condition);
            var falseExpr = this.ParenthesizeIfNested(ternary.FalseExpr);
            if (condition is null || falseExpr is null)
            {
                return null;
            }

            if (ternary.TrueExpr is null)
            {
                return condition + " ?: " + falseExpr;
            }

            var trueExpr = this.ParenthesizeIfNested(ternary.TrueExpr);
            return trueExpr is null ? null : condition + " ? " + trueExpr + " : " + falseExpr;
        }

        private string SpellVariable(PhpVariableAst variable)
        {
            if (variable.VariableToken != null)
            {
                var text = variable.VariableToken.ValueString ?? "";
                if (string.Equals(text.TrimStart('$'), "this_", StringComparison.OrdinalIgnoreCase))
                {
                    return "$this";
                }

                return text;
            }

            if (variable.VariableExpression is PhpVariableAst inner)
            {
                return this.SpellVariable(inner);
            }

            if (variable.VariableExpression != null)
            {
                var innerText = this.Spell(variable.VariableExpression) ?? "";
                return innerText.StartsWith('$') ? innerText : "$" + innerText;
            }

            var named = CheckerHelpers.GetVariableName(variable);
            if (string.IsNullOrEmpty(named))
            {
                return "";
            }

            return string.Equals(named, "this_", StringComparison.OrdinalIgnoreCase) ? "$this" : "$" + named;
        }

        private static string SpellScalar(PhpScalarAst scalar)
        {
            var token = scalar.AstChildren.ElementAtOrDefault(0) as TokenValueAst;
            if (!string.IsNullOrEmpty(token?.ValueString))
            {
                return token!.ValueString!;
            }

            return scalar.ScalarType switch
            {
                PhpScalarType.Integer or PhpScalarType.OctalNumber or PhpScalarType.HexNumber or PhpScalarType.BinaryNumber
                    => scalar.ValueInt64?.ToString() ?? "0",
                PhpScalarType.Float => scalar.ValueDecimal?.ToString() ?? "0.0",
                PhpScalarType.String => FormatStringLiteral(scalar.ValueString),
                _ => scalar.ValueString ?? "",
            };
        }

        private string? SpellString(PhpStringAst str)
        {
            if (str.Parts is null)
            {
                return "''";
            }

            var partList = str.Parts.GetAllNotNull().ToList();
            if (str.StringType == PhpStringType.SingleQuoted
                && partList.Count == 1
                && partList[0] is PhpEncapsStringAst encaps)
            {
                return SpellEncapsString(encaps);
            }

            var quote = str.StringType == PhpStringType.SingleQuoted ? "'" : "\"";
            var parts = new List<string>();
            foreach (var part in partList)
            {
                var spelled = this.Spell(part);
                if (spelled is null)
                {
                    return null;
                }

                parts.Add(spelled);
            }

            return quote + string.Join("", parts) + quote;
        }

        private static string SpellEncapsString(PhpEncapsStringAst encaps)
        {
            var tokenText = encaps.TokenValue?.ValueString;
            if (!string.IsNullOrEmpty(tokenText)
                && (tokenText!.StartsWith('\'') || tokenText.StartsWith('"')))
            {
                return tokenText;
            }

            return FormatStringLiteral(tokenText ?? encaps.ValueString);
        }

        private string? SpellEncapsList(PhpEncapsListAst encapsList)
        {
            var partList = encapsList.GetAllNotNull().ToList();
            if (encapsList.StringType == PhpStringType.SingleQuoted
                && partList.Count == 1
                && partList[0] is PhpEncapsStringAst encaps)
            {
                return SpellEncapsString(encaps);
            }

            var quote = encapsList.StringType == PhpStringType.SingleQuoted ? "'" : "\"";
            var sb = new StringBuilder();
            sb.Append(quote);
            foreach (var part in partList)
            {
                switch (part)
                {
                    case PhpEncapsStringAst encapsPart:
                        sb.Append(encapsPart.TokenValue?.ValueString ?? encapsPart.ValueString ?? "");
                        break;
                    case IExpression expr:
                        var spelled = this.Spell(expr);
                        if (spelled is null)
                        {
                            return null;
                        }

                        sb.Append(spelled);
                        break;
                    default:
                        sb.Append(part.ValueString ?? "");
                        break;
                }
            }

            sb.Append(quote);
            return sb.ToString();
        }

        private string? SpellArray(PhpArrayAst array)
        {
            var inner = this.SpellArrayPairs(array.ArrayPairs);
            if (inner is null)
            {
                return null;
            }

            return array.IsShortSyntax ? "[" + inner + "]" : "array(" + inner + ")";
        }

        private string? SpellArrayPairList(PhpArrayPairListAst list)
        {
            var inner = this.SpellArrayPairs(list);
            return inner is null ? null : "[" + inner + "]";
        }

        private string? SpellArrayPairs(PhpArrayPairListAst? list)
        {
            if (list is null)
            {
                return "";
            }

            var parts = new List<string>();
            foreach (var pair in list.GetAllNotNull())
            {
                var spelled = this.SpellArrayPair(pair);
                if (spelled is null)
                {
                    return null;
                }

                parts.Add(spelled);
            }

            return string.Join(", ", parts);
        }

        private string? SpellArrayPair(PhpArrayPairAst pair)
        {
            var value = this.Spell(pair.ValueExpr);
            if (value is null)
            {
                return null;
            }

            if (pair.IsExpansion)
            {
                return "..." + value;
            }

            if (pair.KeyExpr != null)
            {
                var key = this.Spell(pair.KeyExpr);
                return key is null ? null : key + " => " + value;
            }

            return value;
        }

        private string? SpellNew(PhpNewAst newExpr)
        {
            if (newExpr.AnonymousClass != null)
            {
                return null;
            }

            var args = this.SpellArgumentList(newExpr.Arguments);
            var className = newExpr.ClassName switch
            {
                TyhpGenericIdentifierAst generic => this.SpellGenericName(generic),
                PhpNameAst name => this.SpellName(name),
                IExpression expr => this.Spell(expr),
                _ => newExpr.ClassName?.Identifier,
            };
            if (string.IsNullOrWhiteSpace(className))
            {
                return null;
            }

            return "new " + className + "(" + args + ")";
        }

        private string? SpellInlineFunction(PhpInlineFunctionAst inlineFn)
        {
            if (!inlineFn.IsArrowFunction)
            {
                return null;
            }

            var parameters = this.SpellParameterList(inlineFn.Parameters);
            if (parameters is null)
            {
                return null;
            }

            var returnType = this._spellType(inlineFn.ReturnType);
            var returnSuffix = string.IsNullOrWhiteSpace(returnType) ? "" : ": " + returnType;
            var refPrefix = inlineFn.ReturnsRef ? "&" : "";
            if (!TyhpdefExtensionBody.TryGetSingleReturnExpression(inlineFn, out var body))
            {
                return null;
            }

            var bodyText = this.Spell(body);
            return bodyText is null ? null : "fn" + refPrefix + "(" + parameters + ")" + returnSuffix + " => " + bodyText;
        }

        private string? SpellParameterList(PhpParameterListAst? parameters)
        {
            if (parameters is null)
            {
                return "";
            }

            var parts = new List<string>();
            foreach (var param in parameters.GetAllNotNull())
            {
                var sb = new StringBuilder();
                var type = this._spellType(param.Type);
                if (!string.IsNullOrWhiteSpace(type))
                {
                    sb.Append(type);
                    sb.Append(' ');
                }

                if (param.IsRef)
                {
                    sb.Append('&');
                }

                if (param.IsVariadic)
                {
                    sb.Append("...");
                }

                var name = param.Name.Trim();
                sb.Append(name.StartsWith('$') ? name : "$" + name);
                if (param.DefaultValue != null)
                {
                    var defaultValue = this.Spell(param.DefaultValue);
                    if (defaultValue is null)
                    {
                        return null;
                    }

                    sb.Append(" = ");
                    sb.Append(defaultValue);
                }

                parts.Add(sb.ToString());
            }

            return string.Join(", ", parts);
        }

        private string? SpellYield(PhpYieldAst yield)
        {
            var value = this.Spell(yield.ValueExpr);
            if (value is null)
            {
                return yield.ValueExpr is null ? "yield" : null;
            }

            if (yield.KeyExpr != null)
            {
                var key = this.Spell(yield.KeyExpr);
                return key is null ? null : "yield " + key + " => " + value;
            }

            return "yield " + value;
        }

        private string? SpellMatch(PhpConditionalAst conditional)
        {
            if (!conditional.IsMatchSyntax)
            {
                return null;
            }

            var expr = this.Spell(conditional.Expression);
            if (expr is null)
            {
                return null;
            }

            var arms = new List<string>();
            foreach (var arm in conditional.Arms?.GetAllNotNull() ?? [])
            {
                var spelled = this.SpellMatchArm(arm);
                if (spelled is null)
                {
                    return null;
                }

                arms.Add(spelled);
            }

            return "match (" + expr + ") { " + string.Join(", ", arms) + " }";
        }

        private string? SpellMatchArm(PhpConditionalArmAst arm)
        {
            var body = this.SpellMatchArmBody(arm.Body);
            if (body is null)
            {
                return null;
            }

            if (arm.IsDefault)
            {
                return "default => " + body;
            }

            var conditions = this.JoinExpressions(arm.Conditions?.GetAllNotNull() ?? [], ", ");
            return conditions is null ? null : conditions + " => " + body;
        }

        private string? SpellMatchArmBody(PhpStatementBlockAst? body)
        {
            if (body is null)
            {
                return "null";
            }

            if (TyhpdefExtensionBody.TryGetSingleReturnExpression(body, out var expr))
            {
                return this.Spell(expr);
            }

            var stmts = body.GetAllNotNull()
                .Where(s => s is not PhpEmptyStatementAst and not PhpNopStatementAst)
                .ToList();
            if (stmts.Count == 1 && stmts[0] is IExpression expression)
            {
                if (expression is PhpUnaryOpAst unary
                    && string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase))
                {
                    return this.Spell(unary.Operand);
                }

                return this.Spell(expression);
            }

            return null;
        }

        private string? SpellDereferenceable(PhpDereferenceableAst dereferenceable)
        {
            var baseText = this.SpellDereferenceableBase(dereferenceable.Base);
            var suffix = this.SpellDereferenceableSuffix(dereferenceable.Suffix);
            if (baseText is null || suffix is null)
            {
                return null;
            }

            return baseText + suffix;
        }

        private string? SpellDereferenceableBase(IDereferenceableBase? baseExpr) =>
            baseExpr switch
            {
                null => "",
                PhpDereferenceableAst chain => this.SpellDereferenceable(chain),
                PhpDereferenceableExpressionAst paren => "(" + this.Spell(paren.Expression) + ")",
                PhpVariableAst variable => this.SpellVariable(variable),
                TyhpGenericIdentifierAst generic => this.SpellGenericName(generic),
                PhpNameAst name => this.SpellName(name),
                PhpMagicConstantAst magic => magic.ValueString ?? "",
                PhpNewAst newExpr => this.SpellNew(newExpr),
                PhpArrayPairListAst arrayList => this.SpellArrayPairList(arrayList),
                PhpEncapsStringAst encaps => SpellEncapsString(encaps),
                PhpEncapsListAst encapsList => this.SpellEncapsList(encapsList),
                IExpression expr => this.Spell(expr),
            };

        private string? SpellDereferenceableSuffix(IDereferenceableSuffix? suffix) =>
            suffix switch
            {
                null => "",
                PhpInstanceMemberAccessAst instance => this.SpellInstanceMember(instance),
                PhpStaticMemberAccessAst staticAccess => this.SpellAccessorMember("::", staticAccess.Member),
                PhpClassConstantAccessAst constant => this.SpellAccessorMember("::", constant.Member),
                PhpArrayAccessAst arrayAccess => arrayAccess.IndexExpression is null
                    ? "[]"
                    : "[" + this.Spell(arrayAccess.IndexExpression) + "]",
                PhpCallAst call => "(" + this.SpellArgumentList(call.Arguments) + ")",
                PhpMemberAccessAst memberAccess => this.SpellMemberAccess(memberAccess),
                PhpArgumentListAst argList => "(" + this.SpellArgumentList(argList) + ")",
                _ => null,
            };

        private string? SpellInstanceMember(PhpInstanceMemberAccessAst instance)
        {
            var accessor = instance.Accessor?.ValueString ?? "->";
            return this.SpellAccessorMember(accessor, instance.MemberName);
        }

        /// <summary>
        /// Member names after <c>::</c> / <c>-&gt;</c> stay as written. <see cref="SpellName"/>
        /// would resolve a colliding PHP builtin (<c>strlen</c> → <c>\strlen</c>) and emit
        /// <c>Helper::\strlen($this)</c>, which is not valid tyhpdef.
        /// </summary>
        private string? SpellAccessorMember(string accessor, IExpression? member)
        {
            if (member is PhpNameAst name)
            {
                this.NotePrivateHelper(name.BoundSymbol);
                return accessor + (name.ValueString ?? name.Identifier ?? "");
            }

            var spelled = this.Spell(member);
            return spelled is null ? null : accessor + spelled;
        }

        private string? SpellMemberAccess(PhpMemberAccessAst memberAccess)
        {
            var accessor = memberAccess.Accessor switch
            {
                TokenValueAst token => token.ValueString ?? "->",
                _ => memberAccess.Accessor?.Identifier ?? "->",
            };

            var key = this.Spell(memberAccess.Key);
            if (key is null)
            {
                return null;
            }

            if (memberAccess.Target != null)
            {
                var target = this.Spell(memberAccess.Target);
                return target is null ? null : target + accessor + key;
            }

            return accessor + key;
        }

        private string? SpellParens(PhpDereferenceableExpressionAst paren)
        {
            var inner = this.Spell(paren.Expression);
            if (inner is null)
            {
                return null;
            }

            return paren.Expression is PhpBinaryOpAst or PhpTernaryOpAst ? "(" + inner + ")" : inner;
        }

        private string? SpellTypedVar(TyhpTypedVarExprAst typedVar)
        {
            var type = this._spellType(typedVar.TypeExpression) ?? "";
            var variable = typedVar.Variable is null ? "" : this.SpellVariable(typedVar.Variable);
            var prefix = typedVar.IsParenthesized ? "(" + type + ") " : type + " ";
            if (typedVar.AssignedExpression is null)
            {
                return prefix + variable;
            }

            var assigned = this.Spell(typedVar.AssignedExpression);
            return assigned is null ? null : prefix + variable + " = " + (typedVar.IsRef ? "&" : "") + assigned;
        }

        private string? SpellCallLike(string name, PhpExpressionListAst? arguments)
        {
            var args = this.JoinExpressions(arguments?.GetAllNotNull() ?? [], ", ");
            return args is null ? null : name + "(" + args + ")";
        }

        private string SpellArgumentList(PhpArgumentListAst? arguments)
        {
            if (arguments is null)
            {
                return "";
            }

            var parts = new List<string>();
            foreach (var argument in arguments.GetAllNotNull())
            {
                var spelled = this.SpellArgument(argument);
                if (spelled is null)
                {
                    this._privateHelper = true;
                    return "";
                }

                parts.Add(spelled);
            }

            return string.Join(", ", parts);
        }

        private string? SpellArgument(PhpArgumentAst argument)
        {
            var expr = this.Spell(argument.Expression);
            if (expr is null)
            {
                return argument.Expression is null ? "" : null;
            }

            if (argument.IsVariadic)
            {
                return "..." + expr;
            }

            if (argument.Name != null)
            {
                var name = argument.Name.ValueString ?? "";
                return name + ": " + expr;
            }

            return expr;
        }

        private string? JoinExpressions(IEnumerable<IExpression> expressions, string separator)
        {
            var parts = new List<string>();
            foreach (var expression in expressions)
            {
                var spelled = this.Spell(expression);
                if (spelled is null)
                {
                    return null;
                }

                parts.Add(spelled);
            }

            return string.Join(separator, parts);
        }

        private string? ParenthesizeIfNested(IExpression? expression)
        {
            var spelled = this.Spell(expression);
            if (spelled is null)
            {
                return null;
            }

            return expression is PhpBinaryOpAst or PhpTernaryOpAst ? "(" + spelled + ")" : spelled;
        }

        private string SpellGenericName(TyhpGenericIdentifierAst generic)
        {
            var name = this.SpellName(generic);
            if (generic.GenericArguments is TyhpGenericsTypeArgumentListAst list)
            {
                var args = new List<string>();
                foreach (var arg in list.GetAllNotNull())
                {
                    var spelled = this._spellType(arg.TypeConstraint)
                        ?? this._spellType(arg.DefaultType)
                        ?? arg.Name?.ValueString
                        ?? arg.Identifier;
                    if (!string.IsNullOrWhiteSpace(spelled))
                    {
                        args.Add(spelled);
                    }
                }

                if (args.Count > 0)
                {
                    return name + "<" + string.Join(", ", args) + ">";
                }
            }

            return name;
        }

        private string SpellName(PhpNameAst name)
        {
            this.NotePrivateHelper(name.BoundSymbol);
            var written = name.ValueString ?? name.Identifier ?? "";
            if (IsRelativeClassKeyword(written)
                || IsBareLiteral(written))
            {
                return written;
            }

            var bound = name.BoundSymbol ?? this.ResolveWrittenName(written);
            if (bound != null)
            {
                this.NotePrivateHelper(bound);
                if (IsCopyableNamedSymbol(bound))
                {
                    var resolved = ResolvedName(bound);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }
            }

            if (written.Contains('\\') && !written.StartsWith('\\'))
            {
                return "\\" + written;
            }

            return written;
        }

        private IBaseSymbol? ResolveWrittenName(string written)
        {
            if (this._resolver is null
                || this._fromScope is null
                || string.IsNullOrWhiteSpace(written)
                || written.StartsWith('$'))
            {
                return null;
            }

            if (written.StartsWith('\\'))
            {
                return this._resolver.ResolveQualifiedName(written.TrimStart('\\').Split('\\'));
            }

            if (written.Contains('\\'))
            {
                return this._resolver.ResolveRelativeName(written.Split('\\'), this._fromScope);
            }

            return this._resolver.ResolveSymbol(written, this._fromScope)
                ?? this._resolver.ResolveRelativeName([written], this._fromScope);
        }

        private static bool IsCopyableNamedSymbol(IBaseSymbol bound) =>
            bound is FunctionDeclarationSymbol
                or ObjectDeclarationSymbol
                or ObjectMethodSymbol
                or ObjectConstantSymbol
                or ConstantSymbol;

        private void NotePrivateHelper(IBaseSymbol? symbol)
        {
            if (symbol is not BaseSymbol baseSymbol)
            {
                return;
            }

            if ((baseSymbol.Visibility & MemberModifier.Private) == 0
                && (baseSymbol.Visibility & MemberModifier.Protected) == 0)
            {
                return;
            }

            if (baseSymbol is not (
                FunctionDeclarationSymbol
                or ObjectMethodSymbol
                or ObjectPropertySymbol
                or ObjectConstantSymbol
                or ObjectDeclarationSymbol))
            {
                return;
            }

            this._privateHelper = true;
        }

        private static string ResolvedName(IBaseSymbol symbol)
        {
            var phpName = symbol switch
            {
                FunctionDeclarationSymbol { OriginalPhpName: { Length: > 0 } original } => original,
                ObjectMethodSymbol { OriginalPhpName: { Length: > 0 } original } => original,
                ObjectDeclarationSymbol { OriginalPhpName: { Length: > 0 } original } => original,
                _ => null,
            };

            var fqn = (symbol.FullyQualifiedName ?? "").Trim();
            if (string.IsNullOrEmpty(fqn))
            {
                fqn = (phpName ?? symbol.Name ?? "").Trim();
            }
            else if (!string.IsNullOrEmpty(phpName))
            {
                var slash = fqn.LastIndexOf('\\');
                fqn = slash < 0 ? phpName : fqn[..(slash + 1)] + phpName;
            }

            if (string.IsNullOrEmpty(fqn) || IsRelativeClassKeyword(fqn))
            {
                return fqn;
            }

            return fqn.StartsWith('\\') ? fqn : "\\" + fqn;
        }

        private static string? FallbackToken(IExpression expression)
        {
            if (!string.IsNullOrEmpty(expression.ValueString))
            {
                return expression.ValueString;
            }

            return string.IsNullOrEmpty(expression.Identifier) ? null : expression.Identifier;
        }

        private static string FormatStringLiteral(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            {
                return value;
            }

            return "'" + value.Replace("'", "\\'", StringComparison.Ordinal) + "'";
        }

        private static string SanitizeThisAlias(string text)
        {
            if (string.IsNullOrEmpty(text)
                || !text.Contains(GeneratedNames.ExtensionReceiverThisAlias, StringComparison.Ordinal))
            {
                return text;
            }

            return text.Replace(GeneratedNames.ExtensionReceiverThisAlias, "$this", StringComparison.Ordinal);
        }

        private static bool IsRelativeClassKeyword(string text) =>
            string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "static", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "parent", StringComparison.OrdinalIgnoreCase);

        private static bool IsBareLiteral(string text) =>
            string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
    }
}
