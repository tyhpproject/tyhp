using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        /// <summary>
        /// After successfully adding a symbol to a scope via <c>AddChildSymbol</c>,
        /// also register it in <see cref="ObjectDeclarationSymbol.Members"/> so that
        /// name-resolution helpers (and conflict detection) can look up members by name.
        /// </summary>
        private static void RegisterObjectMember(ObjectDeclarationScope objScope, IBaseSymbol symbol, string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Operator overloads share a single name across multiple signatures, so the by-name
            // Members map (one entry per name) cannot represent them. They are enumerated from the
            // scope tree (GetAllChildSymbols) by the operator-overload resolver instead; registering
            // them here would silently drop all but the last overload.
            if (symbol is ObjectOperatorOverloadMethodSymbol) return;

            if (objScope.DeclarationSymbol is not ObjectDeclarationSymbol declSymbol) return;

            // PHP: class constants / enum cases are a separate (case-sensitive) namespace from
            // methods and properties — keep them out of the case-insensitive Members map.
            if (symbol is ObjectConstantSymbol)
                declSymbol.Constants[name] = symbol;
            else
                declSymbol.Members[name] = symbol;
        }

        partial void BindObjectBody(PhpObjectTypeDeclAst objDecl, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol)
        {
            Types.PopulateObject(objScope);

            if (objDecl.Body == null) return;

            var members = objDecl.Body.GetAllNotNull().ToList();
            var implementedMethodNames = OverloadSignatureHelper.CollectImplementedMethodNames(members);

            foreach (var member in members)
            {
                switch (member)
                {
                    case PhpMethodDeclAst methodDecl:
                        // Overload signatures are compile-time-only; only the implementation binds.
                        if (OverloadSignatureHelper.IsClassMethodOverloadSignature(methodDecl, implementedMethodNames))
                        {
                            break;
                        }

                        BindMethodDecl(methodDecl, objScope);
                        break;

                    case PhpPropertyDeclAst propDecl:
                        BindPropertyDecl(propDecl, objScope);
                        break;

                    case PhpConstDeclListAst constList:
                        BindObjectConstDecl(constList, objScope);
                        break;

                    case PhpEnumCaseAst enumCase:
                        BindEnumCase(enumCase, objScope, symbol);
                        break;

                    case TyhpOperatorOverloadAst opOverload:
                        BindOperatorOverload(opOverload, objScope);
                        break;

                    case TyhpTypeAliasAst typeAlias:
                    {
                        var aliasName = typeAlias.Name?.ValueString ?? typeAlias.Identifier ?? "";
                        if (!string.IsNullOrEmpty(aliasName))
                        {
                            var aliasSymbol = new ObjectTypeAliasSymbol(aliasName, sourceFile: _currentFileName);
                            aliasSymbol.AliasedType = typeAlias.TypeExpression;

                            if (!objScope.AddChildSymbol(aliasSymbol))
                            {
                                _diagnostics.AddErrorFromAst(
                                    MessageCode.BinderDuplicateSymbolDeclaration,
                                    typeAlias,
                                    _currentFileName,
                                    aliasSymbol.Name);
                            }
                            else
                            {
                                RegisterObjectMember(objScope, aliasSymbol, aliasName);
                            }
                        }
                        break;
                    }

                    case PhpTraitUseAst traitUse:
                        BindTraitUseBlock(traitUse, symbol);
                        break;

                    case UnexpectedNodeAst:
                    case ErrorAst:
                        break;
                }
            }
        }

        private void BindMethodDecl(PhpMethodDeclAst methodDecl, ObjectDeclarationScope objScope)
        {
            // Class overload signatures are filtered by IsClassMethodOverloadSignature before this
            // method is called. Short methods (`fn name(...) => expr;`) are already desugared to a
            // normal body at visit time and bind like any other method.

            // Tyhpdef methods may be `function php_name as tyhpName(...)` — the visitor stores the
            // name AST (with optional `aliasedAs` addon) under `nameOrAlias`. Prefer the Tyhp-facing
            // alias for the symbol name; CreateTyhpdefAlias is only for free-function/class maps, so
            // member emit erasure uses OriginalPhpName collected into TyhpdefMemberAliasMap.
            string name;
            string? originalPhpName = null;
            if (methodDecl.AstGrammarAddons.TryGetValue("nameOrAlias", out var nameOrAlias))
            {
                var (originalName, aliasName) = ExtractTyhpdefName(nameOrAlias);
                name = aliasName ?? originalName;
                if (!string.IsNullOrEmpty(aliasName)
                    && !string.Equals(aliasName, originalName, StringComparison.Ordinal))
                {
                    originalPhpName = originalName;
                }
            }
            else
            {
                name = methodDecl.Identifier ?? "";
            }

            var modifiers = ConvertModifiers(methodDecl.Modifiers);
            var isStatic = modifiers.HasFlag(MemberModifier.Static);
            var symbolType = DetermineMethodSymbolType(name, isStatic);

            ObjectMethodSymbol methodSymbol = symbolType switch
            {
                SymbolType.ObjectConstructor => new ObjectConstructorMethodSymbol(name, _currentFileName, methodDecl),
                SymbolType.ObjectDestructor => new ObjectDestructorMethodSymbol(name, methodDecl, _currentFileName),
                _ => new ObjectMethodSymbol(name, methodDecl, _currentFileName, modifiers, symbolType)
            };

            methodSymbol.OriginalPhpName = originalPhpName;
            methodSymbol.ReturnType = methodDecl.ReturnType;
            methodSymbol.IsAbstract = modifiers.HasFlag(MemberModifier.Abstract);
            methodSymbol.IsAsync = modifiers.HasFlag(MemberModifier.Async) || HasAsyncModifier(methodDecl);
            PopulateGenericParametersFromGrammarAddon(
                methodDecl.AstGrammarAddons,
                methodSymbol.GenericParameters,
                _currentFileName,
                SymbolType.FunctionGenericTypeParameter);

            if (methodDecl.Parameters != null)
            {
                foreach (var param in methodDecl.Parameters.GetAllNotNull())
                {
                    var paramModifiers = ConvertModifiers(param.Modifiers);
                    methodSymbol.Parameters.Add(new ParameterInfo(
                        param.ValueString ?? "",
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers
                    ));
                }
            }

            if (!objScope.AddChildSymbol(methodSymbol))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderDuplicateSymbolDeclaration,
                    methodDecl,
                    _currentFileName,
                    methodSymbol.Name);
            }
            else
            {
                RegisterObjectMember(objScope, methodSymbol, name);
            }

            if (SymbolTypeHelper.IsStaticMethodDeclarationScope(symbolType))
            {
                var staticScope = new StaticMethodDeclarationScope(objScope, methodSymbol);
                objScope.AddChildScope(staticScope);
                BindMethodParameters(methodDecl.Parameters, staticScope, methodSymbol, objScope);
                if (methodDecl.Body != null)
                {
                    BindStatementBlock(methodDecl.Body, staticScope);
                }
            }
            else
            {
                var instanceScope = new InstanceMethodDeclarationScope(objScope, methodSymbol);
                objScope.AddChildScope(instanceScope);
                BindMethodParameters(methodDecl.Parameters, instanceScope, methodSymbol, objScope);
                if (methodDecl.Body != null)
                {
                    BindStatementBlock(methodDecl.Body, instanceScope);
                }
            }
        }

        private void BindPropertyDecl(PhpPropertyDeclAst propDecl, ObjectDeclarationScope objScope)
        {
            var modifiers = ConvertModifiers(propDecl.Modifiers);
            var type = propDecl.Type;
            var isStatic = modifiers.HasFlag(MemberModifier.Static);

            if (propDecl.Properties == null) return;

            var allowsUnset = DeclarationHasAllowUnsetAttribute(propDecl);
            foreach (var prop in propDecl.Properties.GetAllNotNull())
            {
                var symbolType = isStatic ? SymbolType.StaticObjectProperty : SymbolType.InstanceObjectProperty;
                var propSymbol = new ObjectPropertySymbol(
                    prop.Identifier ?? "",
                    sourceFile: _currentFileName,
                    declaringNode: prop,
                    symbolType: symbolType,
                    visibility: modifiers
                );

                propSymbol.DeclaredType = type;
                propSymbol.DefaultValue = prop.DefaultValue;
                propSymbol.HasAccessor = prop.Hooks != null;
                propSymbol.AllowsUnset = allowsUnset
                    || DeclarationHasAllowUnsetAttribute(prop);

                if (!objScope.AddChildSymbol(propSymbol))
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderDuplicateSymbolDeclaration,
                        prop,
                        _currentFileName,
                        propSymbol.Name);
                }
                else
                {
                    RegisterObjectMember(objScope, propSymbol, prop.Identifier ?? "");
                }
            }
        }

        private void BindObjectConstDecl(PhpConstDeclListAst constList, ObjectDeclarationScope objScope)
        {
            foreach (var constDecl in constList.GetAllNotNull())
            {
                var name = constDecl.Identifier ?? "";

                if (string.IsNullOrEmpty(name))
                {
                    _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, constDecl, _currentFileName, "constant declaration identifier");
                    continue;
                }

                // Visibility / final come from the enclosing class-const statement (plumbed onto each
                // PhpConstDeclAst). Bare `const X` leaves MemberModifier.None, which PHP treats as public.
                var constSymbol = new ObjectConstantSymbol(
                    name,
                    sourceFile: _currentFileName,
                    declaringNode: constDecl,
                    visibility: ConvertModifiers(constDecl.Modifiers)
                );
                constSymbol.DeclaredType = constDecl.Type;

                if (!objScope.AddChildSymbol(constSymbol))
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderDuplicateSymbolDeclaration,
                        constDecl,
                        _currentFileName,
                        constSymbol.Name);
                }
                else
                {
                    RegisterObjectMember(objScope, constSymbol, name);
                }
            }
        }

        private void BindEnumCase(PhpEnumCaseAst enumCase, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol)
        {
            // The case name lives in the child PhpNameAst's token text (ValueString); its
            // Identifier slot is never populated for token-created names, so prefer ValueString.
            var name = !string.IsNullOrEmpty(enumCase.Name?.Identifier)
                ? enumCase.Name!.Identifier
                : enumCase.Name?.ValueString ?? "";

            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, enumCase, _currentFileName, "enum case identifier");
                return;
            }

            // Enum cases are always public in PHP; there is no visibility syntax on `case`.
            var constSymbol = new ObjectConstantSymbol(
                name,
                sourceFile: _currentFileName,
                declaringNode: enumCase,
                visibility: MemberModifier.Public
            );

            // An enum case's type is the enum itself (it is a singleton instance of the enum), not the
            // backing scalar type. Mark it so the checker resolves `Enum::Case` to the enum type.
            constSymbol.IsEnumCase = true;
            constSymbol.DeclaredType = symbol.ExtendsType;

            if (!objScope.AddChildSymbol(constSymbol))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderDuplicateSymbolDeclaration,
                    enumCase,
                    _currentFileName,
                    constSymbol.Name);
            }
            else
            {
                RegisterObjectMember(objScope, constSymbol, name);
            }
        }

        /// <summary>
        /// Builds a canonical signature string for an operator overload from its parameter types and
        /// return type, used to detect genuine duplicate operator declarations. Two overloads with
        /// the same operator conflict only when their full parameter- and return-type spellings match.
        /// </summary>
        private static string BuildOperatorOverloadSignature(ObjectOperatorOverloadMethodSymbol op)
        {
            var parameters = string.Join(",", op.Parameters.Select(p => GetTypeDisplayName(p.DeclaredType)));
            return $"{parameters}):{GetTypeDisplayName(op.ReturnType)}";
        }

        private void BindOperatorOverload(TyhpOperatorOverloadAst opOverload, ObjectDeclarationScope objScope)
        {
            var opName = opOverload.Identifier ?? opOverload.Op?.ValueString ?? "";

            if (string.IsNullOrEmpty(opName))
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, opOverload, _currentFileName, "operator overload name");
                return;
            }

            var declaringObj = objScope.DeclarationSymbol as ObjectDeclarationSymbol;
            var inExtensionBlock = declaringObj?.IsExtension == true;

            if (opOverload.ExtensionTargetType != null && !inExtensionBlock && !opOverload.IsInlineExtension)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.ExtensionOperatorTargetNotAllowed,
                    opOverload,
                    _currentFileName,
                    "Operator target type is only allowed inside extension declarations.");
                return;
            }

            if (inExtensionBlock && opOverload.ExtensionTargetType == null)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.ExtensionOperatorMissingTarget,
                    opOverload,
                    _currentFileName,
                    "Extension operator overloads require a <Type> target (e.g. operator +<MyType>(...)).");
                return;
            }

            ObjectDeclarationScope bindingScope = objScope;
            ObjectDeclarationSymbol? inlineOwnerClass = null;
            if (opOverload.IsInlineExtension && declaringObj != null && !inExtensionBlock)
            {
                // `extension operator` in tyhpdef requires a body (maps to methods / rewrite).
                // Bodyless `operator …;` (no `extension`) is the native PHP passthrough form.
                if (opOverload.Body == null)
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefExtensionOperatorRequiresBody,
                        opOverload,
                        _currentFileName,
                        opName);
                    return;
                }

                inlineOwnerClass = declaringObj;
                bindingScope = GetOrCreateSyntheticInlineExtensionScope(declaringObj, objScope);
            }

            var isUnary = opOverload.RightParameter == null;
            OverloadableOperator opEnum;
            if (opOverload.Op != null)
            {
                var opTok = opOverload.Op;
                opEnum = OverloadableOperatorHelper.FromToken(
                    (int)(opTok.ValueInt64 ?? 0L),
                    opTok.ValueString ?? "",
                    isAlternateKind: isUnary);
            }
            else
            {
                opEnum = OverloadableOperator.Invalid;
            }

            var methodSymbol = new ObjectOperatorOverloadMethodSymbol(opName, opEnum, _currentFileName);
            methodSymbol.ReturnType = opOverload.ReturnType;
            methodSymbol.IsExtensionOperator = inExtensionBlock || opOverload.IsInlineExtension;
            // Bodyless class-level tyhpdef `operator …;` = native PHP passthrough (type-check only).
            methodSymbol.IsNativePassthrough =
                !opOverload.IsInlineExtension && !inExtensionBlock && opOverload.Body == null;

            if (inExtensionBlock)
            {
                methodSymbol.PendingExtensionTargetType = opOverload.ExtensionTargetType;
                methodSymbol.DeclaringExtensionSymbol = declaringObj;
            }
            else if (opOverload.IsInlineExtension && inlineOwnerClass != null)
            {
                methodSymbol.ExtensionTargetSymbol = inlineOwnerClass;
                methodSymbol.DeclaringExtensionSymbol = bindingScope.DeclarationSymbol as ObjectDeclarationSymbol;
            }

            if (opOverload.LeftParameter != null)
            {
                var left = opOverload.LeftParameter;
                methodSymbol.Parameters.Add(new ParameterInfo(
                    left.ValueString ?? "",
                    left.Type,
                    left.DefaultValue,
                    left.IsVariadic,
                    left.IsRef,
                    MemberModifier.None
                ));
            }

            if (opOverload.RightParameter != null)
            {
                var right = opOverload.RightParameter;
                methodSymbol.Parameters.Add(new ParameterInfo(
                    right.ValueString ?? "",
                    right.Type,
                    right.DefaultValue,
                    right.IsVariadic,
                    right.IsRef,
                    MemberModifier.None
                ));
            }

            foreach (var mod in opOverload.Modifiers)
            {
                if (mod == PhpModifier.Abstract)
                    methodSymbol.IsAbstract = true;
            }

            if (opOverload.IsInlineExtension && inlineOwnerClass != null)
            {
                var newSignature = BuildOperatorOverloadSignature(methodSymbol);
                foreach (var existing in inlineOwnerClass.ExtensionContributedOperators)
                {
                    // Operators are overloaded by operand type (and, for `convert`, by return type),
                    // so only a fully-identical signature is a genuine conflict. Comparing just the
                    // operator and arity would wrongly reject legitimate type-distinguished overloads
                    // such as `+(self, self)` vs `+(self, float|int|string)` or the three
                    // `convert(self): int|float|string` forms.
                    if (existing.Operator == opEnum
                        && BuildOperatorOverloadSignature(existing) == newSignature)
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefExtensionConflict,
                            opOverload,
                            _currentFileName,
                            opName,
                            inlineOwnerClass.Name);
                        return;
                    }
                }
            }

            if (!bindingScope.AddChildSymbol(methodSymbol))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderDuplicateSymbolDeclaration,
                    opOverload,
                    _currentFileName,
                    methodSymbol.Name);
                return;
            }
            RegisterObjectMember(bindingScope, methodSymbol, opName);

            if (opOverload.IsInlineExtension && inlineOwnerClass != null)
                inlineOwnerClass.ExtensionContributedOperators.Add(methodSymbol);

            var staticScope = new StaticMethodDeclarationScope(bindingScope, methodSymbol);
            bindingScope.AddChildScope(staticScope);

            if (opOverload.LeftParameter != null)
            {
                var left = opOverload.LeftParameter;
                var leftVar = new VariableSymbol(left.ValueString ?? "", declaringNode: left, sourceFile: _currentFileName);
                leftVar.DeclaredType = left.Type;
                leftVar.IsParameter = true;
                leftVar.DefaultValue = left.DefaultValue;
                staticScope.AddChildSymbol(leftVar);
            }
            if (opOverload.RightParameter != null)
            {
                var right = opOverload.RightParameter;
                var rightVar = new VariableSymbol(right.ValueString ?? "", declaringNode: right, sourceFile: _currentFileName);
                rightVar.DeclaredType = right.Type;
                rightVar.IsParameter = true;
                rightVar.DefaultValue = right.DefaultValue;
                staticScope.AddChildSymbol(rightVar);
            }

            if (opOverload.Body != null)
            {
                BindStatementBlock(opOverload.Body, staticScope);
            }
        }

        private void BindMethodParameters(
            PhpParameterListAst? parameters,
            IBaseScope methodScope,
            ObjectMethodSymbol methodSymbol,
            ObjectDeclarationScope objScope)
        {
            if (parameters == null) return;

            var isConstructor = methodSymbol is ObjectConstructorMethodSymbol;

            foreach (var param in parameters.GetAllNotNull())
            {
                var paramName = param.ValueString ?? "";
                var paramModifiers = ConvertModifiers(param.Modifiers);

                var varSymbol = new VariableSymbol(
                    paramName,
                    declaringNode: param,
                    sourceFile: _currentFileName,
                    visibility: paramModifiers
                );

                varSymbol.DeclaredType = param.Type;
                varSymbol.IsParameter = true;
                varSymbol.DefaultValue = param.DefaultValue;

                if (isConstructor && param.Modifiers != null &&
                    (paramModifiers.HasFlag(MemberModifier.Public) ||
                     paramModifiers.HasFlag(MemberModifier.Protected) ||
                     paramModifiers.HasFlag(MemberModifier.Private) ||
                     paramModifiers.HasFlag(MemberModifier.Readonly)))
                {
                    varSymbol.IsPromotedProperty = true;

                    var propSymbolType = paramModifiers.HasFlag(MemberModifier.Static)
                        ? SymbolType.StaticObjectProperty
                        : SymbolType.InstanceObjectProperty;

                    // Properties live in a namespace distinct from methods, keyed by their declared
                    // name including the leading '$' (matching regular property declarations). The
                    // promoted parameter name is bare, so prefix it; otherwise a method of the same
                    // name (e.g. a `bool $isNullable` accessor) would shadow the property in the
                    // member table.
                    var propMemberName = paramName.StartsWith('$') ? paramName : "$" + paramName;

                    var propSymbol = new ObjectPropertySymbol(
                        propMemberName,
                        sourceFile: _currentFileName,
                        declaringNode: param,
                        symbolType: propSymbolType,
                        visibility: paramModifiers
                    );

                    propSymbol.DeclaredType = param.Type;
                    propSymbol.DefaultValue = param.DefaultValue;
                    propSymbol.AllowsUnset = DeclarationHasAllowUnsetAttribute(param);

                    if (objScope.AddChildSymbol(propSymbol))
                    {
                        RegisterObjectMember(objScope, propSymbol, propMemberName);
                        if (methodSymbol is ObjectConstructorMethodSymbol ctorSymbol)
                        {
                            ctorSymbol.PromotedProperties.Add(varSymbol);
                        }
                    }
                    else
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            param,
                            _currentFileName,
                            propSymbol.Name);
                    }
                }

                switch (methodScope)
                {
                    case InstanceMethodDeclarationScope instanceScope:
                        if (!instanceScope.AddChildSymbol(varSymbol))
                        {
                            _diagnostics.AddErrorFromAst(MessageCode.BinderDuplicateSymbolDeclaration, param, _currentFileName, paramName);
                        }
                        break;
                    case StaticMethodDeclarationScope staticScope:
                        if (!staticScope.AddChildSymbol(varSymbol))
                        {
                            _diagnostics.AddErrorFromAst(MessageCode.BinderDuplicateSymbolDeclaration, param, _currentFileName, paramName);
                        }
                        break;
                }
            }
        }

        private static SymbolType DetermineMethodSymbolType(string name, bool isStatic)
        {
            if (string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectConstructor;
            if (string.Equals(name, "__destruct", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectDestructor;
            if (string.Equals(name, "__call", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicCallMethod;
            if (string.Equals(name, "__callStatic", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicCallStaticMethod;
            if (string.Equals(name, "__get", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicGetMethod;
            if (string.Equals(name, "__set", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicSetMethod;
            if (string.Equals(name, "__isset", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicIssetMethod;
            if (string.Equals(name, "__unset", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicUnsetMethod;
            if (string.Equals(name, "__sleep", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicSleepMethod;
            if (string.Equals(name, "__wakeup", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicWakeupMethod;
            if (string.Equals(name, "__serialize", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicSerializeMethod;
            if (string.Equals(name, "__unserialize", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicUnserializeMethod;
            if (string.Equals(name, "__toString", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicToStringMethod;
            if (string.Equals(name, "__invoke", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicInvokeMethod;
            if (string.Equals(name, "__set_state", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicSetStateMethod;
            if (string.Equals(name, "__clone", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicCloneMethod;
            if (string.Equals(name, "__debugInfo", StringComparison.OrdinalIgnoreCase)) return SymbolType.ObjectMagicDebugInfoMethod;
            return isStatic ? SymbolType.StaticObjectMethod : SymbolType.InstanceObjectMethod;
        }

        /// <summary>
        /// Binds a trait use block by adding trait names to the object symbol's implements list
        /// and processing any trait adaptations.
        /// </summary>
        private void BindTraitUseBlock(PhpTraitUseAst traitUse, ObjectDeclarationSymbol symbol)
        {
            if (traitUse.TraitNames != null)
            {
                var existingTraits = new HashSet<string>(
                    symbol.ImplementsTypes
                        .Select(t => t.Identifier)
                        .Where(id => !string.IsNullOrEmpty(id))!,
                    StringComparer.OrdinalIgnoreCase);

                foreach (var traitName in traitUse.TraitNames.GetAllNotNull())
                {
                    // Trait name list items are IClassName (PhpNameAst), not ITypeExpression —
                    // wrap so ImplementsTypes (used by __UsedTraitName existence checks) retains them.
                    var typeExpr = AsTypeExpression(traitName, traitUse);
                    if (typeExpr is null)
                    {
                        continue;
                    }

                    var typeExprId = typeExpr.Identifier;
                    var isDuplicate = !string.IsNullOrEmpty(typeExprId)
                        ? !existingTraits.Add(typeExprId)
                        : symbol.ImplementsTypes.Contains(typeExpr);

                    if (!isDuplicate)
                    {
                        symbol.ImplementsTypes.Add(typeExpr);
                    }
                }
            }

            ProcessTraitAdaptations(traitUse, symbol);
        }

        /// <summary>
        /// Coerces a class-name list item to <see cref="ITypeExpression"/>.
        /// <see cref="PhpClassNameListAst"/> yields <see cref="IClassName"/> nodes
        /// (<see cref="PhpNameAst"/>), which are expressions but not type expressions.
        /// </summary>
        private static ITypeExpression? AsTypeExpression(IBase2Ast node, Base2Ast contextSource)
        {
            if (node is ITypeExpression typeExpr)
            {
                return typeExpr;
            }

            if (node is IExpression nameExpr)
            {
                return PhpNamedTypeAst.WrapClassName(nameExpr, contextSource);
            }

            return null;
        }

        /// <summary>
        /// Processes trait adaptation rules (insteadof / as) from a trait use statement.
        /// </summary>
        private static void ProcessTraitAdaptations(PhpTraitUseAst traitUse, ObjectDeclarationSymbol symbol)
        {
            if (traitUse.Adaptations == null) return;

            foreach (var adaptation in traitUse.Adaptations.GetAllNotNull())
            {
                switch (adaptation)
                {
                    case PhpTraitPrecedenceAst precedence:
                    {
                        var methodRef = precedence.MethodReference;
                        var methodName = GetTraitAdaptationName(methodRef?.MemberName);
                        var preferredTrait = GetTraitAdaptationName(methodRef?.TraitName);

                        if (!string.IsNullOrEmpty(methodName) && !string.IsNullOrEmpty(preferredTrait))
                        {
                            symbol.TraitMethodPrecedence ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            symbol.TraitMethodPrecedence[methodName] = preferredTrait;
                        }
                        break;
                    }
                    case PhpTraitAliasAst alias:
                    {
                        var aliasName = alias.Identifier;
                        var methodRef = alias.MethodReference;
                        var originalMethod = GetTraitAdaptationName(methodRef?.MemberName);
                        var traitNameStr = GetTraitAdaptationName(methodRef?.TraitName);

                        if (!string.IsNullOrEmpty(aliasName) && !string.IsNullOrEmpty(originalMethod))
                        {
                            symbol.TraitMethodAliases ??= new Dictionary<string, (string?, string)>(StringComparer.OrdinalIgnoreCase);
                            symbol.TraitMethodAliases[aliasName] =
                                (string.IsNullOrEmpty(traitNameStr) ? null : traitNameStr, originalMethod);
                        }
                        break;
                    }
                }
            }
        }

        // Trait/member names inside an adaptation rule are PhpNameAst nodes, which carry the text in
        // ValueString and leave the (non-nullable) Identifier empty — so a null-coalescing fallback
        // never reaches ValueString.
        private static string? GetTraitAdaptationName(IBase2Ast? node) =>
            node is null
                ? null
                : !string.IsNullOrEmpty(node.Identifier) ? node.Identifier : node.ValueString;

        /// <summary>
        /// Prop-init #8: <c>#[\Tyhp\AllowUnset]</c> (or unqualified <c>AllowUnset</c>) on a property
        /// declaration or promoted constructor parameter.
        /// </summary>
        private static bool DeclarationHasAllowUnsetAttribute(IBase2Ast? node)
        {
            if (node is null)
            {
                return false;
            }

            foreach (var attribute in node.AstAttributes)
            {
                if (attribute is not PhpAttributeAst attr)
                {
                    continue;
                }

                var name = attr.Name switch
                {
                    PhpNameAst n => n.ValueString,
                    TokenValueAst t => t.ValueString,
                    IExpression e => e.Identifier,
                    _ => null,
                };

                if (name is not null
                    && (string.Equals(name, "AllowUnset", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("\\AllowUnset", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
