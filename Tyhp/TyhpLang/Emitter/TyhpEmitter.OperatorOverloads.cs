using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        // The runtime exception thrown when a hand-written PHP caller passes an operand combination
        // that no operator form accepts (Tyhp's checker guarantees Tyhp callers never hit this).
        private const string InvalidOperatorParamsException =
            "\\Tyhp\\Exceptions\\InvalidParametersForOperatorOverloadException";

        // Story 11 §8 redesign: every operator collapses into a single static method (except convert's
        // instance to-forms). Forms of the same operator are grouped and emitted as one method whose
        // union-typed parameters are dispatched on at runtime via instanceof/is_* guards.
        private void EmitCollapsedOperatorMethods(
            EmitItem parent,
            IReadOnlyList<TyhpOperatorOverloadAst> overloads,
            bool isExtension)
        {
            if (overloads.Count == 0)
            {
                return;
            }

            // Preserve declaration order of operators while grouping all forms of each together.
            var groups = new List<(OverloadableOperator Op, List<TyhpOperatorOverloadAst> Forms)>();
            var indexByOp = new Dictionary<OverloadableOperator, int>();
            foreach (var overload in overloads)
            {
                var op = this.GetOperatorEnum(overload);
                if (op == OverloadableOperator.Invalid)
                {
                    continue;
                }

                if (!indexByOp.TryGetValue(op, out var idx))
                {
                    idx = groups.Count;
                    groups.Add((op, new List<TyhpOperatorOverloadAst>()));
                    indexByOp[op] = idx;
                }

                groups[idx].Forms.Add(overload);
            }

            var emittedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (op, forms) in groups)
            {
                if (op == OverloadableOperator.Convert)
                {
                    this.EmitConvertGroup(parent, forms, isExtension, emittedNames);
                    continue;
                }

                if (forms[0].RightParameter == null)
                {
                    this.EmitUnaryGroup(parent, op, forms, isExtension, emittedNames);
                }
                else
                {
                    this.EmitBinaryGroup(parent, op, forms, isExtension, emittedNames);
                }
            }
        }

        private OverloadableOperator GetOperatorEnum(TyhpOperatorOverloadAst overload)
        {
            var isUnary = overload.RightParameter == null;
            return OverloadableOperatorHelper.FromToken(
                (int)(overload.Op?.ValueInt64 ?? -1),
                overload.Op?.ValueString ?? "",
                isAlternateKind: isUnary);
        }

        private void EmitBinaryGroup(
            EmitItem parent,
            OverloadableOperator op,
            IReadOnlyList<TyhpOperatorOverloadAst> forms,
            bool isExtension,
            HashSet<string> emittedNames)
        {
            var methodName = OperatorMethodNameGenerator.GetMethodName(op);
            if (string.IsNullOrEmpty(methodName) || !emittedNames.Add(methodName))
            {
                return;
            }

            var (isAbstract, isFinal) = this.GetModifierFlags(forms);
            var leftUnion = this.BuildOperandUnionText(
                forms.Select(f => (f.LeftParameter?.Type, this.SelfTypeText(f, isExtension))));
            var rightUnion = this.BuildOperandUnionText(
                forms.Select(f => (f.RightParameter?.Type, this.SelfTypeText(f, isExtension))));
            var returnClause = this.BuildOperatorReturnClause(forms, isExtension);
            var prefix = (isFinal ? "final " : string.Empty) + "public static function";

            if (isAbstract)
            {
                EmitItem.Line(
                    forms[0], EmitType.ObjectStaticMethods,
                    $"{(isFinal ? "final " : string.Empty)}abstract public static function {methodName}({leftUnion} $l, {rightUnion} $r){returnClause};",
                    parent);
                return;
            }

            var methodBlock = EmitItem.BlockBraceNextLine(
                forms[0], EmitType.ObjectStaticMethods,
                $"{prefix} {methodName}({leftUnion} $l, {rightUnion} $r){returnClause}",
                "}", parent);

            var segments = new List<(string Open, Action<EmitItem> Body)>();
            for (var i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                var selfInstance = this.SelfTypeText(form, isExtension);
                var guard = this.BuildBinaryGuard(form, selfInstance);
                var keyword = i == 0 ? "if" : "elseif";
                segments.Add((
                    $"{keyword} ({guard}) {{",
                    block => this.EmitOperatorBranchBody(
                        form, block, ("$l", form.LeftParameter), ("$r", form.RightParameter))));
            }

            segments.Add((
                "else {",
                block => this.EmitOperatorThrow(block, forms[0], "$l", "$r")));
            this.EmitBraceSegments(forms[0], methodBlock, EmitType.FunctionStatement, segments);
        }

        private void EmitUnaryGroup(
            EmitItem parent,
            OverloadableOperator op,
            IReadOnlyList<TyhpOperatorOverloadAst> forms,
            bool isExtension,
            HashSet<string> emittedNames)
        {
            var methodName = OperatorMethodNameGenerator.GetMethodName(op);
            if (string.IsNullOrEmpty(methodName) || !emittedNames.Add(methodName))
            {
                return;
            }

            var (isAbstract, isFinal) = this.GetModifierFlags(forms);
            var operandUnion = this.BuildOperandUnionText(
                forms.Select(f => (f.LeftParameter?.Type, this.SelfTypeText(f, isExtension))));
            var returnClause = this.BuildOperatorReturnClause(forms, isExtension);

            if (isAbstract)
            {
                EmitItem.Line(
                    forms[0], EmitType.ObjectStaticMethods,
                    $"{(isFinal ? "final " : string.Empty)}abstract public static function {methodName}({operandUnion} $o){returnClause};",
                    parent);
                return;
            }

            var prefix = (isFinal ? "final " : string.Empty) + "public static function";
            var methodBlock = EmitItem.BlockBraceNextLine(
                forms[0], EmitType.ObjectStaticMethods,
                $"{prefix} {methodName}({operandUnion} $o){returnClause}",
                "}", parent);

            var segments = new List<(string Open, Action<EmitItem> Body)>();
            for (var i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                var selfInstance = this.SelfTypeText(form, isExtension);
                var guard = this.BuildOperandGuard("$o", form.LeftParameter?.Type, selfInstance);
                var keyword = i == 0 ? "if" : "elseif";
                segments.Add((
                    $"{keyword} ({guard}) {{",
                    block => this.EmitOperatorBranchBody(form, block, ("$o", form.LeftParameter))));
            }

            segments.Add((
                "else {",
                block => this.EmitOperatorThrow(block, forms[0], "$o")));
            this.EmitBraceSegments(forms[0], methodBlock, EmitType.FunctionStatement, segments);
        }

        private void EmitConvertGroup(
            EmitItem parent,
            IReadOnlyList<TyhpOperatorOverloadAst> forms,
            bool isExtension,
            HashSet<string> emittedNames)
        {
            var toForms = forms.Where(f => this.IsSelfTypeExpression(f.LeftParameter?.Type)).ToList();
            var fromForms = forms.Where(f => !this.IsSelfTypeExpression(f.LeftParameter?.Type)).ToList();

            foreach (var toForm in toForms)
            {
                this.EmitConvertTo(parent, toForm, emittedNames);
            }

            if (fromForms.Count > 0)
            {
                this.EmitConvertFrom(parent, fromForms, isExtension, emittedNames);
            }
        }

        private void EmitConvertTo(EmitItem parent, TyhpOperatorOverloadAst form, HashSet<string> emittedNames)
        {
            var targetRaw = GetConvertTargetRawName(form.ReturnType);
            var methodName = OperatorMethodNameGenerator.GetConvertToMethodName(targetRaw);
            if (!emittedNames.Add(methodName))
            {
                return;
            }

            var returnText = form.ReturnType != null ? this.BuildTypeExpression(form.ReturnType) : "mixed";
            var (isAbstract, isFinal) = this.GetModifierFlags([form]);
            var finalPrefix = isFinal ? "final " : string.Empty;

            // convert-to is ALWAYS an instance method (satisfies \Stringable and the *Convertible
            // instance interfaces).
            if (isAbstract)
            {
                EmitItem.Line(
                    form, EmitType.ObjectInstanceMethods,
                    $"{finalPrefix}abstract public function {methodName}(): {returnText};",
                    parent);
                return;
            }

            var block = EmitItem.BlockBraceNextLine(
                form, EmitType.ObjectInstanceMethods,
                $"{finalPrefix}public function {methodName}(): {returnText}", "}", parent);

            // If the author names the self-operand `$this`, it already *is* PHP's real instance
            // `$this` (convert-to is never static) — skip the alias line entirely. PHP forbids
            // re-assigning `$this` (`$this = $this;` is a fatal error), unlike the static
            // operator-branch case where `$this` needs the `$this_` rename because it is an
            // ordinary parameter there.
            if (!string.IsNullOrEmpty(form.LeftParameter?.Name) && !IsThisParameterName(form.LeftParameter!.Name))
            {
                EmitItem.Line(form, EmitType.FunctionStatement, $"{form.LeftParameter!.Name} = $this;", block);
            }

            this.EmitFunctionBody(form.Body, block);
        }

        private void EmitConvertFrom(
            EmitItem parent,
            IReadOnlyList<TyhpOperatorOverloadAst> forms,
            bool isExtension,
            HashSet<string> emittedNames)
        {
            var methodName = OperatorMethodNameGenerator.ConvertFromMethodName;
            if (!emittedNames.Add(methodName))
            {
                return;
            }

            var sourceUnion = this.BuildOperandUnionText(
                forms.Select(f => (f.LeftParameter?.Type, this.SelfTypeText(f, isExtension))));
            var returnType = isExtension ? this.ExtensionTargetText(forms[0]) : "self";
            var (isAbstract, isFinal) = this.GetModifierFlags(forms);
            var finalPrefix = isFinal ? "final " : string.Empty;

            if (isAbstract)
            {
                EmitItem.Line(
                    forms[0], EmitType.ObjectStaticMethods,
                    $"{finalPrefix}abstract public static function {methodName}({sourceUnion} $from): {returnType};",
                    parent);
                return;
            }

            var methodBlock = EmitItem.BlockBraceNextLine(
                forms[0], EmitType.ObjectStaticMethods,
                $"{finalPrefix}public static function {methodName}({sourceUnion} $from): {returnType}", "}", parent);

            var segments = new List<(string Open, Action<EmitItem> Body)>();
            for (var i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                var selfInstance = this.SelfTypeText(form, isExtension);
                var guard = this.BuildOperandGuard("$from", form.LeftParameter?.Type, selfInstance);
                var keyword = i == 0 ? "if" : "elseif";
                segments.Add((
                    $"{keyword} ({guard}) {{",
                    block => this.EmitOperatorBranchBody(form, block, ("$from", form.LeftParameter))));
            }

            segments.Add((
                "else {",
                block => this.EmitOperatorThrow(block, forms[0], "$from")));
            this.EmitBraceSegments(forms[0], methodBlock, EmitType.FunctionStatement, segments);
        }

        // Aliases each declared operand name to the canonical dispatch parameter, then emits the body.
        private void EmitOperatorBranchBody(
            TyhpOperatorOverloadAst form,
            EmitItem branch,
            params (string CanonicalVar, PhpParameterAst? Param)[] operands)
        {
            var previousAlias = this._context.ExtensionReceiverThisAlias;
            try
            {
                foreach (var (canonicalVar, param) in operands)
                {
                    if (string.IsNullOrEmpty(param?.Name) || param!.Name == canonicalVar)
                    {
                        continue;
                    }

                    var emittedName = param.Name;
                    if (IsThisParameterName(emittedName))
                    {
                        // Static operator methods cannot take / assign `$this`; mirror extension-method
                        // rename. Avoid colliding with another declared operand already named `$this_`.
                        emittedName = ResolveCollisionSafeThisAlias(
                            operands.Where(o => o.Param != param).Select(o => o.Param?.Name));
                        this._context.ExtensionReceiverThisAlias = emittedName;
                    }

                    EmitItem.Line(form, EmitType.FunctionStatement, $"{emittedName} = {canonicalVar};", branch);
                }

                this.EmitFunctionBody(form.Body, branch);
            }
            finally
            {
                this._context.ExtensionReceiverThisAlias = previousAlias;
            }
        }

        private void EmitOperatorThrow(EmitItem block, TyhpOperatorOverloadAst ast, params string[] operandVars)
        {
            var args = string.Join(", ", new[] { "static::class", "__FUNCTION__" }.Concat(operandVars));
            EmitItem.Line(
                ast, EmitType.FunctionStatement,
                $"throw new {InvalidOperatorParamsException}({args});", block);
        }

        private (bool IsAbstract, bool IsFinal) GetModifierFlags(IReadOnlyList<TyhpOperatorOverloadAst> forms)
        {
            var isAbstract = false;
            var isFinal = false;
            foreach (var form in forms)
            {
                if (form.Body == null)
                {
                    isAbstract = true;
                }

                foreach (var modifier in form.Modifiers)
                {
                    var text = modifier.ToString().ToLowerInvariant();
                    if (text == "abstract")
                    {
                        isAbstract = true;
                    }
                    else if (text == "final")
                    {
                        isFinal = true;
                    }
                }
            }

            return (isAbstract, isFinal);
        }

        // The PHP type text used to spell a `self`/`static` operand for the given form: the literal
        // `self` for class operators, or the resolved target type for extension operators.
        private string SelfTypeText(TyhpOperatorOverloadAst form, bool isExtension)
            => isExtension ? this.ExtensionTargetText(form) : "self";

        private string ExtensionTargetText(TyhpOperatorOverloadAst form)
            => form.ExtensionTargetType != null
                ? this.BuildTypeExpression(form.ExtensionTargetType)
                : (this._currentObjectShortName ?? "self");

        private string BuildOperatorReturnClause(IReadOnlyList<TyhpOperatorOverloadAst> forms, bool isExtension)
        {
            var returnTypes = forms
                .Where(f => f.ReturnType != null)
                .Select(f => (f.ReturnType, isExtension ? this.ExtensionTargetText(f) : "static"))
                .ToList();
            if (returnTypes.Count == 0)
            {
                return string.Empty;
            }

            var union = this.BuildOperandUnionText(returnTypes!);
            return string.IsNullOrEmpty(union) ? string.Empty : ": " + union;
        }

        // Builds a deduplicated PHP union type string from a set of declared types, mapping each
        // form's `self`/`static` to the supplied replacement text.
        private string BuildOperandUnionText(IEnumerable<(ITypeExpression? Type, string SelfText)> types)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var atoms = new List<string>();
            var hasMixed = false;

            foreach (var (type, selfText) in types)
            {
                var text = type == null ? "mixed" : this.BuildTypeExpression(type);
                foreach (var rawPart in text.Split('|'))
                {
                    var part = rawPart.Trim().TrimStart('?');
                    if (part.Length == 0)
                    {
                        continue;
                    }

                    if (IsSelfKeyword(part))
                    {
                        part = selfText;
                    }

                    if (string.Equals(part, "mixed", StringComparison.OrdinalIgnoreCase))
                    {
                        hasMixed = true;
                        continue;
                    }

                    if (string.Equals(part, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (seen.Add(part))
                    {
                        atoms.Add(part);
                    }
                }
            }

            if (hasMixed || atoms.Count == 0)
            {
                return "mixed";
            }

            return string.Join(" | ", atoms);
        }

        private string BuildBinaryGuard(TyhpOperatorOverloadAst form, string selfInstanceType)
        {
            var left = this.BuildOperandGuard("$l", form.LeftParameter?.Type, selfInstanceType);
            var right = this.BuildOperandGuard("$r", form.RightParameter?.Type, selfInstanceType);
            return $"{left} && {right}";
        }

        // Builds a runtime type guard for a single operand: `instanceof` for self/class types and
        // `is_int`/`is_float`/… for builtins. Union operand types OR their per-atom guards.
        private string BuildOperandGuard(string varName, ITypeExpression? type, string selfInstanceType)
        {
            if (type == null)
            {
                return "true";
            }

            var text = this.BuildTypeExpression(type);
            var conditions = new List<string>();
            foreach (var rawPart in text.Split('|'))
            {
                var part = rawPart.Trim().TrimStart('?');
                if (part.Length == 0)
                {
                    continue;
                }

                if (IsSelfKeyword(part))
                {
                    conditions.Add($"{varName} instanceof {selfInstanceType}");
                }
                else if (string.Equals(part, "mixed", StringComparison.OrdinalIgnoreCase))
                {
                    return "true";
                }
                else if (string.Equals(part, "null", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add($"{varName} === null");
                }
                else if (TryBuiltinGuard(varName, part, out var builtinGuard))
                {
                    conditions.Add(builtinGuard);
                }
                else
                {
                    conditions.Add($"{varName} instanceof {part}");
                }
            }

            if (conditions.Count == 0)
            {
                return "true";
            }

            return conditions.Count == 1 ? conditions[0] : "(" + string.Join(" || ", conditions) + ")";
        }

        private static bool TryBuiltinGuard(string varName, string typeName, out string guard)
        {
            guard = typeName.ToLowerInvariant() switch
            {
                "int" => $"\\is_int({varName})",
                "float" => $"\\is_float({varName})",
                "string" => $"\\is_string({varName})",
                "bool" => $"\\is_bool({varName})",
                "array" => $"\\is_array({varName})",
                "object" => $"\\is_object({varName})",
                "callable" => $"\\is_callable({varName})",
                "iterable" => $"\\is_iterable({varName})",
                _ => string.Empty,
            };
            return guard.Length > 0;
        }

        private static bool IsSelfKeyword(string name)
            => string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase);

        // Collects the \Tyhp\Contracts\*Convertible interfaces required by an object's convert-to
        // overloads (auto-added to the emitted `implements` clause).
        private List<string> CollectConvertibleInterfaces(PhpObjectTypeDeclAst objectDecl)
        {
            var result = new List<string>();
            if (objectDecl.Body == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in objectDecl.Body.GetAllNotNull())
            {
                if (member is not TyhpOperatorOverloadAst overload)
                {
                    continue;
                }

                if (this.GetOperatorEnum(overload) != OverloadableOperator.Convert
                    || !this.IsSelfTypeExpression(overload.LeftParameter?.Type))
                {
                    continue;
                }

                var target = GetConvertTargetRawName(overload.ReturnType);
                var iface = OperatorMethodNameGenerator.GetConvertibleInterface(target);
                if (seen.Add(iface))
                {
                    result.Add(iface);
                }
            }

            return result;
        }

        private bool IsSelfTypeExpression(ITypeExpression? type)
        {
            switch (type)
            {
                case PhpBuiltinTypeAst builtin:
                    return IsSelfKeyword(builtin.Identifier ?? "");
                case PhpNamedTypeAst named:
                    return IsSelfKeyword(GetNamedTypeText(named) ?? "");
                case PhpTypeExpressionAst composite:
                    return composite.Types?.GetAllNotNull().Any(this.IsSelfTypeExpression) == true;
                default:
                    return false;
            }
        }

        private static string GetConvertTargetRawName(ITypeExpression? type)
        {
            switch (type)
            {
                case null:
                    return "mixed";
                case PhpBuiltinTypeAst builtin:
                    return string.IsNullOrWhiteSpace(builtin.Identifier) ? "mixed" : builtin.Identifier!;
                case PhpNamedTypeAst named:
                    var text = GetNamedTypeText(named);
                    return string.IsNullOrWhiteSpace(text) ? "mixed" : text!;
                case PhpTypeExpressionAst composite:
                    var inner = composite.Types?.GetAllNotNull().FirstOrDefault() as ITypeExpression;
                    return inner != null ? GetConvertTargetRawName(inner) : "mixed";
                default:
                    return "mixed";
            }
        }

        private static string? GetNamedTypeText(PhpNamedTypeAst named)
            => named.Name is PhpNameAst name ? name.ValueString : null;
    }
}
