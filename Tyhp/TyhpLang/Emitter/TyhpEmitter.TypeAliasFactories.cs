using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Source type-alias declarations emit a <c>\Tyhp\Type</c> factory (namespace function or
    /// class static method). PHP type hints still expand via <see cref="TypeSpellingHelper"/>.
    /// </summary>
    public partial class TyhpEmitter
    {
        private readonly record struct AliasFactoryGenericParam(string Name, ITypeExpression? DefaultType);

        private EmitItem EmitTypeAliasFactory(TyhpTypeAliasAst alias, EmitItem parent, bool classLevel)
        {
            var emptyType = classLevel ? EmitType.ObjectStaticMethods : EmitType.RootStatement;
            if (!ShouldEmitSourceTypeAliasFactory(alias)
                || !ShouldEmitPhpVersionGatedDeclaration(alias)
                || NoEmitAttributeSupport.ShouldOmitDeclaration(alias)
                || (TypeAliasIsCircular(alias.BoundSymbol)
                    && alias.ObjectShape is null
                    && alias.CallableShape is null))
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

            this._aliasFactoryGenericParamExprs.Clear();
            foreach (var gp in genericParams)
            {
                this._aliasFactoryGenericParamExprs[gp.Name] = "$" + gp.Name;
            }

            this._emittingAliasFactorySymbol = alias.BoundSymbol;
            if (classLevel)
            {
                this._currentMemberIsStatic = true;
            }

            try
            {
                var signature = this.BuildAliasFactorySignature(alias, name, genericParams, classLevel);
                var block = this.ApplyDocComment(
                    alias,
                    EmitItem.BlockBraceNextLine(alias, emptyType, signature, "}", parent));
                this.AttachAttributes(alias, block);

                foreach (var gp in genericParams)
                {
                    var defaultExpr = gp.DefaultType is not null
                        ? this.BuildRuntimeTypeExpression(gp.DefaultType, preferCtorLocals: false)
                        : $"{RuntimeTypeClassFq}::mixed()";
                    EmitItem.Line(
                        alias,
                        EmitType.FunctionStatement,
                        $"${gp.Name} ??= {defaultExpr};",
                        block);
                }

                var bodyExpr = this.BuildRuntimeTypeExpression(alias.TypeExpression, preferCtorLocals: false);
                EmitItem.Line(alias, EmitType.FunctionStatement, "return " + bodyExpr + ";", block);
                return block;
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
            }
        }

        private static bool ShouldEmitSourceTypeAliasFactory(TyhpTypeAliasAst alias)
            => !IsTyhpdefTypeAlias(alias)
                && alias.StructShape is null
                && alias.BoundSymbol is not ObjectDeclarationSymbol { IsStruct: true };

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
                    return $"{RuntimeTypeClassFq}::fromClassName({selfName}::class)";
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

            var callee = this.SpellSourceAliasFactoryCallee(alias, writtenType, writtenName);
            return callee + "(" + string.Join(", ", args) + ")";
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
