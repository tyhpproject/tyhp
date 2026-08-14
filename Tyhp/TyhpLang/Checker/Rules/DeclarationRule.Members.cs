using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class DeclarationRule
    {
        private void CheckProperty(
            PhpPropertyDeclAst property,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Class members bypass CheckNode (CheckObjectBody calls us directly), so attribute
            // rules registered for PhpPropertyDeclAst never dispatch — validate explicitly here.
            AttributeRule.ValidateDeclarationAttributes(property, state, context, diagnostics);
            context.CheckAttributes(property, state);

            var modifiers = CheckerHelpers.ToMemberModifiers(property.Modifiers);

            if (state.EnclosingObject?.ObjectKind == PhpTypeDeclType.Interface)
            {
                if (property.Type is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, property, MessageCode.CheckerInterfacePropertyNotAllowed);
                }

                foreach (var prop in property.Properties?.GetAllNotNull() ?? [])
                {
                    if (prop.DefaultValue is not null)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, prop, MessageCode.CheckerInterfacePropertyInitializer, prop.Identifier);
                    }
                }
            }

            if (state.EnclosingObject?.ObjectKind == PhpTypeDeclType.Enum)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, property, MessageCode.CheckerEnumPropertyNotAllowed);
            }

            if ((state.Modifiers & MemberModifier.Readonly) != 0
                && (modifiers & MemberModifier.Readonly) == 0
                && (modifiers & MemberModifier.Static) == 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, property, MessageCode.CheckerReadonlyClassMutableProperty, property.Identifier);
            }

            if (property.Type is null)
            {
                var propName = property.Properties?.GetAllNotNull()
                        .Select(p => p.Identifier)
                        .FirstOrDefault(n => !string.IsNullOrEmpty(n))
                    ?? property.Identifier;
                if (propName.StartsWith('$'))
                {
                    propName = propName[1..];
                }

                CheckerHelpers.ReportError(
                    context, state, property, MessageCode.CheckerVariableTypeRequired, propName);
            }
            else
            {
                state.IsPropertyTypePosition = true;
                context.CheckNode(property.Type, state);
                context.MarkImportNames(property.Type, state);
                state.IsPropertyTypePosition = false;

                var declaredType = context.ResolveTypeAnnotation(property.Type, state);
                foreach (var prop in property.Properties?.GetAllNotNull() ?? [])
                {
                    if (prop.DefaultValue is not null)
                    {
                        if (!CheckerHelpers.IsConstantExpression(prop.DefaultValue, state))
                        {
                            CheckerHelpers.ReportError(
                                diagnostics, state, prop, MessageCode.CheckerNonConstantExpression);
                        }

                        var defaultType = context.ResolveExpressionType(prop.DefaultValue, state);
                        if (!context.IsAssignable(defaultType, declaredType))
                        {
                            CheckerHelpers.ReportError(
                                diagnostics, state, prop, MessageCode.CheckerTypeMismatch,
                                defaultType.DisplayName, declaredType.DisplayName);
                        }
                    }
                }

                // Generic-typed properties (incl. fixed args like `\Closure<bool>`) need
                // tyhpGenericObjectSetPropertyType registration → flag the enclosing class.
                if (state.EnclosingObject is { GenericParameters.Count: > 0 }
                    && TypeInvolvesGenericsForTracking(property.Type, state))
                {
                    context.MarkRequiresRuntimeGenericTracking(state.EnclosingObject);
                }
            }

            // PHP 8.4+ only allows `final` on a property hook; every other modifier is a parse error.
            foreach (var prop in property.Properties?.GetAllNotNull() ?? [])
            {
                ValidatePropertyHookModifiers(prop.Hooks, state, diagnostics);
                CheckByRefPropertyGetHooks(prop.Hooks, state, context, diagnostics);
                CheckPropertyHookFinalOverrides(prop.Identifier, prop.Hooks, state, context, diagnostics);

                // A hook already governs read/write access, so PHP fatals if `readonly` is also present.
                if (prop.Hooks is not null && (modifiers & MemberModifier.Readonly) != 0)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, prop, MessageCode.CheckerHookedPropertyReadonly);
                }

                var propertyType = property.Type is not null
                    ? context.ResolveTypeAnnotation(property.Type, state)
                    : CheckedTypes.Mixed;
                CheckPropertyHooks(prop.Hooks, propertyType, state, context, diagnostics);
            }
        }

        /// <summary>
        /// Type-checks <c>get</c>/<c>set</c> hook bodies (FOUND property-hook follow-up §1).
        /// <list type="bullet">
        /// <item><c>get =&gt; expr</c> / block get: expression or returns must match the property type.</item>
        /// <item><c>set =&gt; expr</c>: expression is the written value — must be assignable to the
        /// property type (arrow bodies are rewritten as <c>return expr;</c>).</item>
        /// <item>Block <c>set { … }</c>: statements with implicit void; seeds <c>$value</c>.</item>
        /// </list>
        /// </summary>
        private static void CheckPropertyHooks(
            PhpPropertyHookListAst? hooks,
            ICheckedType propertyType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (hooks is null)
            {
                return;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                CheckPropertyHook(hook, propertyType, state, context, diagnostics);
            }
        }

        private static void CheckPropertyHook(
            PhpPropertyHookAst hook,
            ICheckedType propertyType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (hook.Body is null)
            {
                return;
            }

            var hookState = state.Fork();
            SeedHookReceiverState(hookState, state, context);

            var hookName = hook.Identifier?.Trim() ?? string.Empty;
            var isGet = string.Equals(hookName, "get", StringComparison.OrdinalIgnoreCase);
            var isSet = string.Equals(hookName, "set", StringComparison.OrdinalIgnoreCase);

            if (isGet)
            {
                hookState.ExpectedReturnType = propertyType;
            }
            else if (isSet)
            {
                SeedSetHookValueParameter(hook, propertyType, hookState, context, diagnostics);
                // Arrow `set => expr` is lowered to `return expr;` — that value is what gets
                // written to the property, so expect the property type. Block set is void.
                hookState.ExpectedReturnType = hook.IsExpressionBody
                    ? propertyType
                    : CheckedTypes.Void;
            }

            switch (hook.Body)
            {
                case PhpStatementBlockAst block:
                    hookState.HasReturnedOnAllPaths = false;
                    context.CheckStatementBlock(block, hookState);

                    // A block `get { … }` hook fatals at runtime if it does not return a value on
                    // every path (block `set { … }` is void, so no return is required).
                    if (!IsEffectivelyVoid(hookState.ExpectedReturnType) && !hookState.HasReturnedOnAllPaths)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, hook, MessageCode.CheckerMissingReturnStatement, hookName);
                    }

                    break;
                case IExpression expression:
                    // Defensive: visitor normally wraps `=> expr` as a return statement block.
                    var exprType = context.ResolveExpressionType(expression, hookState);
                    context.CheckNode(expression, hookState);
                    var expected = isSet || isGet ? propertyType : hookState.ExpectedReturnType;
                    if (expected is not null)
                    {
                        context.CheckReturnType(expression, exprType, expected, hookState);
                    }

                    break;
                default:
                    context.CheckNode(hook.Body, hookState);
                    break;
            }
        }

        /// <summary>
        /// Property hooks run as instance accessors — seed <c>$this</c> and property-init state
        /// the same way instance methods do so <c>$this-&gt;prop</c> resolves.
        /// </summary>
        private static void SeedHookReceiverState(
            CheckerState hookState,
            CheckerState outerState,
            CheckerRuleContext context)
        {
            if (outerState.EnclosingObjectType is not null
                && !hookState.Variables.ContainsKey("this"))
            {
                hookState.Variables["this"] = VariableState.ForParameter(
                    new VariableSymbol("this") { IsParameter = true },
                    outerState.EnclosingObjectType,
                    isReference: false);
            }

            if (outerState.EnclosingObject is { } enclosingObject
                && hookState.PropertyInit.Count == 0)
            {
                var seeded = PropertyInitializationAnalysis.SeedForInstanceMethod(
                    enclosingObject, context.SymbolTree, context.GlobalScope);
                hookState.ReplacePropertyInit(seeded);
            }
        }

        private static void SeedSetHookValueParameter(
            PhpPropertyHookAst hook,
            ICheckedType propertyType,
            CheckerState hookState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var parameters = hook.Parameters?.GetAllNotNull().ToList() ?? [];
            if (parameters.Count > 0)
            {
                foreach (var paramAst in parameters)
                {
                    ICheckedType paramType = propertyType;
                    if (paramAst.Type is not null)
                    {
                        paramType = context.ResolveTypeAnnotation(paramAst.Type, hookState);
                        context.CheckNode(paramAst.Type, hookState);
                    }

                    var variable = new VariableSymbol(paramAst.Name) { IsParameter = true, IsRef = paramAst.IsRef };
                    hookState.Variables[paramAst.Name.TrimStart('$')] =
                        VariableState.ForParameter(variable, paramType, paramAst.IsRef);
                }

                return;
            }

            // PHP parameter-less set hooks expose implicit `$value` typed as the property type.
            var valueVar = new VariableSymbol("$value") { IsParameter = true };
            hookState.Variables["value"] = VariableState.ForParameter(valueVar, propertyType, false);
            _ = diagnostics;
        }

        /// <summary>
        /// Rejects any modifier other than <see cref="PhpModifier.Final"/> on a property hook.
        /// Real PHP 8.4+ fatals with "Cannot use the &lt;x&gt; modifier on a property hook".
        /// </summary>
        private static void ValidatePropertyHookModifiers(
            PhpPropertyHookListAst? hooks,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (hooks is null)
            {
                return;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                foreach (var modifier in hook.Modifiers?.Modifiers ?? [])
                {
                    if (modifier is PhpModifier.None or PhpModifier.Final)
                    {
                        continue;
                    }

                    var reportNode = (IBase2Ast?)hook.Modifiers ?? hook;
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerPropertyHookInvalidModifier,
                        FormatPropertyHookModifierName(modifier));
                }
            }
        }

        /// <summary>
        /// Rejects authored <c>&amp;get</c> when targeting PHP &lt; 8.4. Native hooks preserve by-ref
        /// semantics on PHP ≥ 8.4; the polyfill path cannot (<c>__get</c> is not by-ref), so a
        /// silent by-value lowering would change aliasing behavior.
        /// </summary>
        private static void CheckByRefPropertyGetHooks(
            PhpPropertyHookListAst? hooks,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (hooks is null || IsPhpVersionAtLeast(context.Options.PhpVersion, 8, 4))
            {
                return;
            }

            var targetVersion = string.IsNullOrWhiteSpace(context.Options.PhpVersion)
                ? "8.4"
                : context.Options.PhpVersion.Trim();

            foreach (var hook in hooks.GetAllNotNull())
            {
                if (!hook.ReturnsRef)
                {
                    continue;
                }

                var hookName = hook.Identifier?.Trim() ?? string.Empty;
                if (!string.Equals(hookName, "get", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    hook,
                    MessageCode.CheckerByRefPropertyGetHookRequiresPhp84,
                    targetVersion);
            }
        }

        private static bool IsPhpVersionAtLeast(string? version, int major, int minor)
        {
            if (!TryParsePhpVersion(version ?? "8.4", out var parsedMajor, out var parsedMinor))
            {
                return false;
            }

            return parsedMajor > major
                || (parsedMajor == major && parsedMinor >= minor);
        }

        private static bool TryParsePhpVersion(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], out major))
            {
                return false;
            }

            if (parts.Length >= 2)
            {
                if (!int.TryParse(parts[1], out minor))
                {
                    minor = 0;
                }
            }

            return true;
        }

        /// <summary>
        /// Reports when a child redeclares a <c>get</c>/<c>set</c> that an ancestor already marked
        /// <c>final</c> (FOUND_BUGS property-hook Medium #10). Mirrors
        /// <c>CheckMethodOverride</c> / <c>TryFindOverriddenMethod</c>, but walks per hook because
        /// PHP inherits <c>get</c> and <c>set</c> independently across partial overrides.
        /// </summary>
        private static void CheckPropertyHookFinalOverrides(
            string? propertyName,
            PhpPropertyHookListAst? hooks,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (hooks is null || string.IsNullOrEmpty(propertyName) || state.EnclosingObject is null)
            {
                return;
            }

            var bareProperty = propertyName.StartsWith('$') ? propertyName[1..] : propertyName;
            if (string.IsNullOrEmpty(bareProperty))
            {
                return;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                var hookName = hook.Identifier?.Trim() ?? string.Empty;
                if (!IsGetOrSetPropertyHook(hookName))
                {
                    continue;
                }

                if (TryFindFinalAncestorPropertyHookOwner(
                        bareProperty, hookName, state, context) is not { } ownerClass)
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    hook,
                    MessageCode.CheckerFinalPropertyHookOverridden,
                    ownerClass.Name,
                    bareProperty,
                    hookName.ToLowerInvariant());
            }
        }

        /// <summary>
        /// The nearest ancestor class that declares the same property hook marked <c>final</c>, or
        /// null when the override is legal (no ancestor hook, or nearest declaring ancestor is not
        /// final). Skips <c>private</c> properties the same way method override skips private methods.
        /// A plain (unhooked) property at a level breaks the hook-inheritance chain for that name.
        /// </summary>
        private static ObjectDeclarationSymbol? TryFindFinalAncestorPropertyHookOwner(
            string barePropertyName,
            string hookName,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.EnclosingObject is null)
            {
                return null;
            }

            var visited = new HashSet<ObjectDeclarationSymbol>();
            var parent = TypeComparer.TryGetParentDeclaration(
                state.EnclosingObject, context.SymbolTree, context.GlobalScope);

            while (parent is not null && visited.Add(parent))
            {
                if (TryGetObjectPropertyMember(parent, barePropertyName) is { } parentProperty)
                {
                    if ((parentProperty.Visibility & MemberModifier.Private) != 0)
                    {
                        parent = TypeComparer.TryGetParentDeclaration(
                            parent, context.SymbolTree, context.GlobalScope);
                        continue;
                    }

                    if (TryGetDeclaredPropertyHooks(parentProperty) is not { } parentHooks)
                    {
                        // Plain property at this level — no inherited hooks further up apply.
                        return null;
                    }

                    if (TryFindNamedPropertyHook(parentHooks, hookName) is { } parentHook)
                    {
                        return HookHasFinalModifier(parentHook) ? parent : null;
                    }

                    // Partial override: this level redeclared the property but not this hook —
                    // keep walking for the inherited hook.
                }

                parent = TypeComparer.TryGetParentDeclaration(
                    parent, context.SymbolTree, context.GlobalScope);
            }

            return null;
        }

        private static ObjectPropertySymbol? TryGetObjectPropertyMember(
            ObjectDeclarationSymbol objectDecl,
            string barePropertyName)
        {
            var withDollar = "$" + barePropertyName;
            if (objectDecl.Members.TryGetValue(withDollar, out var member)
                && member is ObjectPropertySymbol byDollar)
            {
                return byDollar;
            }

            if (objectDecl.Members.TryGetValue(barePropertyName, out member)
                && member is ObjectPropertySymbol byBare)
            {
                return byBare;
            }

            foreach (var candidate in objectDecl.Members.Values.OfType<ObjectPropertySymbol>())
            {
                var candidateBare = candidate.Name.StartsWith('$') ? candidate.Name[1..] : candidate.Name;
                if (string.Equals(candidateBare, barePropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static PhpPropertyHookListAst? TryGetDeclaredPropertyHooks(ObjectPropertySymbol property)
            => property.DeclaringAstNode switch
            {
                PhpPropertyAst prop => prop.Hooks,
                PhpParameterAst { PropertyHooks: PhpPropertyHookListAst hooks } => hooks,
                _ => null,
            };

        private static PhpPropertyHookAst? TryFindNamedPropertyHook(
            PhpPropertyHookListAst hooks,
            string hookName)
        {
            foreach (var hook in hooks.GetAllNotNull())
            {
                if (string.Equals(hook.Identifier?.Trim(), hookName, StringComparison.OrdinalIgnoreCase))
                {
                    return hook;
                }
            }

            return null;
        }

        private static bool IsGetOrSetPropertyHook(string hookName)
            => string.Equals(hookName, "get", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hookName, "set", StringComparison.OrdinalIgnoreCase);

        private static bool HookHasFinalModifier(PhpPropertyHookAst hook)
            => hook.Modifiers?.Modifiers.Contains(PhpModifier.Final) == true;

        private static string FormatPropertyHookModifierName(PhpModifier modifier) => modifier switch
        {
            PhpModifier.Public => "public",
            PhpModifier.Protected => "protected",
            PhpModifier.Private => "private",
            PhpModifier.Static => "static",
            PhpModifier.Abstract => "abstract",
            PhpModifier.Readonly => "readonly",
            PhpModifier.Var => "var",
            PhpModifier.PublicSet => "public(set)",
            PhpModifier.ProtectedSet => "protected(set)",
            PhpModifier.PrivateSet => "private(set)",
            _ => modifier.ToString().ToLowerInvariant(),
        };

        /// <summary>
        /// True when a property type involves object generic parameters or any generic type
        /// application (e.g. <c>\Closure&lt;bool&gt;</c>), which requires setPropertyType emission.
        /// </summary>
        private static bool TypeInvolvesGenericsForTracking(ITypeExpression typeExpr, CheckerState state)
        {
            if (typeExpr.AstGrammarAddons.TryGetValue("typeName", out var addon)
                && addon is PhpTypeExpressionListAst list
                && list.GetAllNotNull().Any())
            {
                return true;
            }

            if (typeExpr is PhpNamedTypeAst named)
            {
                if (named.Name is IBase2Ast nameNode
                    && nameNode.AstGrammarAddons.TryGetValue("typeName", out var nameAddon)
                    && nameAddon is PhpTypeExpressionListAst nameList
                    && nameList.GetAllNotNull().Any())
                {
                    return true;
                }

                return named.Name switch
                {
                    TyhpGenericIdentifierAst => true,
                    PhpNameAst name => IsObjectGenericName(name.ValueString, state),
                    ITypeExpression inner => TypeInvolvesGenericsForTracking(inner, state),
                    _ => false,
                };
            }

            if (typeExpr is TyhpGenericIdentifierAst)
            {
                return true;
            }

            if (typeExpr is PhpNameAst nameAst)
            {
                return IsObjectGenericName(nameAst.ValueString, state);
            }

            if (typeExpr is PhpTypeExpressionAst composite && composite.Types is { } members)
            {
                foreach (var member in members.GetAllNotNull())
                {
                    if (TypeInvolvesGenericsForTracking(member, state))
                    {
                        return true;
                    }
                }
            }

            foreach (var child in typeExpr.AstChildren)
            {
                if (child is ITypeExpression childType && TypeInvolvesGenericsForTracking(childType, state))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsObjectGenericName(string? name, CheckerState state)
        {
            var simple = name?.TrimStart('\\');
            return !string.IsNullOrEmpty(simple)
                && !simple.Contains('\\')
                && state.ObjectGenerics.Any(gp => string.Equals(gp.Name, simple, StringComparison.Ordinal));
        }

        private void RegisterParameters(
            PhpParameterListAst? parameterList,
            IReadOnlyList<ParameterInfo> symbolParameters,
            CheckerState funcState,
            CheckerState outerState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (parameterList is null)
            {
                return;
            }

            ValidateParameterList(parameterList, outerState, context, diagnostics);

            var parameters = parameterList.GetAllNotNull().ToList();
            for (var i = 0; i < parameters.Count; i++)
            {
                var paramAst = parameters[i];
                var paramInfo = i < symbolParameters.Count ? symbolParameters[i] : null;
                RegisterSingleParameter(paramAst, paramInfo, funcState, outerState, context, diagnostics);
            }
        }

        private static void ValidateParameterList(
            PhpParameterListAst parameterList,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var parameters = parameterList.GetAllNotNull().ToList();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sawOptional = false;

            for (var i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                if (!seenNames.Add(param.Name))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, param, MessageCode.CheckerDuplicateParameter, param.Name);
                }

                AttributeRule.ValidateDeclarationAttributes(param, state, context, diagnostics);

                var hasDefault = param.DefaultValue is not null;
                if (hasDefault)
                {
                    sawOptional = true;
                }
                else if (sawOptional)
                {
                    CheckerHelpers.ReportWarning(
                        diagnostics, state, param, MessageCode.CheckerRequiredAfterOptional, param.Name);
                }

                if (param.IsVariadic && i < parameters.Count - 1)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, param, MessageCode.CheckerVariadicNotLast, param.Name);
                }

                if (param.IsVariadic && hasDefault)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, param, MessageCode.CheckerVariadicWithDefault, param.Name);
                }

                if (param.Modifiers is not null && param.Type is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, param, MessageCode.CheckerPromotedPropertyNoType, param.Name);
                }

                if (param.IsVariadic && param.Modifiers is not null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, param, MessageCode.CheckerPromotedVariadic, param.Name);
                }

                if (param.Modifiers is not null
                    && (state.Modifiers & MemberModifier.Readonly) != 0
                    && (CheckerHelpers.ToMemberModifiers(param.Modifiers) & MemberModifier.Readonly) == 0)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, param, MessageCode.CheckerReadonlyClassMutableProperty, param.Name);
                }

                if (param.PropertyHooks is PhpPropertyHookListAst promotedHooks)
                {
                    ValidatePropertyHookModifiers(promotedHooks, state, diagnostics);
                    CheckByRefPropertyGetHooks(promotedHooks, state, context, diagnostics);
                    CheckPropertyHookFinalOverrides(param.Name, promotedHooks, state, context, diagnostics);

                    if (param.Modifiers is not null
                        && (CheckerHelpers.ToMemberModifiers(param.Modifiers) & MemberModifier.Readonly) != 0)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, param, MessageCode.CheckerHookedPropertyReadonly);
                    }

                    var promotedType = param.Type is not null
                        ? context.ResolveTypeAnnotation(param.Type, state)
                        : CheckedTypes.Mixed;
                    CheckPropertyHooks(promotedHooks, promotedType, state, context, diagnostics);
                }
            }
        }

        private void RegisterSingleParameter(
            PhpParameterAst paramAst,
            ParameterInfo? paramInfo,
            CheckerState funcState,
            CheckerState outerState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (paramAst.Type is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, outerState, paramAst, MessageCode.CheckerVariableTypeRequired, paramAst.Name);
            }

            ICheckedType paramType = CheckedTypes.Mixed;
            if (paramAst.Type is not null)
            {
                funcState.IsParameterTypePosition = true;
                paramType = context.ResolveTypeAnnotation(paramAst.Type, funcState);
                TypeDeclarationValidationRule.ValidateResolvedParameterType(
                    paramAst.Type, paramType, funcState, context, diagnostics);
                funcState.IsParameterTypePosition = false;
                ValidateParameterResolvedType(paramAst, paramType, funcState, diagnostics);
                // Type ASTs are not always CheckNode'd (and grammar addons are never walked by
                // CheckNode) — still count import usage for TYHP4130.
                context.MarkImportNames(paramAst.Type, outerState);
            }

            if (paramAst.DefaultValue is not null)
            {
                if (!CheckerHelpers.IsConstantExpression(paramAst.DefaultValue, funcState))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, outerState, paramAst, MessageCode.CheckerNonConstantExpression);
                }
                else
                {
                    var defaultType = context.ResolveExpressionType(paramAst.DefaultValue, funcState);
                    var bagChecked = StructBagLiteralChecker.TryCheck(
                        paramAst.DefaultValue, paramType, funcState, context, diagnostics);
                    if (!bagChecked && !context.IsAssignable(defaultType, paramType))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, outerState, paramAst, MessageCode.CheckerTypeMismatch,
                            defaultType.DisplayName, paramType.DisplayName);
                    }
                }
            }

            var variable = new VariableSymbol(paramAst.Name) { IsParameter = true, IsRef = paramAst.IsRef };

            // A variadic parameter (`T ...$args`) collects its arguments into an int-keyed array, so
            // inside the body the variable's type is `array<int, T>` rather than the declared element
            // type `T`. `__CallableParametersRest<T>` stores the positional bag instead.
            var variableType = paramAst.IsVariadic
                ? CallableSignatureReflection.VariadicParameterStorageType(paramType)
                : paramType;

            // Variable lookups normalize names to their bare form (no leading '$'), matching how
            // `$this`, catch variables, and locals are keyed. Parameter names arrive with the '$'
            // prefix, so strip it; otherwise every `$param` dereference would miss the seeded entry
            // and resolve to `unknown`.
            funcState.Variables[paramAst.Name.TrimStart('$')] = VariableState.ForParameter(variable, variableType, paramAst.IsRef);
            _ = paramInfo;
        }

        private static void ValidateParameterResolvedType(
            PhpParameterAst paramAst,
            ICheckedType paramType,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (paramType.Kind == CheckedTypeKind.Void || CheckerHelpers.IsBuiltInName(paramType, "void"))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, paramAst, MessageCode.CheckerVoidNotAllowedHere);
            }

            if (paramType.Kind == CheckedTypeKind.Never || CheckerHelpers.IsBuiltInName(paramType, "never"))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, paramAst, MessageCode.CheckerNeverNotAllowedHere);
            }
        }

        private static void ValidateMagicMethodIfNeeded(
            string methodName,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            bool isStatic,
            IBase2Ast node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!methodName.StartsWith("__", StringComparison.Ordinal))
            {
                return;
            }

            var paramCount = parameters?.GetAllNotNull().Count() ?? 0;
            var hasReturnType = returnType is not null;
            // Tyhp source conventionally spells `__construct`/`__destruct` with an explicit `: void`
            // (BuildMethodSignature erases it for PHP, which forbids any return type on either magic
            // method) — only a *non-void* return type is a real signature violation for them. Resolve
            // through the checker rather than pattern-matching the AST node: `__construct`'s `: void` is
            // parsed as a distinct `TyhpCtorReturnTypeAst` grammar addon (see tyhpCtorReturnType in the
            // grammar), not the ordinary builtin-type AST an inline syntactic check would expect.
            var hasNonVoidReturnType = hasReturnType
                && !CheckerHelpers.IsBuiltInName(
                    context.ResolveTypeAnnotation(returnType!, state, isReturnTypePosition: true), "void");

            var invalid = methodName.ToLowerInvariant() switch
            {
                "__construct" => hasNonVoidReturnType,
                "__destruct" => paramCount != 0 || hasNonVoidReturnType,
                "__clone" => paramCount != 0,
                "__tostring" => paramCount != 0,
                "__debuginfo" => paramCount != 0,
                "__get" => paramCount != 1,
                "__set" => paramCount != 2,
                "__isset" => paramCount != 1,
                "__unset" => paramCount != 1,
                "__call" => paramCount != 2,
                "__callstatic" => paramCount != 2 || !isStatic,
                "__sleep" => paramCount != 0,
                "__wakeup" => paramCount != 0,
                "__serialize" => paramCount != 0,
                "__unserialize" => paramCount != 1,
                "__set_state" => paramCount != 1 || !isStatic,
                _ when methodName.StartsWith("__", StringComparison.Ordinal) && isStatic
                    && methodName is not "__callStatic" and not "__set_state" => true,
                _ => false,
            };

            if (invalid)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerMagicMethodSignature, methodName,
                    "incorrect parameter count, return type, or static modifier");
            }

            if (methodName.Equals("__toString", StringComparison.OrdinalIgnoreCase)
                && returnType is not null)
            {
                var resolved = context.ResolveTypeAnnotation(returnType, state, isReturnTypePosition: true);
                if (!CheckerHelpers.IsBuiltInName(resolved, "string"))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, node, MessageCode.CheckerMagicMethodSignature, methodName,
                        "must declare a 'string' return type");
                }
            }
        }
    }
}
