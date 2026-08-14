using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Mechanism C — runtime generic plumbing emitted onto the author's own class.
    ///
    /// Generic initialization is separated from the constructor entirely: binding type arguments is
    /// compiler bookkeeping that must run for every ancestor level before any author statement, while
    /// calling the parent constructor is author semantics chosen at the signature (<c>: parent(...)</c>
    /// versus <c>: void</c>). Each level in a generic chain therefore declares
    /// <c>__initGenerics__tyhpGeneric</c>, which records its own bindings and chains one hop to its
    /// parent's, and the author's <c>__construct</c> receives one injected statement gating a call to
    /// its own level's hook.
    ///
    /// See FOUND_BUGS.md "Mechanism C" for the design record and the verified probe.
    /// </summary>
    public partial class TyhpEmitter
    {
        /// <summary>
        /// Parameter the init hook collects its type arguments into. It carries nothing but generics,
        /// which is what keeps them out of the author's parameter list and lets a variadic constructor
        /// work at all.
        /// </summary>
        private const string GenericInitHookParam = "$generics";

        /// <summary>
        /// Resolves the Mechanism C shape for the object being emitted: whether it needs an init hook,
        /// whether that hook chains to a parent's, and the literal FQN its bindings are keyed on.
        /// </summary>
        private void ResolveGenericChainState(PhpObjectTypeDeclAst objectDecl)
        {
            this._currentObjectInGenericChain = false;
            this._currentObjectParentInGenericChain = false;
            this._currentObjectRecordsOwnGenerics = false;
            this._currentObjectFqn = null;

            if (this._currentObjectSymbol is null || objectDecl.IsAnonymousClass)
            {
                return;
            }

            // An interface or trait has no constructor to gate and no `parent::` to chain through.
            if (objectDecl.DeclType?.ValueString is { } declType
                && !string.Equals(declType, "class", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this._currentObjectFqn = this._currentObjectSymbol.FullyQualifiedName;

            var parent = this.TryResolveEmitParent(this._currentObjectSymbol);
            this._currentObjectParentInGenericChain = this.SymbolIsInGenericChain(parent);

            // A non-generic class between two generic ones still needs a hook, so that every level only
            // ever has to talk to its immediate parent (requirement 6).
            this._currentObjectInGenericChain =
                this._currentObjectNeedsGenericTracking || this._currentObjectParentInGenericChain;

            this._currentObjectRecordsOwnGenerics =
                this._currentObjectInGenericChain && this._currentObjectGenericParams.Count > 0;
        }

        private IReadOnlyList<GenericTypeParameterSymbol> OwnRecordedGenericParameters() =>
            this._currentObjectRecordsOwnGenerics
                ? this._currentObjectGenericParams
                : Array.Empty<GenericTypeParameterSymbol>();

        private ObjectDeclarationSymbol? TryResolveEmitParent(ObjectDeclarationSymbol? symbol)
        {
            if (symbol is null)
            {
                return null;
            }

            try
            {
                return TypeComparer.TryGetParentDeclaration(
                    symbol, this._context.GetSymbolTree(), this._context.GlobalScope);
            }
            catch (Exception)
            {
                // An unresolvable base is already diagnosed by the declaration rules; emission must not
                // fail because of it.
                return null;
            }
        }

        /// <summary>
        /// True when <paramref name="symbol"/> or any ancestor declares tracked generic parameters, and
        /// therefore carries an <c>__initGenerics__tyhpGeneric</c> this level can chain to.
        /// </summary>
        private bool SymbolIsInGenericChain(ObjectDeclarationSymbol? symbol)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (symbol is not null && seen.Add(symbol))
            {
                if (symbol.GenericParameters.Count > 0
                    && this._context.RequiresRuntimeGenericTrackingFor(symbol))
                {
                    return true;
                }

                symbol = this.TryResolveEmitParent(symbol);
            }

            return false;
        }

        /// <summary>
        /// The <c>HasGenerics</c> trait is applied once, at the topmost generic level. Re-applying it
        /// lower down redeclares the trait's public <c>$__tyhpGeneric</c> storage.
        /// </summary>
        private bool ShouldApplyGenericObjectTrait() =>
            this._currentObjectInGenericChain && !this._currentObjectParentInGenericChain;

        /// <summary>
        /// Emits <c>__initGenerics__tyhpGeneric</c>: resolve this level's arguments against their
        /// declared defaults, record them keyed by declaring class, register generic-typed properties,
        /// then either chain one hop to the parent or mark the object bound.
        /// </summary>
        private void EmitGenericInitHook(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
        {
            if (!this._currentObjectInGenericChain || this._currentObjectFqn is null)
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            var declaringClass = FormatDeclaringClassLiteral(this._currentObjectFqn);
            var ownParams = this.OwnRecordedGenericParameters();

            var block = EmitItem.BlockBraceNextLine(
                objectDecl,
                EmitType.ObjectInstanceMethods,
                $"protected function {GeneratedNames.GenericInitHook}(?{RuntimeTypeClassFq} ...{GenericInitHookParam}): void",
                "}",
                classBlock);

            var lines = new List<string>
            {
                // Factories use newInstanceWithoutConstructor — boot traits (create bag) before any lookup.
                "$this->tyhpBootTraits();",
            };

            if (ownParams.Count > 0)
            {
                // Belt-and-braces against a level being re-entered within a single chain. The
                // per-object bound flag makes this unreachable in emitted code; a level contributing no
                // parameters of its own has no key to check and relies on the flag alone.
                lines.Add($"if ($this->__tyhpGeneric->isInitialized({declaringClass})) {{");
                lines.Add("    return;");
                lines.Add("}");
            }

            var locals = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < ownParams.Count; i++)
            {
                var gp = ownParams[i];
                var local = ToGenericLocalVarName(gp.Name);
                locals[gp.Name] = local;
                lines.Add(
                    $"${local} = {GenericInitHookParam}[{i}] ?? {this.BuildUnboundGenericFallback(gp)};");
            }

            if (ownParams.Count > 0)
            {
                var namedTypes = ownParams.Select(gp =>
                    $"new {RuntimeNamedTypeFq}('{gp.Name}', ${locals[gp.Name]})");
                lines.Add(
                    $"$this->__tyhpGeneric->init(static::class, {declaringClass}, {string.Join(", ", namedTypes)});");
            }

            // Generic-typed property registration belongs here rather than in the constructor: the
            // whole init chain completes before any constructor statement, and the resolved type
            // arguments are in scope as locals.
            var previousLocals = this._ctorGenericLocalVars;
            this._ctorGenericLocalVars = locals.Count > 0 ? locals : null;
            try
            {
                foreach (var (propName, typeExpr) in this.CollectGenericTypedProperties())
                {
                    var typePhp = this.BuildRuntimeTypeExpression(typeExpr, preferCtorLocals: true);
                    lines.Add($"$this->__tyhpGeneric->setPropertyType('{propName}', {typePhp});");
                }

                if (this._currentObjectParentInGenericChain)
                {
                    var chainArgs = this.BuildParentInitChainArguments(objectDecl, locals);
                    lines.Add($"parent::{GeneratedNames.GenericInitHook}({chainArgs});");
                }
                else
                {
                    // Topmost generic level: the chain completes here, so this is where the object is
                    // marked bound. Marking at the end means a set flag implies a completed chain.
                    lines.Add("$this->__tyhpGeneric->markBound();");
                }
            }
            finally
            {
                this._ctorGenericLocalVars = previousLocals;
            }

            EmitItem.MultiLine(objectDecl, EmitType.FunctionStatement, lines, block);
        }

        /// <summary>
        /// The value an unbound type parameter resolves to: its declared default, else the broadest
        /// type its constraint allows, else <c>mixed</c>. Nothing is left null, so a lookup never has to
        /// distinguish "not passed" from "bound to null".
        /// </summary>
        private string BuildUnboundGenericFallback(GenericTypeParameterSymbol parameter)
        {
            if (parameter.DefaultType is { } defaultType)
            {
                return this.BuildRuntimeTypeExpression(defaultType, preferCtorLocals: false);
            }

            if (parameter.Constraint is { } constraint)
            {
                return this.BuildRuntimeTypeExpression(constraint, preferCtorLocals: false);
            }

            return $"{RuntimeTypeClassFq}::mixed()";
        }

        /// <summary>
        /// The argument list for the <c>parent::__initGenerics__tyhpGeneric(...)</c> chain call, derived
        /// from the <c>extends Other&lt;T, string&gt;</c> type arguments: a reference to one of this
        /// level's own parameters forwards its resolved value, anything else is bound at emit time.
        /// </summary>
        private string BuildParentInitChainArguments(
            PhpObjectTypeDeclAst objectDecl,
            IReadOnlyDictionary<string, string> locals)
        {
            var parentSymbol = this.TryResolveEmitParent(this._currentObjectSymbol);
            var parentParamCount = parentSymbol?.GenericParameters.Count ?? 0;
            var typeArgs = GetExtendsTypeArguments(objectDecl);

            // A parent that declares no parameters of its own still forwards the chain, and takes no
            // arguments to do it.
            var count = Math.Max(parentParamCount, typeArgs?.Count ?? 0);
            if (count == 0)
            {
                return "";
            }

            var args = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var typeArg = typeArgs is not null && i < typeArgs.Count ? typeArgs[i] : null;
                if (typeArg is null)
                {
                    // Not spelled at the `extends` clause; let the parent's own hook resolve it against
                    // its declared default.
                    args.Add("null");
                    continue;
                }

                if (TryGetSimpleTypeName(typeArg) is { } simple
                    && locals.TryGetValue(simple, out var local))
                {
                    args.Add("$" + local);
                    continue;
                }

                args.Add(this.BuildRuntimeTypeExpression(typeArg, preferCtorLocals: false));
            }

            return string.Join(", ", args);
        }

        /// <summary>
        /// Type arguments on the <c>extends</c> clause. They ride on the class name's "identifier"
        /// grammar addon (see <c>VisitClassName</c>), not on the declaration node.
        /// </summary>
        private static IReadOnlyList<ITypeExpression>? GetExtendsTypeArguments(PhpObjectTypeDeclAst objectDecl)
        {
            if (objectDecl.Extends is not IBase2Ast extendsNode)
            {
                return null;
            }

            if (extendsNode.AstGrammarAddons.TryGetValue("identifier", out var addon)
                && addon is PhpTypeExpressionListAst list)
            {
                return list.GetAllNotNull().ToList();
            }

            return GetGenericTypeArgumentAddon(extendsNode);
        }

        private static string? TryGetSimpleTypeName(ITypeExpression typeArg)
        {
            // A type argument arrives wrapped in a composite even when it names exactly one type.
            if (typeArg is PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                var only = members.GetAllNotNull().ToList();
                if (only.Count == 1)
                {
                    return TryGetSimpleTypeName(only[0]);
                }
            }

            // TyhpGenericIdentifierAst derives from PhpNameAst, so the derived arms have to come first.
            var name = typeArg switch
            {
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst g } => g.ValueString,
                PhpNamedTypeAst { Name: PhpNameAst inner } => inner.ValueString,
                TyhpGenericIdentifierAst g => g.ValueString,
                PhpNameAst n => n.ValueString,
                _ => null,
            };

            var simple = name?.TrimStart('\\');
            return string.IsNullOrEmpty(simple) || simple.Contains('\\') ? null : simple;
        }

        /// <summary>
        /// The declaring class is written as a literal fully qualified name rather than
        /// <c>self::class</c>, so the registry key means <em>the level that declared the parameter</em>
        /// rather than whichever class the emitted code ended up in.
        /// </summary>
        private static string FormatDeclaringClassLiteral(string fullyQualifiedName) =>
            "\\" + fullyQualifiedName.TrimStart('\\') + "::class";

        /// <summary>
        /// The constructor whose signature this class presents: its own if it declares one, otherwise
        /// the nearest inherited one. A class in a generic chain that declares no constructor still
        /// needs one synthesized around the inherited signature, because letting the ancestor's
        /// constructor be inherited outright would run that ancestor's gate — and its <c>self::</c> is
        /// pinned to the ancestor's level, binding this class's declared type arguments to the
        /// ancestor's defaults instead.
        /// </summary>
        private PhpMethodDeclAst? TryFindConstructorForEmit(out bool isInherited)
        {
            isInherited = false;

            if (this._currentObjectDecl is not null
                && FindDeclaredConstructor(this._currentObjectDecl) is { } own)
            {
                return own;
            }

            var symbol = this.TryResolveEmitParent(this._currentObjectSymbol);
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (symbol is not null && seen.Add(symbol))
            {
                if (symbol.DeclaringAstNode is PhpObjectTypeDeclAst ancestorDecl
                    && FindDeclaredConstructor(ancestorDecl) is { } inherited)
                {
                    isInherited = true;
                    return inherited;
                }

                symbol = this.TryResolveEmitParent(symbol);
            }

            return null;
        }

        private static PhpMethodDeclAst? FindDeclaredConstructor(PhpObjectTypeDeclAst objectDecl)
        {
            foreach (var member in objectDecl.Body?.GetAllNotNull() ?? [])
            {
                if (member is PhpMethodDeclAst method
                    && string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase))
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Synthesizes the constructor for a class in a generic chain that declares none: the injected
        /// gate, then a forward to the inherited constructor with its parameter list re-spelled.
        /// </summary>
        private void EmitSynthesizedGenericConstructor(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
        {
            if (!this._currentObjectInGenericChain || this._currentObjectEmittedConstructor)
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            var inheritedCtor = this.TryFindConstructorForEmit(out var isInherited);
            var parameters = isInherited ? inheritedCtor?.Parameters : null;

            var block = EmitItem.BlockBraceNextLine(
                objectDecl,
                EmitType.ObjectConstructor,
                $"public function __construct({this.BuildForwardingParameterList(parameters)})",
                "}",
                classBlock);

            this.EmitGenericObjectConstructorPrologue(objectDecl, block, includeUserParamChecks: false);
            this.EmitPropertyAccessorConstructorPrologue(objectDecl, block, classBlock);

            // Keyed on whether an ancestor declares a constructor at all, not on whether it takes
            // parameters: a no-argument ancestor constructor still has to run, and the class declared no
            // `: void` to opt out of it.
            if (isInherited)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.FunctionStatement,
                    $"parent::__construct({BuildForwardingArguments(parameters)});",
                    block);
            }

            this.EmitGenericObjectEnablePropertyChecksIfNeeded(objectDecl, block);

            this._currentObjectEmittedConstructor = true;
        }

        /// <summary>
        /// Emits the factory that instantiation with explicit type arguments routes through. It binds
        /// generics onto an unconstructed instance and <em>then</em> runs the author's constructor, which
        /// is why promoted and <c>readonly</c> parameters need no lowering: promotion happens exactly
        /// where PHP normally performs it.
        /// </summary>
        private void EmitGenericFactoryIfNeeded(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
        {
            // Only a class with type parameters of its own needs a factory. A non-generic level in the
            // chain is instantiated with plain `new`, whose gate reaches the same hook.
            if (!this._currentObjectRecordsOwnGenerics
                || this._currentObjectFqn is null
                || IsAbstractObjectDeclaration(objectDecl))
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            var ownParams = this.OwnRecordedGenericParameters();

            var ctor = this.TryFindConstructorForEmit(out _);
            var authorParams = this.BuildForwardingParameterList(ctor?.Parameters);
            var authorArgs = BuildForwardingArguments(ctor?.Parameters);

            var hidden = ownParams
                .Select(gp => $"?{RuntimeTypeClassFq} ${GeneratedNames.GenericVariantParameterPrefix}{gp.Name}")
                .ToList();
            var signatureParams = string.Join(
                ", ",
                string.IsNullOrEmpty(authorParams) ? hidden : hidden.Append(authorParams));

            EmitItem.Line(
                objectDecl,
                EmitType.ObjectStaticPropertyDeclaration,
                $"private static ?\\ReflectionClass ${GeneratedNames.ReflectedClassField} = null;",
                classBlock);

            var factoryName = GeneratedNames.GenericFactory(this._currentObjectFqn);
            var block = EmitItem.BlockBraceNextLine(
                objectDecl,
                EmitType.ObjectStaticMethods,
                $"final public static function {factoryName}({signatureParams}): self",
                "}",
                classBlock);

            var initArgs = string.Join(
                ", ",
                ownParams.Select(gp => $"${GeneratedNames.GenericVariantParameterPrefix}{gp.Name}"));

            // `self::class`, not `static::class`: the factory always constructs its own class, and the
            // FQN-derived name means every subclass needing generic instantiation gets its own.
            EmitItem.MultiLine(
                objectDecl,
                EmitType.FunctionStatement,
                [
                    $"self::${GeneratedNames.ReflectedClassField} ??= new \\ReflectionClass(self::class);",
                    $"$__tyhp_obj = self::${GeneratedNames.ReflectedClassField}->newInstanceWithoutConstructor();",
                    $"$__tyhp_obj->{GeneratedNames.GenericInitHook}({initArgs});",
                    $"$__tyhp_obj->__construct({authorArgs});",
                    "return $__tyhp_obj;",
                ],
                block);
        }

        private static bool IsAbstractObjectDeclaration(PhpObjectTypeDeclAst objectDecl) =>
            objectDecl.Modifiers?.Modifiers.Contains(PhpModifier.Abstract) == true;

        /// <summary>
        /// Re-spells a constructor's parameter list for a forwarding signature: promotion modifiers are
        /// dropped, because promotion belongs to the declaring constructor and re-declaring the property
        /// here would collide with it.
        /// </summary>
        private string BuildForwardingParameterList(PhpParameterListAst? parameters)
        {
            if (parameters is null)
            {
                return "";
            }

            return string.Join(
                ", ",
                parameters.GetAllNotNull().Select(p => this.FormatParameterWithoutPromotion(p)));
        }

        /// <summary>
        /// The arguments that forward a re-spelled parameter list onward, spreading a variadic.
        /// </summary>
        private static string BuildForwardingArguments(PhpParameterListAst? parameters)
        {
            if (parameters is null)
            {
                return "";
            }

            var args = parameters.GetAllNotNull()
                .Select(p => (p.IsVariadic ? "..." : "") + EnsureLeadingDollar(p.Name));

            return string.Join(", ", args);
        }

        private static string EnsureLeadingDollar(string name) =>
            name.StartsWith('$') ? name : "$" + name;
    }
}
