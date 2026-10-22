using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Source type-alias declarations emit <c>\Tyhp\Type::register</c>. A value-position call
    /// (<c>UserId()</c>, <c>\Ns\UserId()</c>, <c>UserService::NameType()</c>) emits
    /// <c>\Tyhp\Type::ref</c>. PHP type hints still expand via <see cref="TypeSpellingHelper"/>.
    /// </summary>
    public partial class TyhpEmitter
    {
        private readonly record struct AliasFactoryGenericParam(string Name, ITypeExpression? DefaultType);

        private EmitItem EmitTypeAliasFactory(TyhpTypeAliasAst alias, EmitItem parent, bool classLevel)
        {
            var emptyType = classLevel ? EmitType.ObjectStaticMethods : EmitType.RootStatement;
            if (!ShouldEmitSourceTypeAliasFactory(alias)
                || !ShouldEmitPhpVersionGatedDeclaration(alias)
                || NoEmitAttributeSupport.ShouldOmitDeclaration(alias))
            {
                return EmitItem.Empty(alias, emptyType, parent);
            }

            var name = alias.Name?.ValueString ?? alias.Identifier ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                return EmitItem.Empty(alias, emptyType, parent);
            }

            this._context.RequirePackage("tyhp/core");

            var genericParams = CollectAliasFactoryGenericParams(alias);
            var previousExprs = new Dictionary<string, string>(
                this._aliasFactoryGenericParamExprs,
                StringComparer.Ordinal);
            var previousSymbol = this._emittingAliasFactorySymbol;
            var previousIsStatic = this._currentMemberIsStatic;
            var previousStructFqn = this._materializingStructFqn;

            this._aliasFactoryGenericParamExprs.Clear();
            foreach (var gp in genericParams)
            {
                this._aliasFactoryGenericParamExprs[gp.Name] = "$" + gp.Name;
            }

            this._emittingAliasFactorySymbol = alias.BoundSymbol;
            this._materializingStructFqn = StructRegistrationFqn(alias);
            if (classLevel)
            {
                this._currentMemberIsStatic = true;
            }

            try
            {
                var bodyExpr = this.BuildRuntimeTypeExpression(alias.TypeExpression, preferCtorLocals: false);
                var registeredName = classLevel && this._currentObjectFqn is { } objectFqn
                    ? objectFqn.TrimStart('\\') + "\\" + name
                    : DeclarationRegistrationName(alias, name);
                var statement = this.BuildTypeRegisterStatement(
                    registeredName,
                    genericParams,
                    bodyExpr);
                if (classLevel)
                {
                    this._pendingClassTypeRegistrations.Add((alias, statement));
                    return EmitItem.Empty(alias, emptyType, parent);
                }

                return EmitItem.Line(alias, emptyType, statement, parent);
            }
            finally
            {
                this._aliasFactoryGenericParamExprs.Clear();
                foreach (var pair in previousExprs)
                {
                    this._aliasFactoryGenericParamExprs[pair.Key] = pair.Value;
                }

                this._emittingAliasFactorySymbol = previousSymbol;
                this._currentMemberIsStatic = previousIsStatic;
                this._materializingStructFqn = previousStructFqn;
            }
        }

        private static bool ShouldEmitSourceTypeAliasFactory(TyhpTypeAliasAst alias)
            => !IsTyhpdefTypeAlias(alias);

        private static bool IsTyhpdefTypeAlias(TyhpTypeAliasAst alias)
        {
            if (string.Equals(alias.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsTyhpdefAliasSymbol(alias.BoundSymbol);
        }

        private static bool IsTyhpdefAliasSymbol(IBaseSymbol? symbol)
        {
            if (symbol is null)
            {
                return false;
            }

            var file = symbol.SourceFile ?? "";
            if (file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return symbol is BaseSymbol { DeclaringAstNode.LanguageMode: "tyhpdef" };
        }

        private static bool TypeAliasIsCircular(IBaseSymbol? alias)
        {
            if (alias is not TypeAliasSymbol and not ObjectTypeAliasSymbol)
            {
                return false;
            }

            var visiting = new HashSet<IBaseSymbol>();
            var visited = new HashSet<IBaseSymbol>();
            return TypeAliasGraphHasCycle(alias, visiting, visited);
        }

        private static bool TypeAliasGraphHasCycle(
            IBaseSymbol start,
            HashSet<IBaseSymbol> visiting,
            HashSet<IBaseSymbol> visited)
        {
            if (visited.Contains(start))
            {
                return false;
            }

            if (!visiting.Add(start))
            {
                return true;
            }

            foreach (var next in EnumerateReferencedTypeAliases(GetAliasedType(start) as IBase2Ast, depth: 0))
            {
                if (TypeAliasGraphHasCycle(next, visiting, visited))
                {
                    visiting.Remove(start);
                    return true;
                }
            }

            visiting.Remove(start);
            visited.Add(start);
            return false;
        }

        private static ITypeExpression? GetAliasedType(IBaseSymbol alias) =>
            alias switch
            {
                TypeAliasSymbol fileAlias => fileAlias.AliasedType,
                ObjectTypeAliasSymbol objectAlias => objectAlias.AliasedType,
                _ => null,
            };

        private static IEnumerable<IBaseSymbol> EnumerateReferencedTypeAliases(IBase2Ast? node, int depth)
        {
            if (node is null || depth > 500)
            {
                yield break;
            }

            if (node.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                yield return node.BoundSymbol;
            }

            foreach (var child in node.AstChildren)
            {
                foreach (var nested in EnumerateReferencedTypeAliases(child, depth + 1))
                {
                    yield return nested;
                }
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                foreach (var nested in EnumerateReferencedTypeAliases(addon, depth + 1))
                {
                    yield return nested;
                }
            }
        }

        private static List<AliasFactoryGenericParam> CollectAliasFactoryGenericParams(TyhpTypeAliasAst alias)
        {
            var fromSymbol = alias.BoundSymbol switch
            {
                TypeAliasSymbol fileAlias => fileAlias.GenericParameters,
                ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                _ => null,
            };

            if (fromSymbol is { Count: > 0 })
            {
                return fromSymbol
                    .Select(gp => new AliasFactoryGenericParam(gp.Name, gp.DefaultType))
                    .ToList();
            }

            var fromAst = new List<AliasFactoryGenericParam>();
            foreach (var arg in alias.GenericArguments?.GetAllNotNull() ?? [])
            {
                var name = arg.Name?.ValueString ?? arg.Identifier ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fromAst.Add(new AliasFactoryGenericParam(name, arg.DefaultType));
                }
            }

            return fromAst;
        }

        private string BuildAliasFactorySignature(
            TyhpTypeAliasAst alias,
            string name,
            IReadOnlyList<AliasFactoryGenericParam> genericParams,
            bool classLevel)
        {
            var parameters = string.Join(
                ", ",
                genericParams.Select(gp => $"?{RuntimeTypeClassFq} ${gp.Name} = null"));
            var function = "function " + name + "(" + parameters + "): " + RuntimeTypeClassFq;
            if (!classLevel)
            {
                return function;
            }

            var modifiers = this.EnsureMethodVisibility(this.FormatModifiers(alias.Modifiers));
            if (modifiers.Contains("static", StringComparison.Ordinal))
            {
                return modifiers + function;
            }

            return modifiers + "static " + function;
        }

        private string? TryBuildAliasFactoryRuntimeType(ITypeExpression typeExpr, bool preferCtorLocals)
        {
            if (this._emittingAliasFactorySymbol is not null)
            {
                if (TryGetSelfStaticParentSpelling(typeExpr) is { } selfName)
                {
                    return this.BuildSelfStaticParentRuntimeClass(selfName);
                }

                if (typeExpr is TyhpTemplateStringTypeAst)
                {
                    return $"{RuntimeTypeClassFq}::string()";
                }
            }

            var aliasSymbol = this.TryGetRuntimeTypeAliasSymbol(typeExpr);
            if (aliasSymbol is null
                || ReferenceEquals(aliasSymbol, this._emittingAliasFactorySymbol))
            {
                return null;
            }

            this._context.RequirePackage("tyhp/core");
            var typeArgs = GetTypeArgumentsForAliasUse(typeExpr);
            if (!this.GenericRuntimeLayoutIsSupported(aliasSymbol, typeExpr))
            {
                return null;
            }

            if (IsTyhpdefAliasSymbol(aliasSymbol)
                && GenericRuntimeAttributeSupport.TryRead(aliasSymbol)?.HasAliasFactory != true)
            {
                return this.BuildInlinedTyhpdefAliasRuntimeType(aliasSymbol, typeArgs, preferCtorLocals);
            }

            return this.BuildSourceAliasFactoryCall(aliasSymbol, typeArgs, preferCtorLocals, typeExpr);
        }

        private string BuildInlinedTyhpdefAliasRuntimeType(
            IBaseSymbol alias,
            IReadOnlyList<ITypeExpression>? typeArgs,
            bool preferCtorLocals)
        {
            var body = GetAliasedType(alias);
            if (body is null)
            {
                return $"{RuntimeTypeClassFq}::mixed()";
            }

            var previousShapeName = this._objectShapeDescriptorName;
            this._objectShapeDescriptorName = alias.Name;
            try
            {
                var generics = alias switch
                {
                    TypeAliasSymbol fileAlias => fileAlias.GenericParameters,
                    ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                    _ => (IReadOnlyList<GenericTypeParameterSymbol>)Array.Empty<GenericTypeParameterSymbol>(),
                };

                if (generics.Count == 0)
                {
                    return this.BuildRuntimeTypeExpression(body, preferCtorLocals);
                }

                var previous = new Dictionary<string, string>(
                    this._aliasFactoryGenericParamExprs,
                    StringComparer.Ordinal);
                try
                {
                    for (var i = 0; i < generics.Count; i++)
                    {
                        var gp = generics[i];
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

                        this._aliasFactoryGenericParamExprs[gp.Name] = expr;
                    }

                    return this.BuildRuntimeTypeExpression(body, preferCtorLocals);
                }
                finally
                {
                    this._aliasFactoryGenericParamExprs.Clear();
                    foreach (var pair in previous)
                    {
                        this._aliasFactoryGenericParamExprs[pair.Key] = pair.Value;
                    }
                }
            }
            finally
            {
                this._objectShapeDescriptorName = previousShapeName;
            }
        }

        /// <summary>
        /// A value-position call of a source alias (<c>UserId()</c>, <c>\Ns\UserId()</c>,
        /// <c>Optional(typeof(int))</c>, <c>UserService::NameType()</c>) is a
        /// <c>\Tyhp\Type::ref</c> of the registered declaration. The same shape
        /// <c>typeof</c> already emits. Tyhpdef aliases are left as calls.
        /// </summary>
        private string? TryBuildValuePositionTypeRef(PhpDereferenceableAst dereferenceable, PhpCallAst call)
        {
            if (CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
            {
                return null;
            }

            if (!this.TryResolveValuePositionAlias(dereferenceable, out var alias, out var writtenName, out var typeArgBase))
            {
                return null;
            }

            this._context.RequirePackage("tyhp/core");
            return this.BuildRuntimeTypeRefCall(
                this.BuildAliasRefNameExpression(alias, writtenType: null, writtenName),
                this.BuildValuePositionRefArguments(alias, call, typeArgBase));
        }

        private bool TryResolveValuePositionAlias(
            PhpDereferenceableAst dereferenceable,
            out IBaseSymbol alias,
            out string? writtenName,
            out IDereferenceableBase? typeArgBase)
        {
            alias = null!;
            writtenName = null;
            typeArgBase = null;

            if (dereferenceable.Base is PhpNameAst name)
            {
                var resolved = this.ResolveEmitTypeAliasFactory(name);
                if (resolved is null)
                {
                    return false;
                }

                alias = resolved;
                writtenName = name.ValueString ?? name.Identifier;
                typeArgBase = name;
                return true;
            }

            if (dereferenceable.Base is PhpDereferenceableAst chain
                && this.TryResolveStaticAliasCall(chain) is { } objectAlias)
            {
                alias = objectAlias;
                writtenName = WrittenStaticAliasName(chain, objectAlias.Name);
                typeArgBase = chain;
                return true;
            }

            return false;
        }

        private TypeAliasSymbol? ResolveEmitTypeAliasFactory(PhpNameAst name)
        {
            if (this._context.GlobalScope is null)
            {
                return null;
            }

            var state = new CheckerState
            {
                CurrentNamespaceName = this._context.CurrentSourceNamespace?.Trim().TrimStart('\\'),
                CurrentFileName = this._context.CurrentOutputFile?.SourceFileAst?.Identifier
                    ?? this._context.CurrentSourceFile?.Identifier,
                NameResolutionScope = this.GetEmitAliasResolutionScope(),
                EnclosingObject = this._currentObjectSymbol,
            };
            var resolved = CheckerHelpers.ResolveTypeAliasFactory(
                name,
                state,
                this._context.GetSymbolTree(),
                this._context.GlobalScope);
            return resolved is not null && !IsTyhpdefAliasSymbol(resolved) ? resolved : null;
        }

        private ObjectTypeAliasSymbol? TryResolveStaticAliasCall(PhpDereferenceableAst chain)
        {
            if (chain.Base is not PhpNameAst ownerName || this._context.GlobalScope is null)
            {
                return null;
            }

            var memberName = StaticCallMemberName(chain);
            if (string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var ownerText = (ownerName.ValueString ?? ownerName.Identifier ?? "").Trim();
            var simple = ownerText.TrimStart('\\');
            if (simple.Length == 0)
            {
                return null;
            }

            var ownerSegments = simple.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (ownerSegments.Length == 0)
            {
                return null;
            }

            var segments = new string[ownerSegments.Length + 1];
            ownerSegments.CopyTo(segments, 0);
            segments[^1] = memberName;

            var resolver = new NameResolver(this._context.GetSymbolTree(), new DiagnosticBag());
            var resolved = resolver.TryResolveObjectTypeAlias(
                segments,
                this.GetEmitAliasResolutionScope(),
                isFullyQualified: ownerText.StartsWith('\\'));
            if (resolved is ObjectTypeAliasSymbol alias && !IsTyhpdefAliasSymbol(alias))
            {
                return alias;
            }

            if (ownerSegments.Length == 1 && IsRelativeClassKeyword(ownerSegments[0]))
            {
                var owner = string.Equals(ownerSegments[0], "parent", StringComparison.OrdinalIgnoreCase)
                    ? this.TryResolveEmitParent(this._currentObjectSymbol)
                    : this._currentObjectSymbol;
                return this.FindSourceAliasMember(owner, memberName);
            }

            return null;
        }

        private ObjectTypeAliasSymbol? FindSourceAliasMember(ObjectDeclarationSymbol? owner, string memberName)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (owner is not null && seen.Add(owner))
            {
                if (owner.Members.TryGetValue(memberName, out var member))
                {
                    return member is ObjectTypeAliasSymbol alias && !IsTyhpdefAliasSymbol(alias)
                        ? alias
                        : null;
                }

                owner = this.TryResolveEmitParent(owner);
            }

            return null;
        }

        /// <summary>
        /// Namespace scope for the file being emitted, or the class scope when a method body
        /// is being emitted so <c>self</c> / <c>parent</c> / <c>static</c> alias calls resolve.
        /// </summary>
        private IBaseScope GetEmitAliasResolutionScope()
        {
            var scope = this.GetEmitResolutionScope();
            if (this._currentObjectSymbol is null)
            {
                return scope;
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                if (ReferenceEquals(child.DeclarationSymbol, this._currentObjectSymbol))
                {
                    return child;
                }
            }

            var parent = this._currentObjectSymbol.ContainingScope;
            if (parent is null)
            {
                return scope;
            }

            foreach (var child in parent.GetAllChildScopes())
            {
                if (ReferenceEquals(child.DeclarationSymbol, this._currentObjectSymbol))
                {
                    return child;
                }
            }

            return scope;
        }

        private List<string> BuildValuePositionRefArguments(
            IBaseSymbol alias,
            PhpCallAst call,
            IDereferenceableBase? typeArgBase)
        {
            var generics = CheckerHelpers.GetAliasFactoryGenericParameters(alias);
            var callArgs = call.Arguments?.GetAllNotNull().ToList() ?? [];
            if (callArgs.Count > 0)
            {
                var emitted = new List<string?>(generics.Count);
                while (emitted.Count < generics.Count)
                {
                    emitted.Add(null);
                }

                var positional = 0;
                foreach (var arg in callArgs)
                {
                    var text = arg.IsVariadic
                        ? "..." + this.BuildExpression(arg.Expression)
                        : this.BuildExpression(arg.Expression);
                    var named = arg.Name?.ValueString?.Trim().TrimStart('$');
                    if (!string.IsNullOrEmpty(named))
                    {
                        var index = IndexOfGenericParameter(generics, named);
                        if (index >= 0)
                        {
                            while (emitted.Count <= index)
                            {
                                emitted.Add(null);
                            }

                            emitted[index] = text;
                            continue;
                        }
                    }

                    while (emitted.Count <= positional)
                    {
                        emitted.Add(null);
                    }

                    emitted[positional] = text;
                    positional++;
                }

                for (var i = 0; i < emitted.Count; i++)
                {
                    if (emitted[i] is not null)
                    {
                        continue;
                    }

                    emitted[i] = i < generics.Count
                        ? this.DefaultAliasGenericExpr(generics[i])
                        : $"{RuntimeTypeClassFq}::mixed()";
                }

                return emitted.Select(expr => expr!).ToList();
            }

            var fromTypes = new List<string>();
            var typeArgs = TypeInferrer.TryGetCallSiteTypeArgumentList(typeArgBase);
            if (typeArgs is null
                && call.AstGrammarAddons.TryGetValue("genericTypeArguments", out var addon))
            {
                typeArgs = addon as PhpTypeExpressionListAst;
            }

            if (typeArgs is not null)
            {
                foreach (var typeArg in typeArgs.GetAllNotNull().OfType<ITypeExpression>())
                {
                    fromTypes.Add(this.BuildRuntimeTypeExpression(typeArg, preferCtorLocals: false));
                }
            }

            for (var i = fromTypes.Count; i < generics.Count; i++)
            {
                fromTypes.Add(this.DefaultAliasGenericExpr(generics[i]));
            }

            return fromTypes;
        }

        private string DefaultAliasGenericExpr(GenericTypeParameterSymbol generic) =>
            generic.DefaultType is not null
                ? this.BuildRuntimeTypeExpression(generic.DefaultType, preferCtorLocals: false)
                : $"{RuntimeTypeClassFq}::mixed()";

        private static int IndexOfGenericParameter(
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            string name)
        {
            for (var i = 0; i < generics.Count; i++)
            {
                if (string.Equals(generics[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string? StaticCallMemberName(PhpDereferenceableAst chain)
        {
            IExpression? member = chain.Suffix switch
            {
                PhpStaticMemberAccessAst staticAccess => staticAccess.Member,
                PhpClassConstantAccessAst classConst => classConst.Member,
                _ => null,
            };
            if (member is null)
            {
                return null;
            }

            var text = member.ValueString;
            if (string.IsNullOrWhiteSpace(text) && member is PhpNameAst name)
            {
                text = name.Identifier;
            }

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static string? WrittenStaticAliasName(PhpDereferenceableAst chain, string aliasName)
        {
            if (chain.Base is not PhpNameAst ownerName)
            {
                return null;
            }

            var ownerText = (ownerName.ValueString ?? ownerName.Identifier ?? "").Trim().TrimStart('\\');
            if (ownerText.Length == 0)
            {
                return null;
            }

            if (IsRelativeClassKeyword(ownerText))
            {
                ownerText = ownerText.ToLowerInvariant();
            }

            return ownerText + "\\" + aliasName;
        }

        private string BuildSourceAliasFactoryCall(
            IBaseSymbol alias,
            IReadOnlyList<ITypeExpression>? typeArgs,
            bool preferCtorLocals,
            ITypeExpression? writtenType = null,
            string? writtenName = null)
        {
            var args = new List<string>();
            if (typeArgs is not null)
            {
                foreach (var typeArg in typeArgs)
                {
                    args.Add(this.BuildRuntimeTypeExpression(typeArg, preferCtorLocals));
                }
            }

            var generics = CheckerHelpers.GetAliasFactoryGenericParameters(alias);
            for (var i = args.Count; i < generics.Count; i++)
            {
                var gp = generics[i];
                args.Add(gp.DefaultType is not null
                    ? this.BuildRuntimeTypeExpression(gp.DefaultType, preferCtorLocals)
                    : $"{RuntimeTypeClassFq}::mixed()");
            }

            return this.BuildRuntimeTypeRefCall(
                this.BuildAliasRefNameExpression(alias, writtenType, writtenName),
                args);
        }

        private string BuildTypeRegisterStatement(
            string fqn,
            IReadOnlyList<AliasFactoryGenericParam> genericParams,
            string bodyExpr)
        {
            var parameters = string.Join(
                ", ",
                genericParams.Select(gp => $"{RuntimeTypeClassFq} ${gp.Name}"));
            var closure = genericParams.Count == 0
                ? $"fn () => {bodyExpr}"
                : $"fn ({parameters}) => {bodyExpr}";
            var nameLiteral = FormatPhpArrayKey(new StructArrayKey(fqn, IsInteger: false));
            return $"{RuntimeTypeClassFq}::register({nameLiteral}, {closure});";
        }

        private string BuildRuntimeTypeRefCall(string nameExpr, IReadOnlyList<string> genericExprs)
        {
            return genericExprs.Count == 0
                ? $"{RuntimeTypeClassFq}::ref({nameExpr})"
                : $"{RuntimeTypeClassFq}::ref({nameExpr}, {string.Join(", ", genericExprs)})";
        }

        private string BuildAliasRefNameExpression(
            IBaseSymbol alias,
            ITypeExpression? writtenType,
            string? writtenName)
        {
            var written = writtenName ?? GetWrittenTypeSpelling(writtenType);
            if (alias is ObjectTypeAliasSymbol objectAlias)
            {
                var owner = objectAlias.ContainingScope?.DeclarationSymbol as ObjectDeclarationSymbol;
                var classFqn = (owner?.FullyQualifiedName ?? "").TrimStart('\\');
                var registered = string.IsNullOrEmpty(classFqn)
                    ? objectAlias.Name
                    : classFqn + "\\" + objectAlias.Name;

                if (TrySpellObjectAliasCalleeFromWritten(written, objectAlias.Name) is { } callee)
                {
                    var split = callee.LastIndexOf("::", StringComparison.Ordinal);
                    if (split > 0)
                    {
                        var qualifier = callee[..split];
                        if (qualifier is "self" or "static" or "parent")
                        {
                            if (this._emittingAliasFactorySymbol is not null)
                            {
                                var boundFqn = string.Equals(qualifier, "parent", StringComparison.Ordinal)
                                    ? this.TryGetCurrentParentClassFqn()
                                    : this._currentObjectFqn?.TrimStart('\\');
                                if (!string.IsNullOrEmpty(boundFqn))
                                {
                                    registered = boundFqn + "\\" + objectAlias.Name;
                                }
                            }
                            else
                            {
                                return qualifier + "::class . '\\\\" + objectAlias.Name + "'";
                            }
                        }
                    }
                }

                return FormatPhpArrayKey(new StructArrayKey(registered, IsInteger: false));
            }

            var fqn = (alias.FullyQualifiedName ?? alias.Name).TrimStart('\\');
            if (string.IsNullOrEmpty(fqn))
            {
                fqn = alias.Name;
            }

            return FormatPhpArrayKey(new StructArrayKey(fqn, IsInteger: false));
        }

        private string BuildSelfStaticParentRuntimeClass(string spelling)
        {
            if (string.Equals(spelling, "parent", StringComparison.OrdinalIgnoreCase))
            {
                if (this.TryGetCurrentParentClassFqn() is { } parentFqn)
                {
                    return $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(parentFqn)}::class)";
                }
            }
            else if (this._currentObjectFqn is { } fqn)
            {
                return $"{RuntimeTypeClassFq}::fromClassName({QuoteClassNameForType(fqn)}::class)";
            }

            return $"{RuntimeTypeClassFq}::fromClassName({spelling}::class)";
        }

        private string? TryGetCurrentParentClassFqn()
        {
            var extends = this._currentObjectDecl?.Extends;
            if (extends is null)
            {
                return null;
            }

            if (extends.BoundSymbol is ObjectDeclarationSymbol parent
                && !string.IsNullOrWhiteSpace(parent.FullyQualifiedName))
            {
                return parent.FullyQualifiedName.TrimStart('\\');
            }

            var written = (extends.ValueString ?? "").Trim().TrimStart('\\');
            return string.IsNullOrEmpty(written) ? null : written;
        }

        private static string? StructRegistrationFqn(TyhpTypeAliasAst alias)
        {
            if (alias.BoundSymbol is not ObjectDeclarationSymbol { IsStruct: true } structDecl)
            {
                return null;
            }

            var fqn = (structDecl.FullyQualifiedName ?? structDecl.Name).TrimStart('\\');
            return string.IsNullOrEmpty(fqn) ? structDecl.Name : fqn;
        }

        private static string DeclarationRegistrationName(TyhpTypeAliasAst alias, string shortName)
        {
            if (StructRegistrationFqn(alias) is { } structFqn)
            {
                return structFqn;
            }

            var fromSymbol = (alias.BoundSymbol?.FullyQualifiedName ?? shortName).TrimStart('\\');
            return string.IsNullOrEmpty(fromSymbol) ? shortName : fromSymbol;
        }

        private string SpellSourceAliasFactoryCallee(
            IBaseSymbol alias,
            ITypeExpression? writtenType = null,
            string? writtenName = null)
        {
            var written = writtenName ?? GetWrittenTypeSpelling(writtenType);

            if (alias is ObjectTypeAliasSymbol objectAlias)
            {
                if (TrySpellObjectAliasCalleeFromWritten(written, objectAlias.Name) is { } fromWritten)
                {
                    return fromWritten;
                }

                var owner = objectAlias.ContainingScope?.DeclarationSymbol as ObjectDeclarationSymbol;
                if (owner is not null
                    && this._currentObjectSymbol is not null
                    && (ReferenceEquals(this._currentObjectSymbol, owner)
                        || string.Equals(
                            this._currentObjectSymbol.FullyQualifiedName,
                            owner.FullyQualifiedName,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return "self::" + objectAlias.Name;
                }

                var classFqn = (owner?.FullyQualifiedName ?? "").TrimStart('\\');
                return string.IsNullOrEmpty(classFqn)
                    ? objectAlias.Name
                    : "\\" + classFqn + "::" + objectAlias.Name;
            }

            if (!string.IsNullOrEmpty(written) && written.StartsWith('\\'))
            {
                return written;
            }

            var fqn = (alias.FullyQualifiedName ?? alias.Name).TrimStart('\\');
            var lastSlash = fqn.LastIndexOf('\\');
            var ns = lastSlash < 0 ? "" : fqn[..lastSlash];
            var currentNs = (this._context.CurrentSourceNamespace ?? "").Trim().TrimStart('\\');
            if (string.Equals(ns, currentNs, StringComparison.OrdinalIgnoreCase)
                || this.CurrentFileImportsAliasFactory(fqn))
            {
                return alias.Name;
            }

            if (!string.IsNullOrEmpty(written) && written.Contains('\\'))
            {
                return "\\" + written.TrimStart('\\');
            }

            if (!string.IsNullOrEmpty(written))
            {
                return written;
            }

            return string.IsNullOrEmpty(fqn) ? alias.Name : "\\" + fqn;
        }

        /// <summary>
        /// True when this PHP file's Tyhp <c>use</c> list includes <paramref name="aliasFqn"/>
        /// (class-kind import of a type alias). The factory should then be spelled as a short
        /// name so import prune can emit <c>use function</c>.
        /// </summary>
        private bool CurrentFileImportsAliasFactory(string aliasFqn)
        {
            if (string.IsNullOrEmpty(aliasFqn) || this._context.CurrentOutputFile is null)
            {
                return false;
            }

            foreach (var list in this._context.CurrentOutputFile.FileImports)
            {
                foreach (var import in list.GetAllNotNull())
                {
                    var imported = (import.NamespaceName ?? "").TrimStart('\\');
                    if (string.Equals(imported, aliasFqn, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Preserves the written qualifier for class-level aliases:
        /// <c>self\NameType</c> → <c>self::NameType</c>, <c>C\NameType</c> → <c>C::NameType</c>.
        /// </summary>
        private static string? TrySpellObjectAliasCalleeFromWritten(string? written, string aliasName)
        {
            if (string.IsNullOrEmpty(written))
            {
                return null;
            }

            var slash = written.LastIndexOf('\\');
            if (slash < 0)
            {
                return null;
            }

            var qualifier = written[..slash];
            var member = written[(slash + 1)..];
            if (string.IsNullOrEmpty(member)
                || !string.Equals(member, aliasName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrEmpty(qualifier))
            {
                return null;
            }

            return qualifier + "::" + member;
        }

        private IBaseSymbol? TryGetRuntimeTypeAliasSymbol(ITypeExpression typeExpr)
        {
            if (TryGetGenericTypeParameterSymbolFromType(typeExpr) is not null)
            {
                return null;
            }

            var bound = GetBoundTypeAliasSymbol(typeExpr);
            if (bound is not null)
            {
                return bound;
            }

            var written = GetWrittenTypeSpelling(typeExpr);
            if (string.IsNullOrEmpty(written)
                || this.IsErasedGenericParamName(written.TrimStart('\\')))
            {
                return null;
            }

            return this.TryResolveTypeAliasByWrittenName(written);
        }

        private IBaseSymbol? TryResolveTypeAliasByWrittenName(string? written)
        {
            if (string.IsNullOrWhiteSpace(written))
            {
                return null;
            }

            var trimmed = written.Trim();
            var simple = trimmed.TrimStart('\\');
            if (string.IsNullOrEmpty(simple))
            {
                return null;
            }

            if (simple.Contains('\\'))
            {
                return this.TryResolveQualifiedTypeAlias(simple, trimmed.StartsWith('\\'));
            }

            return FindTypeAliasNamed(this._context.GlobalScope, simple);
        }

        private IBaseSymbol? TryResolveQualifiedTypeAlias(string simple, bool fullyQualified)
        {
            var segments = simple.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return FindTypeAliasNamed(this._context.GlobalScope, simple);
            }

            var aliasName = segments[^1];
            var ownerName = string.Join('\\', segments[..^1]);
            ObjectDeclarationSymbol? owner = null;
            if (segments.Length == 2
                && (string.Equals(segments[0], "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segments[0], "static", StringComparison.OrdinalIgnoreCase)))
            {
                owner = this._currentObjectSymbol;
            }
            else
            {
                owner = this.TryResolveObjectByName(fullyQualified ? "\\" + ownerName : ownerName)
                    ?? this.TryResolveObjectByName(ownerName);
            }

            if (owner is not null
                && owner.Members.TryGetValue(aliasName, out var member)
                && member is ObjectTypeAliasSymbol objectAlias)
            {
                return objectAlias;
            }

            return FindTypeAliasNamed(this._context.GlobalScope, simple);
        }

        private static TypeAliasSymbol? FindTypeAliasNamed(
            IBaseScope scope,
            string name,
            int depth = 0)
        {
            if (depth > 500)
            {
                return null;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is TypeAliasSymbol alias)
                {
                    if (string.Equals(alias.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return alias;
                    }

                    var fqn = (alias.FullyQualifiedName ?? alias.Name).TrimStart('\\');
                    if (string.Equals(fqn, name, StringComparison.OrdinalIgnoreCase)
                        || fqn.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase))
                    {
                        return alias;
                    }
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                var found = FindTypeAliasNamed(child, name, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GenericTypeParameterSymbol? TryGetGenericTypeParameterSymbolFromType(
            ITypeExpression typeExpr)
        {
            if (typeExpr.BoundSymbol is GenericTypeParameterSymbol fromExpr)
            {
                return fromExpr;
            }

            if (typeExpr is PhpNamedTypeAst named)
            {
                return TryGetGenericTypeParameterSymbol(named);
            }

            if (typeExpr is PhpNameAst { BoundSymbol: GenericTypeParameterSymbol fromName })
            {
                return fromName;
            }

            return null;
        }

        private static IBaseSymbol? GetBoundTypeAliasSymbol(ITypeExpression typeExpr)
        {
            if (typeExpr.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                return typeExpr.BoundSymbol;
            }

            if (typeExpr is PhpTypeExpressionAst { IsNullable: false, TypeKind: PhpTypeKind.Simple } composite
                && composite.Types is { } members)
            {
                var list = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                if (list.Count == 1)
                {
                    return GetBoundTypeAliasSymbol(list[0]);
                }
            }

            if (typeExpr is PhpNamedTypeAst named)
            {
                if (named.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
                {
                    return named.BoundSymbol;
                }

                if (named.Name is IBase2Ast nameNode
                    && nameNode.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
                {
                    return nameNode.BoundSymbol;
                }
            }

            if (typeExpr is PhpNameAst name
                && name.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                return name.BoundSymbol;
            }

            return null;
        }

        private static string? GetWrittenTypeSpelling(ITypeExpression? typeExpr)
        {
            return typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst name => name.ValueString,
                PhpNamedTypeAst named => named.Name switch
                {
                    PhpBuiltinTypeAst builtin => builtin.Identifier,
                    PhpNameAst name => name.ValueString,
                    _ => null,
                },
                PhpTypeExpressionAst { IsNullable: false, TypeKind: PhpTypeKind.Simple } composite
                    when composite.Types is { } members
                    && members.GetAllNotNull().OfType<ITypeExpression>().ToList() is { Count: 1 } list =>
                    GetWrittenTypeSpelling(list[0]),
                _ => null,
            };
        }

        private static IReadOnlyList<ITypeExpression>? GetTypeArgumentsForAliasUse(ITypeExpression typeExpr)
        {
            if (typeExpr is PhpTypeExpressionAst { IsNullable: false, TypeKind: PhpTypeKind.Simple } composite
                && composite.Types is { } members)
            {
                var list = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                if (list.Count == 1)
                {
                    return GetTypeArgumentsForAliasUse(list[0]);
                }
            }

            var fromNode = GetGenericTypeArgumentAddon(typeExpr);
            if (fromNode is { Count: > 0 })
            {
                return fromNode;
            }

            if (typeExpr is PhpNamedTypeAst { Name: IBase2Ast nameNode })
            {
                return GetGenericTypeArgumentAddon(nameNode);
            }

            return null;
        }

        private static string? TryGetSelfStaticParentSpelling(ITypeExpression typeExpr)
        {
            var written = typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst name => name.ValueString,
                PhpNamedTypeAst named => named.Name switch
                {
                    PhpBuiltinTypeAst builtin => builtin.Identifier,
                    PhpNameAst name => name.ValueString,
                    _ => null,
                },
                _ => null,
            };

            var simple = written?.TrimStart('\\');
            if (string.Equals(simple, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(simple, "static", StringComparison.OrdinalIgnoreCase)
                || string.Equals(simple, "parent", StringComparison.OrdinalIgnoreCase))
            {
                return simple;
            }

            return null;
        }

        private static bool IsNullTypeExpression(ITypeExpression typeExpr)
        {
            var written = typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst name => name.ValueString,
                PhpNamedTypeAst named => named.Name switch
                {
                    PhpBuiltinTypeAst builtin => builtin.Identifier,
                    PhpNameAst name => name.ValueString,
                    _ => null,
                },
                _ => null,
            };

            return string.Equals(written?.TrimStart('\\'), "null", StringComparison.OrdinalIgnoreCase);
        }
    }
}
