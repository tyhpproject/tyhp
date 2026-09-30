using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
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
                    var text = NameText(traitName);
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
                    if (this.IsEraseGenericOptedOut(propertyDecl)
                        || !this.TypeAstInvolvesGenerics(propType))
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

                        if (this.IsEraseGenericOptedOut(param)
                            || !this.TypeAstInvolvesGenerics(param.Type))
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

        private bool IsEraseGenericOptedOut(IBase2Ast host)
            => EraseGenericAttributeSupport.HasEraseGeneric(host)
                || EraseGenericAttributeSupport.HasEraseGeneric(this._currentObjectDecl);

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

            // typeExpr `Optional<int>` stores args on TyhpGenericIdentifierAst.GenericArguments.
            if (node is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst genericList })
            {
                var fromGeneric = FlattenTypeArgumentList(genericList);
                if (fromGeneric.Count > 0)
                {
                    return fromGeneric;
                }
            }

            if (node is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst namedGeneric }
                && namedGeneric.GenericArguments is PhpTypeExpressionListAst namedList)
            {
                var fromNamed = FlattenTypeArgumentList(namedList);
                if (fromNamed.Count > 0)
                {
                    return fromNamed;
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
            this.ReportInternalErrorIfExternType(typeExpr);
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

        /// <summary>
        /// Spells an inferred <see cref="ICheckedType"/> as a <c>\Tyhp\Type::…</c> value so a
        /// bare <c>new Generic()</c> whose type arguments came from context still stamps the
        /// Mechanism C factory.
        /// </summary>
        private string BuildRuntimeTypeExpressionFromChecked(ICheckedType type, bool preferCtorLocals)
        {
            this.ReportInternalErrorIfExternCheckedType(type);
            return this.BuildRuntimeTypeExpressionFromCheckedCore(type, preferCtorLocals);
        }

        private string BuildRuntimeTypeExpressionFromCheckedCore(ICheckedType type, bool preferCtorLocals)
        {
            switch (type)
            {
                case NullableCheckedType nullable:
                {
                    var inner = this.BuildRuntimeTypeExpressionFromCheckedCore(
                        nullable.InnerType, preferCtorLocals);
                    if (inner.StartsWith($"{RuntimeTypeClassFq}::nullable(", StringComparison.Ordinal)
                        || inner.StartsWith($"{RuntimeTypeClassFq}::null(", StringComparison.Ordinal)
                        || inner.StartsWith($"{RuntimeTypeClassFq}::mixed(", StringComparison.Ordinal))
                    {
                        return inner;
                    }

                    return $"{RuntimeTypeClassFq}::nullable({inner})";
                }

                case LiteralCheckedType { Value: null }:
                    return $"{RuntimeTypeClassFq}::null()";
                case LiteralCheckedType { Value: true }:
                    return $"{RuntimeTypeClassFq}::true()";
                case LiteralCheckedType { Value: false }:
                    return $"{RuntimeTypeClassFq}::false()";

                case SpecialCheckedType special when special.IsMixed:
                    return $"{RuntimeTypeClassFq}::mixed()";
                case SpecialCheckedType special when special.IsVoid:
                    return $"{RuntimeTypeClassFq}::void()";
                case SpecialCheckedType special when special.IsNever:
                    return $"{RuntimeTypeClassFq}::never()";

                case UnionCheckedType union:
                {
                    var parts = union.Members
                        .Select(m => this.BuildRuntimeTypeExpressionFromCheckedCore(m, preferCtorLocals))
                        .ToList();
                    if (parts.Count == 0)
                    {
                        return $"{RuntimeTypeClassFq}::mixed()";
                    }

                    if (parts.Count == 1)
                    {
                        return parts[0];
                    }

                    return $"{RuntimeTypeClassFq}::union({string.Join(", ", parts)})";
                }

                case IntersectionCheckedType intersection:
                {
                    var parts = intersection.Members
                        .Select(m => this.BuildRuntimeTypeExpressionFromCheckedCore(m, preferCtorLocals))
                        .ToList();
                    if (parts.Count == 0)
                    {
                        return $"{RuntimeTypeClassFq}::mixed()";
                    }

                    if (parts.Count == 1)
                    {
                        return parts[0];
                    }

                    return $"{RuntimeTypeClassFq}::intersection({string.Join(", ", parts)})";
                }

                case GenericCheckedType generic:
                    return this.BuildRuntimeGenericFromChecked(generic, preferCtorLocals);

                case StructCheckedType:
                    return $"{RuntimeTypeClassFq}::array()";

                case SimpleCheckedType simple:
                    return this.BuildRuntimeSimpleFromChecked(simple, preferCtorLocals);

                default:
                    if (type.IsMixed)
                    {
                        return $"{RuntimeTypeClassFq}::mixed()";
                    }

                    return $"{RuntimeTypeClassFq}::mixed()";
            }
        }

        private string BuildRuntimeGenericFromChecked(GenericCheckedType generic, bool preferCtorLocals)
        {
            var origin = TypeComparer.TryGetNominalSymbol(generic.BaseType);
            var className = origin switch
            {
                ObjectDeclarationSymbol obj => obj.FullyQualifiedName,
                BuiltInTypeSymbol builtIn => builtIn.Name,
                _ => origin?.FullyQualifiedName ?? origin?.Name,
            };

            if (string.IsNullOrEmpty(className))
            {
                return $"{RuntimeTypeClassFq}::mixed()";
            }

            var args = generic.TypeArguments
                .Select(arg => this.BuildRuntimeTypeExpressionFromCheckedCore(arg, preferCtorLocals))
                .ToList();
            if (args.Count == 0)
            {
                return BuildRuntimeSimpleNameFromChecked(className);
            }

            var isArrayFamily = string.Equals(className, "array", StringComparison.OrdinalIgnoreCase)
                || string.Equals(className, "iterable", StringComparison.OrdinalIgnoreCase);
            if (isArrayFamily)
            {
                var typeName = className.Equals("iterable", StringComparison.OrdinalIgnoreCase)
                    ? "iterable"
                    : "array";
                return $"{RuntimeTypeClassFq}::generic('{typeName}', {string.Join(", ", args)})";
            }

            var classExpr = QuoteClassNameForType(className) + "::class";
            return $"{RuntimeTypeClassFq}::generic({classExpr}, {string.Join(", ", args)})";
        }

        private string BuildRuntimeSimpleFromChecked(SimpleCheckedType simple, bool preferCtorLocals)
        {
            if (simple.ResolvedSymbol is GenericTypeParameterSymbol param)
            {
                return this.BuildRuntimeGenericParameterType(param.Name, preferCtorLocals);
            }

            if (simple.ResolvedSymbol is BuiltInTypeSymbol builtIn)
            {
                return BuildRuntimeSimpleNameFromChecked(builtIn.Name);
            }

            if (simple.ResolvedSymbol is ObjectDeclarationSymbol { IsStruct: true })
            {
                return $"{RuntimeTypeClassFq}::array()";
            }

            if (simple.ResolvedSymbol is ObjectDeclarationSymbol obj)
            {
                return $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(obj.FullyQualifiedName)}::class)";
            }

            var name = simple.ResolvedSymbol.FullyQualifiedName ?? simple.ResolvedSymbol.Name;
            return BuildRuntimeSimpleNameFromChecked(name);
        }

        private static string BuildRuntimeSimpleNameFromChecked(string name)
        {
            var simple = name.TrimStart('\\');
            if (ScalarTypeFactoryNames.Contains(simple))
            {
                return $"{RuntimeTypeClassFq}::{simple}()";
            }

            return $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(simple)}::class)";
        }

        private string BuildRuntimeTypeExpressionCore(ITypeExpression typeExpr, bool preferCtorLocals)
        {
            if (this.TryBuildAliasFactoryRuntimeType(typeExpr, preferCtorLocals) is { } aliasFactory)
            {
                return aliasFactory;
            }

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
                var builtinArgs = GetGenericTypeArgumentAddon(builtin);
                if (builtinArgs is { Count: > 0 })
                {
                    var builtinName = builtin.Identifier ?? "mixed";
                    return this.BuildRuntimeGenericFromClassAndArgs(
                        builtinName, builtinArgs, preferCtorLocals);
                }

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

            if (typeExpr is TyhpObjectShapeAst shape)
            {
                return this.BuildRuntimeObjectShapeType(shape);
            }

            if (typeExpr is TyhpCallableShapeAst)
            {
                return this.BuildRuntimeCallableShapeType();
            }

            if (typeExpr is TyhpStructShapeAst structShape)
            {
                if (structShape.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } namedStruct)
                {
                    return this.BuildRuntimeStructType(namedStruct, typeArgs: null, preferCtorLocals);
                }

                if (typeExpr.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } aliasStruct)
                {
                    return this.BuildRuntimeStructType(aliasStruct, typeArgs: null, preferCtorLocals);
                }

                return $"{RuntimeTypeClassFq}::array()";
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

                if (this._emittingAliasFactorySymbol is not null
                    && composite.TypeKind != PhpTypeKind.Intersection
                    && members.GetAllNotNull().ToList() is { Count: 2 } unionMembers
                    && parts.Count == 2)
                {
                    if (IsNullTypeExpression(unionMembers[0]))
                    {
                        return $"{RuntimeTypeClassFq}::nullable({parts[1]})";
                    }

                    if (IsNullTypeExpression(unionMembers[1]))
                    {
                        return $"{RuntimeTypeClassFq}::nullable({parts[0]})";
                    }
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

            if (this.TryResolveStructDeclaration(name.BoundSymbol, simple) is { } structDecl)
            {
                return this.BuildRuntimeStructType(structDecl, typeArgs: null, preferCtorLocals);
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
                    || this._currentCallableGenericParamNames.Contains(simple)
                    || this._aliasFactoryGenericParamExprs.ContainsKey(simple));
        }

        /// <summary>
        /// Runtime <c>\Tyhp\Type</c> for a generic type parameter name: Mechanism D capture, class
        /// GenericObject lookup, or erased <c>mixed</c> when the binding is unavailable here.
        /// </summary>
        private string BuildRuntimeGenericParameterType(string paramName, bool preferCtorLocals)
        {
            if (this._aliasFactoryGenericParamExprs.TryGetValue(paramName, out var factoryExpr))
            {
                return factoryExpr;
            }

            if (this.IsVariantGenericParamName(paramName))
            {
                return this.BuildVariantTypeofLookup(paramName);
            }

            if (this._currentObjectGenericParamNames.Contains(paramName))
            {
                if (this._emittingAliasFactorySymbol is not null && this._currentMemberIsStatic)
                {
                    return $"{RuntimeTypeClassFq}::mixed()";
                }

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
            if (this.TryResolveStructDeclaration(bound: null, className) is { } structDecl)
            {
                return this.BuildRuntimeStructType(structDecl, typeArgs, preferCtorLocals);
            }

            var isArrayFamily = string.Equals(className, "array", StringComparison.OrdinalIgnoreCase)
                || string.Equals(className, "iterable", StringComparison.OrdinalIgnoreCase)
                || className.Equals("\\array", StringComparison.OrdinalIgnoreCase)
                || className.Equals("\\iterable", StringComparison.OrdinalIgnoreCase);
            var typeName = isArrayFamily
                ? (className.TrimStart('\\').Equals("iterable", StringComparison.OrdinalIgnoreCase)
                    ? "iterable"
                    : "array")
                : null;

            if (typeArgs.Count == 0)
            {
                return isArrayFamily
                    ? $"{RuntimeTypeClassFq}::{typeName}()"
                    : $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(className)}::class)";
            }

            var namedArgs = new List<string>();
            for (var i = 0; i < typeArgs.Count; i++)
            {
                namedArgs.Add(this.BuildRuntimeNamedTypeArg(
                    typeArgs[i],
                    isArrayFamily ? typeName! : className,
                    i,
                    typeArgs.Count,
                    preferCtorLocals));
            }

            if (isArrayFamily)
            {
                return $"{RuntimeTypeClassFq}::generic('{typeName}', {string.Join(", ", namedArgs)})";
            }

            var classExpr = QuoteClassNameForType(className) + "::class";
            return $"{RuntimeTypeClassFq}::generic({classExpr}, {string.Join(", ", namedArgs)})";
        }

        private string BuildRuntimeNamedTypeArg(
            ITypeExpression typeArg,
            string parentClassName,
            int index,
            int arity,
            bool preferCtorLocals)
        {
            var paramHint = GuessGenericParamName(parentClassName, index, arity);

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

        private static string GuessGenericParamName(string className, int index, int arity)
        {
            var shortName = className.TrimStart('\\').Split('\\')[^1];
            if (string.Equals(shortName, "array", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortName, "iterable", StringComparison.OrdinalIgnoreCase))
            {
                // PHP `array<T>` / `iterable<T>` is a value-type shorthand; the sole argument is TValue.
                // Two-argument form is array<TKey, TValue> / iterable<TKey, TValue>.
                if (arity == 1)
                {
                    return "TValue";
                }

                return index == 0 ? "TKey" : "TValue";
            }

            if (string.Equals(shortName, "Closure", StringComparison.OrdinalIgnoreCase))
            {
                return index switch
                {
                    0 => "TCallableShape",
                    1 => "TThis",
                    2 => "TScope",
                    _ => $"T{index}",
                };
            }

            return index == 0 ? "TValue" : $"T{index}";
        }

        private ObjectDeclarationSymbol? TryResolveStructDeclaration(IBaseSymbol? bound, string? writtenName)
        {
            if (bound is ObjectDeclarationSymbol { IsStruct: true } boundStruct)
            {
                return boundStruct;
            }

            return this.TryResolveObjectByName(writtenName) is { IsStruct: true } resolved
                ? resolved
                : null;
        }

        /// <summary>
        /// Materializes a struct declaration as <c>\Tyhp\Type::struct(name, fields, requiredKeys)</c>
        /// so <c>Type::is</c> / <c>Json::decode&lt;T&gt;</c> can check associative-array shapes.
        /// Recursive fields of the same struct emit <c>Type::array()</c> to break the cycle.
        /// </summary>
        private string BuildRuntimeStructType(
            ObjectDeclarationSymbol structDecl,
            IReadOnlyList<ITypeExpression>? typeArgs,
            bool preferCtorLocals)
        {
            var fqn = (structDecl.FullyQualifiedName ?? structDecl.Name).TrimStart('\\');
            if (string.IsNullOrEmpty(fqn))
            {
                fqn = structDecl.Name;
            }

            if (!this._structTypeEmitStack.Add(fqn))
            {
                return $"{RuntimeTypeClassFq}::array()";
            }

            try
            {
                Dictionary<string, string>? substitutions = null;
                if (structDecl.GenericParameters.Count > 0)
                {
                    substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (var i = 0; i < structDecl.GenericParameters.Count; i++)
                    {
                        var gp = structDecl.GenericParameters[i];
                        string expr;
                        if (typeArgs is not null && i < typeArgs.Count)
                        {
                            expr = this.BuildRuntimeTypeExpression(typeArgs[i], preferCtorLocals);
                        }
                        else if (gp.DefaultType is not null)
                        {
                            expr = this.BuildRuntimeTypeExpression(gp.DefaultType, preferCtorLocals);
                        }
                        else
                        {
                            expr = $"{RuntimeTypeClassFq}::mixed()";
                        }

                        substitutions[gp.Name] = expr;
                    }
                }

                // Base-first so a derived property of the same PHP key replaces the inherited one.
                var fields = new Dictionary<string, (StructArrayKey Key, string TypeExpr, bool Required)>(
                    StringComparer.Ordinal);
                foreach (var level in StructEmissionHelper.EnumerateStructHierarchy(structDecl))
                {
                    foreach (var member in level.Members.Values)
                    {
                        if (member is not ObjectPropertySymbol property || property.DeclaredType is null)
                        {
                            continue;
                        }

                        var key = StructEmissionHelper.GetStructArrayKey(property);
                        var typeExpr = this.BuildRuntimeTypeExpressionSubstituting(
                            property.DeclaredType,
                            substitutions,
                            preferCtorLocals);
                        var required = !TypeExpressionAllowsNull(property.DeclaredType)
                            && property.DefaultValue is null;
                        fields[(key.IsInteger ? "#" : "") + key.Text] = (key, typeExpr, required);
                    }
                }

                var fieldParts = new List<string>(fields.Count);
                var requiredParts = new List<string>();
                foreach (var entry in fields.Values)
                {
                    fieldParts.Add($"{FormatPhpArrayKey(entry.Key)} => {entry.TypeExpr}");
                    if (entry.Required)
                    {
                        requiredParts.Add(FormatPhpArrayKey(entry.Key));
                    }
                }

                var fieldsLiteral = fieldParts.Count == 0
                    ? "[]"
                    : "[" + string.Join(", ", fieldParts) + "]";
                var requiredLiteral = requiredParts.Count == 0
                    ? "[]"
                    : "[" + string.Join(", ", requiredParts) + "]";
                var nameLiteral = FormatPhpArrayKey(new StructArrayKey(fqn, IsInteger: false));
                return $"{RuntimeTypeClassFq}::struct({nameLiteral}, {fieldsLiteral}, {requiredLiteral})";
            }
            finally
            {
                this._structTypeEmitStack.Remove(fqn);
            }
        }

        /// <summary>
        /// Materializes an object-shape alias body as
        /// <c>\Tyhp\Type::objectShape(name, methods, properties)</c> for v1 existence matching.
        /// <c>__construct</c> is omitted (constructability only). Member types are not reified.
        /// </summary>
        private string BuildRuntimeObjectShapeType(TyhpObjectShapeAst shape)
        {
            var aliasName = this._objectShapeDescriptorName
                ?? this._emittingAliasFactorySymbol?.Name
                ?? "object";
            var methods = new List<string>();
            var properties = new List<string>();
            var seenMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in shape.Members?.GetAllNotNull() ?? [])
            {
                switch (member)
                {
                    case PhpMethodDeclAst method:
                    {
                        var name = method.Identifier ?? string.Empty;
                        if (string.IsNullOrEmpty(name)
                            || string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase)
                            || HasModifier(method.Modifiers, PhpModifier.Static)
                            || !seenMethods.Add(name))
                        {
                            break;
                        }

                        methods.Add(name);
                        break;
                    }
                    case PhpPropertyDeclAst property:
                    {
                        if (HasModifier(property.Modifiers, PhpModifier.Static))
                        {
                            break;
                        }

                        foreach (var item in property.Properties?.GetAllNotNull() ?? [])
                        {
                            var name = item.Identifier ?? string.Empty;
                            if (name.StartsWith('$'))
                            {
                                name = name[1..];
                            }

                            if (string.IsNullOrEmpty(name) || !seenProperties.Add(name))
                            {
                                continue;
                            }

                            properties.Add(name);
                        }

                        break;
                    }
                }
            }

            var nameLiteral = FormatPhpArrayKey(new StructArrayKey(aliasName, IsInteger: false));
            var methodsLiteral = FormatPhpStringList(methods);
            var propertiesLiteral = FormatPhpStringList(properties);
            return $"{RuntimeTypeClassFq}::objectShape({nameLiteral}, {methodsLiteral}, {propertiesLiteral})";
        }

        /// <summary>
        /// Materializes a callable-shape alias body as
        /// <c>\Tyhp\Type::callableShape(name)</c> for v1 existence matching
        /// (<c>\is_callable</c>). Parameter and return types are not reified.
        /// </summary>
        private string BuildRuntimeCallableShapeType()
        {
            var aliasName = this._objectShapeDescriptorName
                ?? this._emittingAliasFactorySymbol?.Name
                ?? "callable";
            var nameLiteral = FormatPhpArrayKey(new StructArrayKey(aliasName, IsInteger: false));
            return $"{RuntimeTypeClassFq}::callableShape({nameLiteral})";
        }

        private static string FormatPhpStringList(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                return "[]";
            }

            return "["
                + string.Join(
                    ", ",
                    values.Select(value => FormatPhpArrayKey(new StructArrayKey(value, IsInteger: false))))
                + "]";
        }

        private static bool HasModifier(PhpModifierListAst? modifiers, PhpModifier wanted)
        {
            if (modifiers is null)
            {
                return false;
            }

            foreach (var modifier in modifiers.Modifiers)
            {
                if (modifier == wanted)
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildRuntimeTypeExpressionSubstituting(
            ITypeExpression typeExpr,
            Dictionary<string, string>? substitutions,
            bool preferCtorLocals)
        {
            if (substitutions is { Count: > 0 }
                && TryGetBareTypeName(typeExpr) is { } bare
                && substitutions.TryGetValue(bare, out var substituted))
            {
                return TypeExpressionAllowsNull(typeExpr)
                    ? $"{RuntimeTypeClassFq}::nullable({substituted})"
                    : substituted;
            }

            return this.BuildRuntimeTypeExpression(typeExpr, preferCtorLocals);
        }

        private static string? TryGetBareTypeName(ITypeExpression typeExpr)
        {
            if (typeExpr is PhpTypeExpressionAst composite && composite.Types is { } members)
            {
                var list = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                if (list.Count == 1)
                {
                    return TryGetBareTypeName(list[0]);
                }

                return null;
            }

            return typeExpr switch
            {
                PhpNamedTypeAst { Name: PhpNameAst n } => n.ValueString?.TrimStart('\\'),
                PhpNameAst name => name.ValueString?.TrimStart('\\'),
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                _ => null,
            };
        }

        private static bool TypeExpressionAllowsNull(ITypeExpression? typeExpr) =>
            typeExpr is PhpTypeExpressionAst { IsNullable: true };

        private static string FormatPhpArrayKey(StructArrayKey key)
        {
            if (key.IsInteger)
            {
                return key.Text;
            }

            return "'"
                + key.Text.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("'", "\\'", StringComparison.Ordinal)
                + "'";
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
            var simpleName = newExpr.ClassName switch
            {
                TyhpGenericIdentifierAst g => g.ValueString?.TrimStart('\\'),
                PhpNameAst n => n.ValueString?.TrimStart('\\'),
                _ => null,
            };

            if (string.IsNullOrEmpty(simpleName) || simpleName.Contains('\\'))
            {
                return null;
            }

            if (this.IsVariantGenericParamName(simpleName))
            {
                this._context.RequirePackage("tyhp/core");
                // Mechanism D binder captures `\Tyhp\Type`; Type::getName() is the class FQCN.
                return $"new ({GenericVariantParamName(simpleName)}->getName()){args}";
            }

            if (!this._currentObjectRecordsOwnGenerics
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
            if (targetSymbol is null || targetSymbol.GenericParameters.Count == 0)
            {
                return null;
            }

            if (!this.GenericRuntimeLayoutIsSupported(targetSymbol, newExpr))
            {
                return null;
            }

            var typeArgExpressions = new List<string>();
            IReadOnlyList<ICheckedType>? inferredArgs = null;
            if ((explicitTypeArgs is null || explicitTypeArgs.Count < targetSymbol.GenericParameters.Count)
                && this._context.ExpressionTypes.TryGetValue(newExpr, out var inferredNewType)
                && inferredNewType is GenericCheckedType inferredGeneric
                && inferredGeneric.TypeArguments.Count == targetSymbol.GenericParameters.Count
                && TypeComparer.SymbolsMatch(
                    TypeComparer.TryGetNominalSymbol(inferredGeneric.BaseType), targetSymbol))
            {
                inferredArgs = inferredGeneric.TypeArguments;
            }

            for (var i = 0; i < targetSymbol.GenericParameters.Count; i++)
            {
                if (explicitTypeArgs is not null && i < explicitTypeArgs.Count)
                {
                    typeArgExpressions.Add(
                        this.BuildRuntimeTypeExpression(explicitTypeArgs[i], preferCtorLocals: false));
                    continue;
                }

                if (inferredArgs is not null && i < inferredArgs.Count)
                {
                    typeArgExpressions.Add(
                        this.BuildRuntimeTypeExpressionFromChecked(inferredArgs[i], preferCtorLocals: false));
                    continue;
                }

                // Not spelled at the call site and not inferred from context: let the factory's
                // own hook resolve it against the declared default or the broadest type the
                // constraint allows.
                typeArgExpressions.Add("null");
            }

            var ctorArgs = this.FormatArgumentItems(newExpr.Arguments);
            var ctorArgsList = JoinPhpCommaList(ctorArgs);

            if (this._context.HasForeignGenericRuntime(targetSymbol))
            {
                this._context.RequirePackage("tyhp/core");
                var classExpr = "\\" + targetSymbol.FullyQualifiedName.TrimStart('\\') + "::class";
                var bindArgs = typeArgExpressions.Count == 0
                    ? classExpr
                    : classExpr + ", " + string.Join(", ", typeArgExpressions);
                return $"\\Tyhp\\Generic::bind({bindArgs})({ctorArgsList})";
            }

            if (!this.SymbolIsInGenericChain(targetSymbol))
            {
                return null;
            }

            this._context.RequirePackage("tyhp/core");

            var allArgs = typeArgExpressions.Concat(ctorArgs).ToList();
            var fqn = targetSymbol.FullyQualifiedName;
            var factory = GeneratedNames.GenericFactory(fqn);
            return $"\\{fqn.TrimStart('\\')}::{factory}({JoinPhpCommaList(allArgs)})";
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
