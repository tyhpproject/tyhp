using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Story 11 Phase 8 — <c>GenericObject</c> runtime tracking emission helpers.
    /// </summary>
    public partial class TyhpEmitter
    {
        private const string RuntimeTypeClassFq = "\\Tyhp\\Type";
        private const string RuntimeNamedTypeFq = "\\Tyhp\\NamedType";
        private const string HasGenericsTraitFq = "\\Tyhp\\Concerns\\HasGenerics";

        private void BeginGenericObjectObjectScope(PhpObjectTypeDeclAst objectDecl)
        {
            this._currentObjectDecl = objectDecl;
            this._currentObjectSymbol = objectDecl.BoundSymbol as ObjectDeclarationSymbol;
            this._currentObjectGenericParams = this._currentObjectSymbol?.GenericParameters
                ?? (IReadOnlyList<GenericTypeParameterSymbol>)Array.Empty<GenericTypeParameterSymbol>();
            this._currentObjectGenericParamNames.Clear();
            foreach (var gp in this._currentObjectGenericParams)
            {
                this._currentObjectGenericParamNames.Add(gp.Name);
            }

            this._currentObjectNeedsGenericTracking =
                this._currentObjectSymbol is not null
                && this._currentObjectGenericParams.Count > 0
                && this._context.RequiresRuntimeGenericTrackingFor(this._currentObjectSymbol);
            this._currentObjectEmittedConstructor = false;
            this._ctorGenericLocalVars = null;
            this.ResolveGenericChainState(objectDecl);
        }

        private void EmitGenericObjectTraitUseIfNeeded(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
        {
            if (!this.ShouldApplyGenericObjectTrait())
            {
                return;
            }

            if (ObjectAlreadyUsesGenericObjectTrait(objectDecl))
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            // HasGenerics composes BootsTraits for bag creation in __bootTrait_*.
            EmitItem.Line(
                objectDecl,
                EmitType.ObjectTraitUse,
                $"use {HasGenericsTraitFq};",
                classBlock);
        }

        private static bool ObjectAlreadyUsesGenericObjectTrait(PhpObjectTypeDeclAst objectDecl)
        {
            foreach (var member in objectDecl.Body?.GetAllNotNull() ?? [])
            {
                if (member is not PhpTraitUseAst traitUse)
                {
                    continue;
                }

                foreach (var traitName in traitUse.TraitNames?.GetAllNotNull() ?? [])
                {
                    var text = traitName.Identifier ?? (traitName as PhpNameAst)?.ValueString ?? "";
                    var normalized = text.TrimStart('\\');
                    if (string.Equals(normalized, "Tyhp\\Concerns\\HasGenerics", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "Concerns\\HasGenerics", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "HasGenerics", StringComparison.OrdinalIgnoreCase)
                        // Legacy name from before the GenericObject class split.
                        || string.Equals(normalized, "Tyhp\\Concerns\\GenericObject", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "Concerns\\GenericObject", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "GenericObject", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EmitSynthesizedGenericObjectConstructorIfNeeded(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
            => this.EmitSynthesizedGenericConstructor(objectDecl, classBlock);

        /// <summary>
        /// Formats a constructor parameter list. Under Mechanism C generic arguments never share a
        /// parameter list with the author's — they travel to <c>__initGenerics__tyhpGeneric</c>, whose
        /// own list carries nothing else — so a trailing variadic stays trailing where PHP requires it
        /// and no parameter position is contested.
        /// </summary>
        private string BuildConstructorParameterList(PhpParameterListAst? parameters)
            => this.FormatParameterList(parameters);

        /// <summary>
        /// Emits the one statement injected at the top of every constructor in a generic chain: bind
        /// this level's generics unless an initialization chain has already completed on this object.
        /// The gate is what keeps an ancestor constructor reached through
        /// <c>parent::__construct(...)</c> from re-walking a chain that is already bound.
        /// </summary>
        private void EmitGenericObjectConstructorPrologue(
            IBase2Ast provider,
            EmitItem methodBlock,
            bool includeUserParamChecks)
        {
            if (!this._currentObjectInGenericChain)
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            var ownParamCount = this.OwnRecordedGenericParameters().Count;

            // `self::` pins the call to this level's own hook. `$this->` would dispatch virtually to the
            // most-derived override, handing one level's argument list to a different level's
            // parameters.
            var nulls = string.Join(", ", Enumerable.Repeat("null", ownParamCount));
            EmitItem.MultiLine(
                provider,
                EmitType.FunctionStatement,
                [
                    "$this->tyhpBootTraits();",
                    "if ($this->__tyhpGeneric->needsInit()) {",
                    $"    self::{GeneratedNames.GenericInitHook}({nulls});",
                    "}",
                ],
                methodBlock);

            if (includeUserParamChecks && this._context.IsRuntimeGenericChecks())
            {
                this.EmitRuntimeGenericParamChecks(provider, methodBlock, preferCtorLocals: false);
            }
        }

        private IEnumerable<(string Name, ITypeExpression Type)> CollectGenericTypedProperties()
        {
            if (this._currentObjectDecl?.Body is null)
            {
                yield break;
            }

            foreach (var member in this._currentObjectDecl.Body.GetAllNotNull())
            {
                if (member is PhpPropertyDeclAst propertyDecl && propertyDecl.Type is { } propType)
                {
                    if (!this.TypeAstInvolvesGenerics(propType))
                    {
                        continue;
                    }

                    foreach (var prop in propertyDecl.Properties?.GetAllNotNull() ?? [])
                    {
                        var name = prop.Identifier?.TrimStart('$') ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            yield return (name, propType);
                        }
                    }
                }

                if (member is PhpMethodDeclAst method
                    && string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var param in method.Parameters?.GetAllNotNull() ?? [])
                    {
                        if (param.Modifiers is null || param.Type is null)
                        {
                            continue;
                        }

                        if (!this.TypeAstInvolvesGenerics(param.Type))
                        {
                            continue;
                        }

                        var name = param.Name.TrimStart('$');
                        if (!string.IsNullOrEmpty(name))
                        {
                            yield return (name, param.Type);
                        }
                    }
                }
            }
        }

        private bool TypeAstInvolvesGenerics(ITypeExpression typeExpr)
        {
            if (HasGenericTypeArgumentAddon(typeExpr))
            {
                return true;
            }

            if (typeExpr is PhpNamedTypeAst named)
            {
                return named.Name switch
                {
                    TyhpGenericIdentifierAst => true,
                    PhpNameAst name => this.IsObjectGenericParamName(name.ValueString)
                        || HasGenericTypeArgumentAddon(name),
                    ITypeExpression innerType => this.TypeAstInvolvesGenerics(innerType),
                    _ => false,
                };
            }

            if (typeExpr is TyhpGenericIdentifierAst)
            {
                return true;
            }

            if (typeExpr is PhpNameAst nameAst)
            {
                return this.IsObjectGenericParamName(nameAst.ValueString)
                    || HasGenericTypeArgumentAddon(nameAst);
            }

            if (typeExpr is PhpTypeExpressionAst composite && composite.Types is { } members)
            {
                foreach (var member in members.GetAllNotNull())
                {
                    if (this.TypeAstInvolvesGenerics(member))
                    {
                        return true;
                    }
                }
            }

            foreach (var child in typeExpr.AstChildren)
            {
                if (child is ITypeExpression childType && this.TypeAstInvolvesGenerics(childType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasGenericTypeArgumentAddon(IBase2Ast node)
            => GetGenericTypeArgumentAddon(node) is { Count: > 0 };

        private static IReadOnlyList<ITypeExpression>? GetGenericTypeArgumentAddon(IBase2Ast node)
        {
            // Type positions use "typeName"; class-name / instanceof / new-variable forms use
            // "identifier" (classNameIdentifierGrammarAddon). Either may wrap a single
            // PhpTypeExpressionAst layer around the real argument list.
            foreach (var key in (string[])["typeName", "identifier"])
            {
                if (!node.AstGrammarAddons.TryGetValue(key, out var addon)
                    || addon is not PhpTypeExpressionListAst list)
                {
                    continue;
                }

                var args = FlattenTypeArgumentList(list);
                if (args.Count > 0)
                {
                    return args;
                }
            }

            return null;
        }

        /// <summary>
        /// Unwraps a grammar type-argument list that may be a single <see cref="PhpTypeExpressionAst"/>
        /// whose members are the real arguments (instanceof / classNameIdentifier shape).
        /// </summary>
        private static List<ITypeExpression> FlattenTypeArgumentList(PhpTypeExpressionListAst list)
        {
            var raw = list.GetAllNotNull().ToList();
            if (raw.Count == 1
                && raw[0] is PhpTypeExpressionAst { Types: PhpTypeExpressionListAst inner })
            {
                var innerArgs = inner.GetAllNotNull().ToList();
                if (innerArgs.Count > 0)
                {
                    return innerArgs;
                }
            }

            return raw;
        }

        private bool IsObjectGenericParamName(string? name)
        {
            var simple = name?.TrimStart('\\');
            return !string.IsNullOrEmpty(simple)
                && !simple.Contains('\\')
                && this._currentObjectGenericParamNames.Contains(simple);
        }

        private void PushCallableGenericParamNames(IBaseSymbol? callableSymbol)
        {
            this._currentCallableGenericParamNames.Clear();
            var generics = callableSymbol switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => null,
            };
            if (generics is null)
            {
                return;
            }

            foreach (var gp in generics)
            {
                if (!string.IsNullOrEmpty(gp.Name))
                {
                    this._currentCallableGenericParamNames.Add(gp.Name);
                }
            }
        }

        private void EmitRuntimeGenericParamChecks(
            IBase2Ast provider,
            EmitItem methodBlock,
            bool preferCtorLocals)
        {
            if (provider is not PhpMethodDeclAst method || method.Parameters is null)
            {
                return;
            }

            foreach (var param in method.Parameters.GetAllNotNull())
            {
                if (param.Type is null || !this.TypeAstInvolvesGenerics(param.Type))
                {
                    continue;
                }

                var expected = this.BuildRuntimeTypeExpression(param.Type, preferCtorLocals);
                var varName = param.Name.StartsWith('$') ? param.Name : "$" + param.Name;
                EmitItem.Line(
                    param,
                    EmitType.FunctionStatement,
                    $"{RuntimeTypeClassFq}::check({varName}, {expected});",
                    methodBlock);
            }
        }

        /// <summary>
        /// Builds a <c>\Tyhp\Type::…</c> expression for runtime GenericObject registration / checks.
        /// </summary>
        private string BuildRuntimeTypeExpression(ITypeExpression? typeExpr, bool preferCtorLocals)
        {
            if (typeExpr is null)
            {
                return $"{RuntimeTypeClassFq}::mixed()";
            }

            var inner = this.BuildRuntimeTypeExpressionCore(typeExpr, preferCtorLocals);
            if (typeExpr is PhpTypeExpressionAst { IsNullable: true }
                && !inner.StartsWith($"{RuntimeTypeClassFq}::nullable(", StringComparison.Ordinal)
                && !inner.StartsWith($"{RuntimeTypeClassFq}::null(", StringComparison.Ordinal)
                && !inner.StartsWith($"{RuntimeTypeClassFq}::mixed(", StringComparison.Ordinal))
            {
                return $"{RuntimeTypeClassFq}::nullable({inner})";
            }

            return inner;
        }

        private string BuildRuntimeTypeExpressionCore(ITypeExpression typeExpr, bool preferCtorLocals)
        {
            if (typeExpr is PhpNamedTypeAst named)
            {
                // A free type parameter is not a PHP class — never emit `Type::fromClassName(T::class)`.
                // Prefer Mechanism D / GenericObject lookups when the emit context can reify the
                // binding; otherwise erase to `mixed` (FOUND #1b related type-arg spill).
                if (TryGetGenericTypeParameterSymbol(named) is { } namedGenericParam)
                {
                    return this.BuildRuntimeGenericParameterType(namedGenericParam.Name, preferCtorLocals);
                }

                if (TryGetSimpleTypeNameFromNamed(named) is { } unboundSimple
                    && this.IsErasedGenericParamName(unboundSimple))
                {
                    return this.BuildRuntimeGenericParameterType(unboundSimple, preferCtorLocals);
                }

                var typeArgs = GetGenericTypeArgumentAddon(named)
                    ?? (named.Name is IBase2Ast nameNode ? GetGenericTypeArgumentAddon(nameNode) : null);
                if (typeArgs is { Count: > 0 })
                {
                    var className = ResolveRuntimeClassName(
                        named.BoundSymbol,
                        named.Name,
                        written: named.Name switch
                        {
                            TyhpGenericIdentifierAst g => g.ValueString,
                            PhpNameAst n => n.ValueString,
                            _ => null,
                        });
                    return this.BuildRuntimeGenericFromClassAndArgs(className, typeArgs, preferCtorLocals);
                }

                return named.Name switch
                {
                    TyhpGenericIdentifierAst g => this.BuildRuntimeGenericType(g, preferCtorLocals),
                    PhpBuiltinTypeAst b => this.BuildRuntimeTypeExpressionCore(b, preferCtorLocals),
                    PhpNameAst n => this.BuildRuntimeNameType(n, preferCtorLocals),
                    ITypeExpression inner => this.BuildRuntimeTypeExpression(inner, preferCtorLocals),
                    _ => $"{RuntimeTypeClassFq}::mixed()",
                };
            }

            if (typeExpr is PhpBuiltinTypeAst builtin)
            {
                var id = builtin.Identifier ?? "mixed";
                return ScalarTypeFactoryNames.Contains(id)
                    ? $"{RuntimeTypeClassFq}::{id}()"
                    : $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(id)}::class)";
            }

            if (typeExpr is TyhpGenericIdentifierAst generic)
            {
                return this.BuildRuntimeGenericType(generic, preferCtorLocals);
            }

            if (typeExpr is PhpNameAst name)
            {
                var nameTypeArgs = GetGenericTypeArgumentAddon(name);
                if (nameTypeArgs is { Count: > 0 })
                {
                    var className = ResolveRuntimeClassName(
                        name.BoundSymbol, name, written: name.ValueString);
                    return this.BuildRuntimeGenericFromClassAndArgs(
                        className, nameTypeArgs, preferCtorLocals);
                }

                return this.BuildRuntimeNameType(name, preferCtorLocals);
            }

            if (typeExpr is PhpTypeExpressionAst composite && composite.Types is { } members)
            {
                var parts = members.GetAllNotNull()
                    .Select(m => this.BuildRuntimeTypeExpression(m, preferCtorLocals))
                    .ToList();
                if (parts.Count == 0)
                {
                    return $"{RuntimeTypeClassFq}::mixed()";
                }

                if (parts.Count == 1)
                {
                    var single = parts[0];
                    return composite.IsNullable
                        ? $"{RuntimeTypeClassFq}::nullable({single})"
                        : single;
                }

                var kind = composite.TypeKind switch
                {
                    PhpTypeKind.Intersection => "intersection",
                    _ => "union",
                };
                return $"{RuntimeTypeClassFq}::{kind}({string.Join(", ", parts)})";
            }

            var spelled = this.BuildTypeExpression(typeExpr).TrimStart('?');
            if (string.IsNullOrWhiteSpace(spelled) || spelled == "mixed")
            {
                return $"{RuntimeTypeClassFq}::mixed()";
            }

            if (ScalarTypeFactoryNames.Contains(spelled))
            {
                return $"{RuntimeTypeClassFq}::{spelled}()";
            }

            return $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(spelled)}::class)";
        }

        private string BuildRuntimeNameType(PhpNameAst name, bool preferCtorLocals)
        {
            var simple = (name.ValueString ?? "").TrimStart('\\');
            if (string.IsNullOrEmpty(simple))
            {
                return $"{RuntimeTypeClassFq}::mixed()";
            }

            if (name.BoundSymbol is GenericTypeParameterSymbol
                || this.IsErasedGenericParamName(simple))
            {
                return this.BuildRuntimeGenericParameterType(simple, preferCtorLocals);
            }

            if (ScalarTypeFactoryNames.Contains(simple))
            {
                return $"{RuntimeTypeClassFq}::{simple}()";
            }

            var className = ResolveRuntimeClassName(name.BoundSymbol, name, written: name.ValueString);
            return $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(className)}::class)";
        }

        /// <summary>
        /// True when <paramref name="simpleName"/> names a generic parameter known in the current
        /// emit context (object, Mechanism D binder, or enclosing callable) and must not be spelled
        /// as a PHP class name.
        /// </summary>
        private bool IsErasedGenericParamName(string? simpleName)
        {
            var simple = simpleName?.TrimStart('\\');
            return !string.IsNullOrEmpty(simple)
                && !simple.Contains('\\')
                && (this.IsVariantGenericParamName(simple)
                    || this._currentObjectGenericParamNames.Contains(simple)
                    || this._currentCallableGenericParamNames.Contains(simple));
        }

        /// <summary>
        /// Runtime <c>\Tyhp\Type</c> for a generic type parameter name: Mechanism D capture, class
        /// GenericObject lookup, or erased <c>mixed</c> when the binding is unavailable here.
        /// </summary>
        private string BuildRuntimeGenericParameterType(string paramName, bool preferCtorLocals)
        {
            if (this.IsVariantGenericParamName(paramName))
            {
                return this.BuildVariantTypeofLookup(paramName);
            }

            if (this._currentObjectGenericParamNames.Contains(paramName))
            {
                return this.BuildRuntimeGenericParamTypeLookup(paramName, preferCtorLocals);
            }

            return $"{RuntimeTypeClassFq}::mixed()";
        }

        private static GenericTypeParameterSymbol? TryGetGenericTypeParameterSymbol(PhpNamedTypeAst named)
        {
            if (named.BoundSymbol is GenericTypeParameterSymbol fromNamed)
            {
                return fromNamed;
            }

            return named.Name switch
            {
                // TyhpGenericIdentifierAst subclasses PhpNameAst — one arm covers both.
                PhpNameAst { BoundSymbol: GenericTypeParameterSymbol fromName } => fromName,
                _ => null,
            };
        }

        private static string? TryGetSimpleTypeNameFromNamed(PhpNamedTypeAst named)
        {
            var text = named.Name switch
            {
                TyhpGenericIdentifierAst g => g.ValueString,
                PhpNameAst n => n.ValueString,
                _ => null,
            };
            var simple = text?.TrimStart('\\');
            return string.IsNullOrEmpty(simple) || simple.Contains('\\') || simple.Contains('<')
                ? null
                : simple;
        }

        private string BuildRuntimeGenericParamTypeLookup(string paramName, bool preferCtorLocals)
        {
            if (preferCtorLocals
                && this._ctorGenericLocalVars is not null
                && this._ctorGenericLocalVars.TryGetValue(paramName, out var local))
            {
                // Inside the init hook the resolved argument is already a bare Type, not a NamedType.
                return "$" + local;
            }

            return this.BuildGenericResolvedTypeLookupCall(paramName) is { } lookup
                ? $"$this->{lookup}"
                : $"{RuntimeTypeClassFq}::mixed()";
        }

        /// <summary>
        /// The trait lookup for one of the current class's own generic parameters, keyed by the class
        /// that declared it. <see cref="_currentObjectGenericParamNames"/> only ever holds this class's
        /// own parameters, so the declaring class is always the class being emitted.
        ///
        /// Null when there is no enclosing class to key on, which leaves callers to fall back to the
        /// erased answer. A key has to be a literal class name, and the alternatives — <c>static::class</c>
        /// or <c>self::class</c> — are both a PHP fatal outside a class scope, so there is nothing valid
        /// to emit.
        ///
        /// Prefer <see cref="BuildGenericResolvedTypeLookupCall"/> when the caller needs the underlying
        /// <c>Type</c>; keep this form when the <c>NamedType</c> itself is required (e.g.
        /// <c>new ($this-&gt;…-&gt;getUnderlyingType()-&gt;getName())</c>).
        /// </summary>
        private string? BuildGenericTypeLookupCall(string paramName)
        {
            if (this._currentObjectFqn is not { } fqn)
            {
                return null;
            }

            return $"__tyhpGeneric->genericType(\\{fqn.TrimStart('\\')}::class, '{paramName}')";
        }

        /// <summary>
        /// Resolved underlying <c>Type</c> for a class generic parameter (or <c>mixed</c> when unbound).
        /// </summary>
        private string? BuildGenericResolvedTypeLookupCall(string paramName)
        {
            if (this._currentObjectFqn is not { } fqn)
            {
                return null;
            }

            return $"__tyhpGeneric->resolvedType(\\{fqn.TrimStart('\\')}::class, '{paramName}')";
        }

        /// <summary>
        /// Zero value for a bound class generic parameter — what <c>default(T)</c> evaluates to.
        /// </summary>
        private string? BuildGenericDefaultValueLookupCall(string paramName)
        {
            if (this._currentObjectFqn is not { } fqn)
            {
                return null;
            }

            return $"__tyhpGeneric->defaultValue(\\{fqn.TrimStart('\\')}::class, '{paramName}')";
        }

        /// <summary>
        /// True when <paramref name="typeExpr"/> is a free object generic parameter type
        /// (<c>T</c> or <c>?T</c>), not a parameterized type such as <c>Promise&lt;T&gt;</c> or
        /// <c>array&lt;T&gt;</c>, and not a multi-member union/intersection.
        /// </summary>
        private bool IsFreeObjectGenericPropertyType(ITypeExpression? typeExpr)
        {
            if (typeExpr is null)
            {
                return false;
            }

            if (typeExpr is PhpTypeExpressionAst composite && composite.Types is { } members)
            {
                var list = members.GetAllNotNull().ToList();
                if (list.Count != 1)
                {
                    return false;
                }

                return this.IsFreeObjectGenericPropertyType(list[0]);
            }

            if (HasGenericTypeArgumentAddon(typeExpr))
            {
                return false;
            }

            if (typeExpr is PhpNamedTypeAst named)
            {
                if (named.Name is IBase2Ast nameNode && HasGenericTypeArgumentAddon(nameNode))
                {
                    return false;
                }

                return named.Name switch
                {
                    TyhpGenericIdentifierAst g => this.IsObjectGenericParamName(g.ValueString)
                        && !HasGenericTypeArgumentAddon(g),
                    PhpNameAst n => this.IsObjectGenericParamName(n.ValueString)
                        && !HasGenericTypeArgumentAddon(n),
                    ITypeExpression inner => this.IsFreeObjectGenericPropertyType(inner),
                    _ => false,
                };
            }

            if (typeExpr is TyhpGenericIdentifierAst genericId)
            {
                return this.IsObjectGenericParamName(genericId.ValueString)
                    && !HasGenericTypeArgumentAddon(genericId);
            }

            if (typeExpr is PhpNameAst nameAst)
            {
                return this.IsObjectGenericParamName(nameAst.ValueString)
                    && !HasGenericTypeArgumentAddon(nameAst);
            }

            return false;
        }

        private string BuildRuntimeGenericType(TyhpGenericIdentifierAst generic, bool preferCtorLocals)
        {
            var className = ResolveRuntimeClassName(generic.BoundSymbol, generic, written: generic.ValueString);
            var typeArgs = new List<ITypeExpression>();
            if (generic.GenericArguments is PhpTypeExpressionListAst list)
            {
                typeArgs.AddRange(list.GetAllNotNull());
            }

            return this.BuildRuntimeGenericFromClassAndArgs(className, typeArgs, preferCtorLocals);
        }

        /// <summary>
        /// Class name for <c>Type::generic</c> / <c>fromClassName</c>: prefer the bound symbol's FQCN
        /// so same-namespace unqualified names (e.g. <c>Deferred</c> in <c>namespace Tyhp</c>) emit
        /// <c>\Tyhp\Deferred::class</c>, not <c>\Deferred::class</c> (FOUND #1e).
        /// </summary>
        private string ResolveRuntimeClassName(
            IBaseSymbol? hostBound,
            IBase2Ast? nameNode,
            string? written)
        {
            if (TryGetBoundObjectFqn(hostBound) is { } fromHost)
            {
                return fromHost;
            }

            if (nameNode is PhpNameAst name && TryGetBoundObjectFqn(name.BoundSymbol) is { } fromName)
            {
                return fromName;
            }

            var spelling = (written ?? "object").TrimStart('\\');
            if (string.IsNullOrEmpty(spelling))
            {
                return "object";
            }

            // Unqualified spelling with no BoundSymbol: last-chance resolve via the global scope.
            if (!spelling.Contains('\\')
                && this.TryResolveObjectByName(spelling) is { FullyQualifiedName: { Length: > 0 } fqn })
            {
                return fqn.TrimStart('\\');
            }

            return spelling;
        }

        private static string? TryGetBoundObjectFqn(IBaseSymbol? symbol)
        {
            if (symbol is null
                || symbol is GenericTypeParameterSymbol
                || symbol is TypeAliasSymbol
                || symbol is ObjectTypeAliasSymbol
                || string.IsNullOrWhiteSpace(symbol.FullyQualifiedName))
            {
                return null;
            }

            // Object/interface/enum declarations (and anonymous objects) carry the FQCN we need.
            if (symbol is ObjectDeclarationSymbol or AnonymousObjectDeclarationSymbol)
            {
                return symbol.FullyQualifiedName.TrimStart('\\');
            }

            return null;
        }

        private string BuildRuntimeGenericFromClassAndArgs(
            string className,
            IReadOnlyList<ITypeExpression> typeArgs,
            bool preferCtorLocals)
        {
            var classExpr = QuoteClassNameForType(className) + "::class";
            if (typeArgs.Count == 0)
            {
                return $"{RuntimeTypeClassFq}::fromClassName({classExpr})";
            }

            var namedArgs = new List<string>();
            for (var i = 0; i < typeArgs.Count; i++)
            {
                namedArgs.Add(this.BuildRuntimeNamedTypeArg(typeArgs[i], className, i, preferCtorLocals));
            }

            return $"{RuntimeTypeClassFq}::generic({classExpr}, {string.Join(", ", namedArgs)})";
        }

        private string BuildRuntimeNamedTypeArg(
            ITypeExpression typeArg,
            string parentClassName,
            int index,
            bool preferCtorLocals)
        {
            var paramHint = GuessGenericParamName(parentClassName, index);

            string? typeParamName = typeArg switch
            {
                PhpNamedTypeAst { Name: PhpNameAst n } => n.ValueString?.TrimStart('\\'),
                PhpNameAst n => n.ValueString?.TrimStart('\\'),
                _ => null,
            };

            if (!string.IsNullOrEmpty(typeParamName)
                && !typeParamName.Contains('\\')
                && this._currentObjectGenericParamNames.Contains(typeParamName))
            {
                if (preferCtorLocals
                    && this._ctorGenericLocalVars is not null
                    && this._ctorGenericLocalVars.TryGetValue(typeParamName, out var local))
                {
                    return $"new {RuntimeNamedTypeFq}('{paramHint}', ${local})";
                }

                var lookup = this.BuildGenericResolvedTypeLookupCall(typeParamName) is { } call
                    ? $"$this->{call}"
                    : $"{RuntimeTypeClassFq}::mixed()";
                return $"new {RuntimeNamedTypeFq}('{typeParamName}', {lookup})";
            }

            var underlying = this.BuildRuntimeTypeExpression(typeArg, preferCtorLocals);
            return $"new {RuntimeNamedTypeFq}('{paramHint}', {underlying})";
        }

        private static string GuessGenericParamName(string className, int index)
        {
            var shortName = className.TrimStart('\\').Split('\\')[^1];
            if (string.Equals(shortName, "Closure", StringComparison.OrdinalIgnoreCase))
            {
                return index == 0 ? "TReturn" : $"T{index}";
            }

            return index == 0 ? "TValue" : $"T{index}";
        }

        private static string ToGenericLocalVarName(string genericParamName)
        {
            if (string.IsNullOrEmpty(genericParamName))
            {
                return "t";
            }

            return char.ToLowerInvariant(genericParamName[0]) + genericParamName[1..];
        }

        private static string QuoteClassNameForType(string className)
        {
            var trimmed = className.TrimStart('\\');
            if (string.IsNullOrEmpty(trimmed)
                || string.Equals(trimmed, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "static", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "parent", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            // Always root-anchor class names in Type::generic / fromClassName so emission is
            // namespace-safe (e.g. `\Closure::class` inside `namespace Tyhp`).
            return "\\" + trimmed;
        }

        private string? TryBuildNewGenericTypeParameterExpression(PhpNewAst newExpr, string args)
        {
            if (!this._currentObjectRecordsOwnGenerics)
            {
                return null;
            }

            var simpleName = newExpr.ClassName switch
            {
                TyhpGenericIdentifierAst g => g.ValueString?.TrimStart('\\'),
                PhpNameAst n => n.ValueString?.TrimStart('\\'),
                _ => null,
            };

            if (string.IsNullOrEmpty(simpleName)
                || simpleName.Contains('\\')
                || !this._currentObjectGenericParamNames.Contains(simpleName))
            {
                return null;
            }

            if (this.BuildGenericTypeLookupCall(simpleName) is not { } lookup)
            {
                return null;
            }

            this._context.RequirePackage("tyhp/core");
            // NamedType has getUnderlyingType()->getName(), not getType() (Story 27).
            return $"new ($this->{lookup}->getUnderlyingType()->getName()){args}";
        }

        /// <summary>
        /// Rewrites <c>new Box&lt;int&gt;(…)</c> as a call to <c>Box</c>'s generated factory, which binds
        /// the type arguments onto an unconstructed instance and then runs the author's constructor.
        /// Returns null when the target is not a tracked generic class, leaving a plain <c>new</c>.
        /// <c>new static&lt;T&gt;</c> / <c>new self&lt;T&gt;</c> resolve to the class currently being
        /// emitted so Mechanism D factories such as <c>Promise::_async</c> bind class generics.
        /// </summary>
        private string? TryBuildGenericFactoryCall(PhpNewAst newExpr, string formattedArgs)
        {
            var classRef = newExpr.ClassName;
            ObjectDeclarationSymbol? targetSymbol = null;
            List<ITypeExpression>? explicitTypeArgs = null;

            if (classRef is TyhpGenericIdentifierAst genericId)
            {
                targetSymbol = this.ResolveNewExpressionTarget(
                    genericId.BoundSymbol,
                    genericId.ValueString);
                if (genericId.GenericArguments is PhpTypeExpressionListAst list)
                {
                    explicitTypeArgs = FlattenTypeArgumentList(list);
                }
                else if (GetGenericTypeArgumentAddon(genericId) is { } genericAddonArgs)
                {
                    // Same shape as PhpNameAst: type args may live only on grammar addons.
                    explicitTypeArgs = genericAddonArgs.ToList();
                }
            }
            else if (classRef is PhpNameAst name)
            {
                targetSymbol = this.ResolveNewExpressionTarget(name.BoundSymbol, name.ValueString);
                // `new Box<string>()` / `new static<T>()` stores type args on the class-name
                // "identifier" grammar addon (see VisitClassName / VisitClassNameIdentifierGrammarAddon).
                if (GetGenericTypeArgumentAddon(name) is { } addonArgs)
                {
                    explicitTypeArgs = addonArgs.ToList();
                }
            }

            // A class only has a factory when it records generic parameters of its own; anything else
            // stays a plain `new`, whose constructor gate reaches the same init chain.
            if (targetSymbol is null
                || targetSymbol.GenericParameters.Count == 0
                || !this.SymbolIsInGenericChain(targetSymbol))
            {
                return null;
            }

            this._context.RequirePackage("tyhp/core");

            var typeArgExpressions = new List<string>();
            for (var i = 0; i < targetSymbol.GenericParameters.Count; i++)
            {
                var typeArg = explicitTypeArgs is not null && i < explicitTypeArgs.Count
                    ? explicitTypeArgs[i]
                    : null;

                // Not spelled at the call site: let the factory's own hook resolve it against the
                // declared default or the broadest type the constraint allows.
                typeArgExpressions.Add(typeArg is null
                    ? "null"
                    : this.BuildRuntimeTypeExpression(typeArg, preferCtorLocals: false));
            }

            var allArgs = string.IsNullOrWhiteSpace(formattedArgs)
                ? string.Join(", ", typeArgExpressions)
                : string.Join(", ", typeArgExpressions.Append(formattedArgs));

            var fqn = targetSymbol.FullyQualifiedName;
            var factory = GeneratedNames.GenericFactory(fqn);
            return $"\\{fqn.TrimStart('\\')}::{factory}({allArgs})";
        }

        /// <summary>
        /// Resolves the class a <c>new</c> expression constructs for Mechanism C factory routing.
        /// Relative names (<c>static</c>/<c>self</c>/<c>parent</c>) map to the object currently being
        /// emitted (or its parent); everything else uses the bound symbol or a global-scope lookup.
        /// </summary>
        private ObjectDeclarationSymbol? ResolveNewExpressionTarget(
            IBaseSymbol? bound,
            string? writtenName)
        {
            var simple = writtenName?.TrimStart('\\');
            if (!string.IsNullOrEmpty(simple)
                && !simple.Contains('\\')
                && (string.Equals(simple, "static", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(simple, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(simple, "parent", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.Equals(simple, "parent", StringComparison.OrdinalIgnoreCase))
                {
                    return this.TryResolveEmitParent(this._currentObjectSymbol)
                        ?? this._currentObjectSymbol;
                }

                return this._currentObjectSymbol;
            }

            return bound as ObjectDeclarationSymbol
                ?? this.TryResolveObjectByName(writtenName);
        }

        private ObjectDeclarationSymbol? TryResolveObjectByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var simple = name.TrimStart('\\');
            return FindObjectDeclarationNamed(this._context.GlobalScope, simple);
        }

        private static ObjectDeclarationSymbol? FindObjectDeclarationNamed(
            Binder.Scopes.Interfaces.IBaseScope scope,
            string name,
            int depth = 0)
        {
            if (depth > 500)
            {
                return null;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol obj)
                {
                    if (string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return obj;
                    }

                    var fqn = obj.FullyQualifiedName ?? obj.Name;
                    if (string.Equals(fqn, name, StringComparison.OrdinalIgnoreCase)
                        || fqn.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase))
                    {
                        return obj;
                    }
                }
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (childScope is null)
                {
                    continue;
                }

                var found = FindObjectDeclarationNamed(childScope, name, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
