using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter.NameGeneration;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Selects the operator overload form that matches a given operand combination (Story 11 §8
    /// redesign). All generated methods are static and both operands are real parameters, so there is
    /// no self-on-left/self-on-right fallback: the call site picks the declaring class (left operand
    /// first, then right) whose operator declares a form matching the runtime operand types.
    /// </summary>
    internal static class OperatorOverloadResolver
    {
        /// <summary>
        /// Selects the most specific binary operator form (out of <paramref name="candidates"/>) whose
        /// two parameters accept <paramref name="leftOperand"/> and <paramref name="rightOperand"/>
        /// respectively. Returns <c>null</c> when no form matches the operand combination.
        /// </summary>
        public static ObjectOperatorOverloadMethodSymbol? SelectMatchingBinaryForm(
            IEnumerable<ObjectOperatorOverloadMethodSymbol> candidates,
            OverloadableOperator op,
            IExpression? leftOperand,
            IExpression? rightOperand,
            IBaseSymbol owningType,
            Func<IExpression?, IBaseSymbol?> resolveExpressionType,
            Func<IExpression?, string> guessOperandTypeName)
        {
            var leftSymbol = resolveExpressionType(leftOperand);
            var leftName = guessOperandTypeName(leftOperand);
            var rightSymbol = resolveExpressionType(rightOperand);
            var rightName = guessOperandTypeName(rightOperand);
            var leftUnknown = leftSymbol == null && leftOperand is PhpVariableAst;
            var rightUnknown = rightSymbol == null && rightOperand is PhpVariableAst;

            return SelectMatchingBinaryForm(
                candidates,
                op,
                leftSymbol,
                leftName,
                rightSymbol,
                rightName,
                owningType,
                leftUnknown,
                rightUnknown);
        }

        /// <summary>
        /// Selects the most specific binary operator form using already-resolved operand symbols /
        /// names (checker inference path — mirrors the expression-based overload used by emit).
        /// </summary>
        public static ObjectOperatorOverloadMethodSymbol? SelectMatchingBinaryForm(
            IEnumerable<ObjectOperatorOverloadMethodSymbol> candidates,
            OverloadableOperator op,
            IBaseSymbol? leftSymbol,
            string leftName,
            IBaseSymbol? rightSymbol,
            string rightName,
            IBaseSymbol owningType,
            bool leftUnknown = false,
            bool rightUnknown = false)
        {
            ObjectOperatorOverloadMethodSymbol? firstArityMatch = null;
            ObjectOperatorOverloadMethodSymbol? best = null;
            var bestScore = int.MinValue;

            foreach (var candidate in candidates)
            {
                if (candidate.Operator != op || candidate.Parameters.Count < 2)
                {
                    continue;
                }

                firstArityMatch ??= candidate;

                var leftParam = candidate.Parameters[0].DeclaredType;
                var rightParam = candidate.Parameters[1].DeclaredType;
                if (leftParam == null || rightParam == null)
                {
                    continue;
                }

                var leftOk = leftUnknown || TypeMatches(leftSymbol, leftName, leftParam, owningType);
                var rightOk = rightUnknown || TypeMatches(rightSymbol, rightName, rightParam, owningType);
                if (!leftOk || !rightOk)
                {
                    continue;
                }

                if (leftUnknown && rightUnknown)
                {
                    // Nothing concrete to score; keep the first arity match as the answer.
                    continue;
                }

                var score = ScoreType(leftSymbol, leftName, leftParam, owningType)
                    + ScoreType(rightSymbol, rightName, rightParam, owningType);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best ?? firstArityMatch;
        }

        /// <summary>
        /// Selects the unary operator form whose sole parameter accepts <paramref name="operand"/>.
        /// </summary>
        public static ObjectOperatorOverloadMethodSymbol? SelectMatchingUnaryForm(
            IEnumerable<ObjectOperatorOverloadMethodSymbol> candidates,
            OverloadableOperator op,
            IExpression? operand,
            IBaseSymbol owningType,
            Func<IExpression?, IBaseSymbol?> resolveExpressionType,
            Func<IExpression?, string> guessOperandTypeName)
        {
            var operandSymbol = resolveExpressionType(operand);
            var operandName = guessOperandTypeName(operand);
            return SelectMatchingUnaryForm(candidates, op, operandSymbol, operandName, owningType);
        }

        /// <summary>
        /// Selects the unary operator form using an already-resolved operand symbol / name
        /// (checker inference path).
        /// </summary>
        public static ObjectOperatorOverloadMethodSymbol? SelectMatchingUnaryForm(
            IEnumerable<ObjectOperatorOverloadMethodSymbol> candidates,
            OverloadableOperator op,
            IBaseSymbol? operandSymbol,
            string operandName,
            IBaseSymbol owningType)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Operator != op || candidate.Parameters.Count != 1)
                {
                    continue;
                }

                var paramType = candidate.Parameters[0].DeclaredType;
                if (paramType == null)
                {
                    return candidate;
                }

                if (TypeMatches(operandSymbol, operandName, paramType, owningType))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// True when two forms of the same operator can both match the same runtime operand
        /// combination, i.e. they are NOT mutually distinguishable (checker error). Binary forms are
        /// ambiguous when both operand positions overlap; unary/word forms are ambiguous whenever
        /// their sole operand overlaps; convert forms are ambiguous when their source (from) type or
        /// target (to) type overlaps.
        /// </summary>
        public static bool FormsAreAmbiguous(
            ObjectOperatorOverloadMethodSymbol a,
            ObjectOperatorOverloadMethodSymbol b,
            IBaseSymbol owningType)
        {
            if (a.Operator != b.Operator)
            {
                return false;
            }

            if (a.Operator == OverloadableOperator.Convert)
            {
                var aTo = IsConvertToForm(a, owningType);
                var bTo = IsConvertToForm(b, owningType);
                if (aTo != bTo)
                {
                    return false;
                }

                if (aTo)
                {
                    // Ambiguous only when both convert to the same target type.
                    return string.Equals(
                        SpellTypeKey(a.ReturnType, owningType.Name),
                        SpellTypeKey(b.ReturnType, owningType.Name),
                        StringComparison.OrdinalIgnoreCase);
                }

                return TypesOverlap(
                    a.Parameters.ElementAtOrDefault(0)?.DeclaredType,
                    b.Parameters.ElementAtOrDefault(0)?.DeclaredType,
                    owningType);
            }

            if (a.Parameters.Count == 1 && b.Parameters.Count == 1)
            {
                return TypesOverlap(
                    a.Parameters[0].DeclaredType, b.Parameters[0].DeclaredType, owningType);
            }

            if (a.Parameters.Count >= 2 && b.Parameters.Count >= 2)
            {
                return TypesOverlap(a.Parameters[0].DeclaredType, b.Parameters[0].DeclaredType, owningType)
                    && TypesOverlap(a.Parameters[1].DeclaredType, b.Parameters[1].DeclaredType, owningType);
            }

            return false;
        }

        /// <summary>True when a convert overload's sole operand is <c>self</c> (a convert-to form).</summary>
        public static bool IsConvertToForm(
            ObjectOperatorOverloadMethodSymbol overload,
            IBaseSymbol owningType)
            => overload.Parameters.Count == 1
                && IsSelfType(overload.Parameters[0].DeclaredType, owningType);

        /// <summary>
        /// Formats a declared type expression into an operator name segment via
        /// <see cref="TypeNameFormatter.FormatTypeNameSegment"/> (Number/Scalar/Or shortcuts).
        /// </summary>
        public static string SpellTypeKey(ITypeExpression? type, string selfTypeKey)
        {
            var raw = SpellTypeRaw(type, selfTypeKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Mixed";
            }

            var formatted = TypeNameFormatter.FormatTypeNameSegment(raw);
            return string.IsNullOrEmpty(formatted) ? "Mixed" : formatted;
        }

        private static string SpellTypeRaw(ITypeExpression? type, string selfTypeKey)
        {
            switch (type)
            {
                case null:
                    return "";
                case PhpTypeExpressionAst composite when composite.TypeKind == PhpTypeKind.Union:
                {
                    var parts = composite.Types?.GetAllNotNull()
                        .Select(m => SpellTypeRaw(m, selfTypeKey))
                        .Where(p => !string.IsNullOrEmpty(p)
                            && !string.Equals(p, "null", StringComparison.OrdinalIgnoreCase))
                        .ToList() ?? [];
                    return string.Join("|", parts);
                }
                case PhpTypeExpressionAst composite when composite.IsNullable:
                {
                    var inner = composite.Types?.GetAllNotNull().FirstOrDefault();
                    return SpellTypeRaw(inner, selfTypeKey);
                }
                case PhpTypeExpressionAst composite:
                {
                    var inner = composite.Types?.GetAllNotNull().FirstOrDefault();
                    return SpellTypeRaw(inner, selfTypeKey);
                }
                case PhpBuiltinTypeAst builtin:
                {
                    var name = builtin.Identifier ?? "";
                    if (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase))
                    {
                        return selfTypeKey;
                    }

                    return name;
                }
                case PhpNamedTypeAst named:
                {
                    var text = GetNamedTypeText(named) ?? "";
                    if (string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(text, "static", StringComparison.OrdinalIgnoreCase))
                    {
                        return selfTypeKey;
                    }

                    return text.TrimStart('\\');
                }
                default:
                    return "";
            }
        }

        private static bool TypeMatches(
            IBaseSymbol? operandSymbol,
            string operandName,
            ITypeExpression paramType,
            IBaseSymbol leftType)
        {
            return paramType switch
            {
                PhpTypeExpressionAst composite when composite.TypeKind == PhpTypeKind.Union =>
                    composite.Types?.GetAllNotNull().Any(member =>
                        TypeMatches(operandSymbol, operandName, member, leftType)) == true,
                PhpTypeExpressionAst composite when composite.IsNullable =>
                    TypeMatches(operandSymbol, operandName,
                        composite.Types?.GetAllNotNull().FirstOrDefault() ?? composite, leftType),
                PhpTypeExpressionAst composite =>
                    composite.Types?.GetAllNotNull().FirstOrDefault() is ITypeExpression inner
                        && TypeMatches(operandSymbol, operandName, inner, leftType),
                _ => MatchesAtomicType(operandSymbol, operandName, paramType, leftType),
            };
        }

        private static bool MatchesAtomicType(
            IBaseSymbol? operandSymbol,
            string operandName,
            ITypeExpression paramType,
            IBaseSymbol leftType)
        {
            if (IsSelfType(paramType, leftType))
            {
                if (leftType is BuiltInTypeSymbol leftBuiltin)
                {
                    // Extension operator on a builtin: `self` means that builtin (e.g. string).
                    if (operandSymbol is BuiltInTypeSymbol operandBuiltin)
                    {
                        return BuiltinNamesMatch(operandBuiltin.Name, leftBuiltin.Name);
                    }

                    if (operandSymbol is ObjectDeclarationSymbol)
                    {
                        return false;
                    }

                    return BuiltinNamesMatch(operandName, leftBuiltin.Name);
                }

                if (operandSymbol is ObjectDeclarationSymbol operandObject)
                {
                    return string.Equals(
                        operandObject.FullyQualifiedName,
                        leftType.FullyQualifiedName,
                        StringComparison.OrdinalIgnoreCase)
                        || string.Equals(operandObject.Name, leftType.Name, StringComparison.OrdinalIgnoreCase);
                }

                // A resolved non-object operand (e.g. int property) can never be `self` for a
                // class-owned overload, even if a fall-through name guess still spells the class.
                if (operandSymbol is BuiltInTypeSymbol)
                {
                    return false;
                }

                return string.Equals(operandName, leftType.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(operandName, "This", StringComparison.OrdinalIgnoreCase);
            }

            if (paramType is PhpBuiltinTypeAst builtin)
            {
                var builtinName = builtin.BoundSymbol is BuiltInTypeSymbol builtInParam
                    ? builtInParam.Name
                    : (builtin.Identifier ?? "");

                // Prefer the resolved operand symbol over a guessed name — property access like
                // `$obj->intProp` must match `int` even when the name guess falls through to `$obj`.
                if (operandSymbol is BuiltInTypeSymbol operandBuiltin)
                {
                    return BuiltinNamesMatch(operandBuiltin.Name, builtinName);
                }

                return BuiltinNamesMatch(operandName, builtinName);
            }

            if (paramType is PhpNamedTypeAst named)
            {
                var paramName = GetNamedTypeText(named);
                if (string.IsNullOrWhiteSpace(paramName))
                {
                    return false;
                }

                if (named.BoundSymbol is BuiltInTypeSymbol builtInParam
                    || string.Equals(paramName, "int", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paramName, "float", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paramName, "string", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paramName, "bool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paramName, "array", StringComparison.OrdinalIgnoreCase))
                {
                    var builtinParamName = named.BoundSymbol is BuiltInTypeSymbol bp
                        ? bp.Name
                        : paramName!;
                    if (operandSymbol is BuiltInTypeSymbol operandBuiltin)
                    {
                        return BuiltinNamesMatch(operandBuiltin.Name, builtinParamName);
                    }

                    return BuiltinNamesMatch(operandName, builtinParamName);
                }

                if (operandSymbol is IBaseSymbol symbol)
                {
                    return string.Equals(symbol.Name, paramName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(symbol.FullyQualifiedName, paramName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(symbol.FullyQualifiedName, "\\" + paramName.TrimStart('\\'), StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(operandName, paramName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(operandName, paramName.TrimStart('\\').Split('\\')[^1], StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static int ScoreType(
            IBaseSymbol? operandSymbol,
            string operandName,
            ITypeExpression paramType,
            IBaseSymbol leftType)
        {
            if (paramType is PhpTypeExpressionAst composite && composite.TypeKind == PhpTypeKind.Union)
            {
                var members = composite.Types?.GetAllNotNull().ToList() ?? [];
                var memberScores = members
                    .Select(member => ScoreType(operandSymbol, operandName, member, leftType))
                    .Where(score => score > 0)
                    .ToList();

                if (memberScores.Count == 0)
                {
                    return 0;
                }

                return memberScores.Max() - members.Count;
            }

            if (paramType is PhpTypeExpressionAst wrapper
                && wrapper.Types?.GetAllNotNull().FirstOrDefault() is ITypeExpression inner)
            {
                return ScoreType(operandSymbol, operandName, inner, leftType);
            }

            if (IsSelfType(paramType, leftType))
            {
                return MatchesAtomicType(operandSymbol, operandName, paramType, leftType) ? 90 : 0;
            }

            if (paramType is PhpBuiltinTypeAst builtin)
            {
                var builtinName = builtin.Identifier ?? "";
                if (operandSymbol is BuiltInTypeSymbol operandBuiltin)
                {
                    return BuiltinNamesMatch(operandBuiltin.Name, builtinName) ? 100 : 0;
                }

                return BuiltinNamesMatch(operandName, builtinName) ? 100 : 0;
            }

            if (paramType is PhpNamedTypeAst)
            {
                return MatchesAtomicType(operandSymbol, operandName, paramType, leftType) ? 100 : 0;
            }

            return 0;
        }

        /// <summary>
        /// True when two declared types can both accept some common runtime value (used for the
        /// mutual-distinguishability checker rule). Unions are expanded and any overlapping atom wins.
        /// </summary>
        private static bool TypesOverlap(
            ITypeExpression? a,
            ITypeExpression? b,
            IBaseSymbol owningType)
        {
            foreach (var atomA in ExpandAtoms(a, owningType))
            {
                foreach (var atomB in ExpandAtoms(b, owningType))
                {
                    if (string.Equals(atomA, atomB, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<string> ExpandAtoms(ITypeExpression? type, IBaseSymbol owningType)
        {
            switch (type)
            {
                case null:
                    yield break;
                case PhpTypeExpressionAst composite when composite.TypeKind == PhpTypeKind.Union:
                    foreach (var member in composite.Types?.GetAllNotNull() ?? [])
                    {
                        foreach (var atom in ExpandAtoms(member, owningType))
                        {
                            yield return atom;
                        }
                    }

                    yield break;
                case PhpTypeExpressionAst composite:
                    foreach (var member in composite.Types?.GetAllNotNull() ?? [])
                    {
                        foreach (var atom in ExpandAtoms(member, owningType))
                        {
                            yield return atom;
                        }
                    }

                    yield break;
                case PhpBuiltinTypeAst builtin:
                    yield return NormalizeAtom(builtin.Identifier ?? "", owningType);
                    yield break;
                case PhpNamedTypeAst named:
                    yield return NormalizeAtom(GetNamedTypeText(named) ?? "", owningType);
                    yield break;
            }
        }

        private static string NormalizeAtom(string raw, IBaseSymbol owningType)
        {
            var name = (raw ?? "").Trim().TrimStart('\\');
            if (name.Length == 0)
            {
                return "mixed";
            }

            if (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase))
            {
                return owningType.Name.ToLowerInvariant();
            }

            var simple = name.Split('\\')[^1];
            return simple.ToLowerInvariant();
        }

        private static bool IsSelfType(ITypeExpression? typeExpression, IBaseSymbol leftType)
        {
            if (typeExpression is PhpBuiltinTypeAst builtin)
            {
                return string.Equals(builtin.Identifier, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(builtin.Identifier, "static", StringComparison.OrdinalIgnoreCase);
            }

            if (typeExpression is PhpNamedTypeAst named)
            {
                var text = GetNamedTypeText(named);
                return string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "static", StringComparison.OrdinalIgnoreCase);
            }

            if (typeExpression is PhpTypeExpressionAst composite
                && composite.Types?.GetAllNotNull().FirstOrDefault() is ITypeExpression inner)
            {
                return IsSelfType(inner, leftType);
            }

            return false;
        }

        private static string? GetNamedTypeText(PhpNamedTypeAst named)
        {
            return named.Name switch
            {
                PhpNameAst name => name.ValueString,
                _ => null,
            };
        }

        private static bool BuiltinNamesMatch(string operandName, string builtinName)
        {
            if (string.Equals(operandName, builtinName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedOperand = NormalizeBuiltinName(operandName);
            var normalizedBuiltin = NormalizeBuiltinName(builtinName);
            return string.Equals(normalizedOperand, normalizedBuiltin, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBuiltinName(string name)
        {
            if (string.Equals(name, "Int", StringComparison.Ordinal))
            {
                return "int";
            }

            if (string.Equals(name, "Float", StringComparison.Ordinal))
            {
                return "float";
            }

            if (string.Equals(name, "String", StringComparison.Ordinal))
            {
                return "string";
            }

            if (string.Equals(name, "Bool", StringComparison.Ordinal))
            {
                return "bool";
            }

            if (string.Equals(name, "Array", StringComparison.Ordinal))
            {
                return "array";
            }

            if (string.Equals(name, "Mixed", StringComparison.Ordinal))
            {
                return "mixed";
            }

            return name;
        }
    }
}
