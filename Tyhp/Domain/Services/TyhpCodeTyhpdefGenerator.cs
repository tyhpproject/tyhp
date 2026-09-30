using System.Text.RegularExpressions;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Bound Tyhp public API → <c>package.tyhpdef</c> (and library <c>extra.tyhp.package</c> on
    /// publish-path <c>composer.json</c>).
    /// Runs from <c>tyhp build</c> after check and before the optimizer.
    /// </summary>
    public sealed class TyhpCodeTyhpdefGenerator
    {
        private readonly GlobalScope _globalScope;
        private readonly TyhpdefGenerationOptions _options;
        private readonly DiagnosticBag _diagnostics;
        private readonly bool _isLibrary;
        private readonly bool _isTagless;
        private readonly bool _dryRun;
        private readonly IReadOnlySet<IBaseSymbol> _requiresGenericVariant;
        private readonly IReadOnlySet<ObjectDeclarationSymbol> _requiresRuntimeGenericTracking;
        private readonly IReadOnlyList<SrcFileAst> _parsedFiles;
        private readonly string _compilerVersion;
        private readonly Dictionary<string, HashSet<string>> _shapeUsedNames;
        private readonly List<(string Ns, TyhpdefTypeAlias Alias)> _harvestedShapes;

        private static readonly Regex SelfTypeAtom = new(
            @"(?<![A-Za-z0-9_])\\?self(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public TyhpCodeTyhpdefGenerator(
            GlobalScope globalScope,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            bool isLibrary,
            bool isTagless = false,
            bool dryRun = false,
            IReadOnlySet<IBaseSymbol>? requiresGenericVariant = null,
            IReadOnlySet<ObjectDeclarationSymbol>? requiresRuntimeGenericTracking = null,
            IReadOnlyList<SrcFileAst>? parsedFiles = null)
        {
            this._globalScope = globalScope ?? throw new ArgumentNullException(nameof(globalScope));
            this._options = options ?? throw new ArgumentNullException(nameof(options));
            this._diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            this._isLibrary = isLibrary;
            this._isTagless = isTagless;
            this._dryRun = dryRun;
            this._requiresGenericVariant = requiresGenericVariant ?? new HashSet<IBaseSymbol>();
            this._requiresRuntimeGenericTracking = requiresRuntimeGenericTracking
                ?? new HashSet<ObjectDeclarationSymbol>();
            this._parsedFiles = parsedFiles ?? [];
            this._compilerVersion = IncrementalBuildService.GetCompilerVersion();
            this._shapeUsedNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            this._harvestedShapes = [];
        }

        public TyhpdefGenerationResult Generate()
        {
            var result = new TyhpdefGenerationResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var file = this.BuildFile(result.Diagnostics);
            TyhpCodeTyhpdefExternPass.Apply(file, this._globalScope, this._options, this._parsedFiles);
            CountDeclarations(file, result);

            if (result.Diagnostics.HasErrors)
            {
                this._diagnostics.AddRange(result.Diagnostics);
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            var outputDir = this._options.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "package.tyhpdef",
                    0,
                    0,
                    "Tyhpdef output directory is empty");
                this._diagnostics.AddRange(result.Diagnostics);
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            var defPath = Path.Combine(outputDir, "package.tyhpdef");
            this.WriteTyhpdef(file, defPath, result);

            if (this._isLibrary && result.Success)
            {
                this.WriteManifest(
                    Path.Combine(outputDir, ComposerExtraTyhpPackageManifest.ComposerJsonFileName),
                    result);
            }

            this._diagnostics.AddRange(result.Diagnostics);
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        private TyhpdefFile BuildFile(DiagnosticBag diagnostics)
        {
            var namespaces = new Dictionary<string, TyhpdefNamespace>(StringComparer.OrdinalIgnoreCase);
            var globalTypes = new List<TyhpdefClassDeclaration>();
            var globalFunctions = new List<TyhpdefFunction>();
            var globalConstants = new List<TyhpdefConstant>();
            var typeAliases = new List<TyhpdefTypeAlias>();

            foreach (var symbol in this.EnumerateProjectSymbols())
            {
                if (symbol is ObjectDeclarationSymbol anon
                    && AnonymousClassShapeHarvest.IsAnonymousClassName(anon.Name))
                {
                    continue;
                }

                this.ShapeUsedNames(NamespaceOf(symbol)).Add(symbol.Name);
            }

            foreach (var symbol in this.EnumerateProjectSymbols())
            {
                var ns = NamespaceOf(symbol);
                switch (symbol)
                {
                    case ObjectDeclarationSymbol obj when AnonymousClassShapeHarvest.IsAnonymousClassName(obj.Name):
                        break;
                    case ObjectDeclarationSymbol obj when obj.IsExtension:
                        this.AddStandaloneExtension(obj, ns, namespaces, globalTypes, diagnostics);
                        break;
                    case ObjectDeclarationSymbol obj:
                        AddTo(ns, namespaces, globalTypes, this.MapType(obj, ns));
                        break;
                    case FunctionDeclarationSymbol fn:
                        AddTo(ns, namespaces, globalFunctions, this.MapFunction(fn, ns));
                        break;
                    case ConstantSymbol constant:
                        AddTo(ns, namespaces, globalConstants, this.MapConstant(constant, ns));
                        break;
                    case TypeAliasSymbol alias:
                        AddTo(ns, namespaces, typeAliases, this.MapTypeAlias(alias, ns));
                        break;
                }
            }

            foreach (var (ns, alias) in this._harvestedShapes)
            {
                AddTo(ns, namespaces, typeAliases, alias);
            }

            return new TyhpdefFile
            {
                Header = "Compiler: " + this._compilerVersion
                    + Environment.NewLine
                    + "Generated: " + DateTime.UtcNow.ToString("O"),
                Namespaces = namespaces.Values.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                GlobalTypes = globalTypes,
                GlobalFunctions = globalFunctions,
                GlobalConstants = globalConstants,
                TypeAliases = typeAliases,
                GlobalUses = this.CollectInPackageGlobalUses(),
            };
        }

        private IEnumerable<IBaseSymbol> EnumerateProjectSymbols()
        {
            var stack = new Stack<IBaseScope>();
            stack.Push(this._globalScope);
            while (stack.Count > 0)
            {
                var scope = stack.Pop();
                foreach (var symbol in scope.GetAllChildSymbols())
                {
                    if (!IsProjectTyhpSource(symbol)
                        || IsOmittedFromPublicApi(symbol)
                        || symbol is ObjectDeclarationSymbol obj
                            && AnonymousClassShapeHarvest.IsAnonymousClassName(obj.Name))
                    {
                        continue;
                    }

                    yield return symbol;
                }

                foreach (var child in scope.GetAllChildScopes())
                {
                    stack.Push(child);
                }
            }
        }

        private void AddStandaloneExtension(
            ObjectDeclarationSymbol extension,
            string ns,
            Dictionary<string, TyhpdefNamespace> namespaces,
            List<TyhpdefClassDeclaration> globalTypes,
            DiagnosticBag diagnostics)
        {
            var backerAlias = extension.Name + GeneratedNames.ExtensionBackerSuffix;
            var phpName = DeclaredTypeName(extension.Name);
            var resolver = new NameResolver(this._globalScope, this._diagnostics);
            var speller = new TyhpdefExpressionSpeller(type => this.Spell(type, ns), resolver);

            var backerMethods = new List<TyhpdefMethod>();
            var operatorGroups = new Dictionary<OverloadableOperator, List<ObjectOperatorOverloadMethodSymbol>>();
            var headerMappings = new List<TyhpdefExtensionMember>();
            var extensionGroups = new List<TyhpdefExtensionGroup>();
            var extensionScope = FindObjectScope(extension);

            this.MapExtensionBlockMembers(
                extensionScope == null
                    ? EnumeratePublicMembers(extension)
                    : MembersOf(extensionScope),
                backerAlias,
                ns,
                speller,
                diagnostics,
                headerMappings,
                backerMethods,
                operatorGroups);

            if (extensionScope != null)
            {
                foreach (var child in ((IBaseScope)extensionScope).GetAllChildScopes())
                {
                    if (child is not ObjectDeclarationScope groupScope
                        || groupScope.DeclarationSymbol is not ObjectDeclarationSymbol { IsExtensionTargetGroup: true } groupSymbol)
                    {
                        continue;
                    }

                    var mapped = new List<TyhpdefExtensionMember>();
                    this.MapExtensionBlockMembers(
                        MembersOf(groupScope),
                        backerAlias,
                        ns,
                        speller,
                        diagnostics,
                        mapped,
                        backerMethods,
                        operatorGroups);
                    extensionGroups.Add(new TyhpdefExtensionGroup
                    {
                        TargetType = this.SpellRequired(groupSymbol.PendingExtensionBlockTarget, ns),
                        GenericParameters = this.MapGenerics(groupSymbol.GenericParameters, ns),
                        Members = mapped,
                    });
                }
            }

            foreach (var (op, group) in operatorGroups)
            {
                if (op == OverloadableOperator.Convert)
                {
                    this.AddStandaloneConvertBackerMethods(group, ns, backerMethods);
                    continue;
                }

                var compiledName = OperatorMethodNameGenerator.GetMethodName(op);
                if (string.IsNullOrEmpty(compiledName))
                {
                    continue;
                }

                backerMethods.Add(this.MapBackerOperatorMethod(compiledName, group, ns));
            }

            if (backerMethods.Count > 0)
            {
                AddTo(ns, namespaces, globalTypes, new TyhpdefClassDeclaration
                {
                    Kind = "class",
                    Name = phpName,
                    AsAlias = backerAlias,
                    DocComment = this.CopyDoc(extension.DocComment),
                    IsDeprecated = extension.IsDeprecated,
                    IsObsolete = extension.IsObsolete,
                    Attributes = this.MapDeclarationAttributes(extension.DeclaringAstNode),
                    Methods = backerMethods,
                });
            }

            var extensionAttributes = this.MapDeclarationAttributes(extension.DeclaringAstNode);
            this.StampGenericRuntime(extensionAttributes, this.FactoryStamp(extension));
            AddTo(ns, namespaces, globalTypes, new TyhpdefClassDeclaration
            {
                Kind = "extension",
                Name = extension.Name,
                GenericParameters = this.MapGenerics(extension.GenericParameters, ns),
                Extends = this.Spell(extension.PendingExtensionBlockTarget, ns),
                DocComment = this.CopyDoc(extension.DocComment),
                IsDeprecated = extension.IsDeprecated,
                IsObsolete = extension.IsObsolete,
                Attributes = extensionAttributes,
                ExtensionMembers = headerMappings,
                ExtensionGroups = extensionGroups,
            });
        }

        private void MapExtensionBlockMembers(
            IEnumerable<IBaseSymbol> members,
            string backerAlias,
            string ns,
            TyhpdefExpressionSpeller speller,
            DiagnosticBag diagnostics,
            List<TyhpdefExtensionMember> mappings,
            List<TyhpdefMethod> backerMethods,
            Dictionary<OverloadableOperator, List<ObjectOperatorOverloadMethodSymbol>> operatorGroups)
        {
            foreach (var member in members)
            {
                switch (member)
                {
                    case ObjectOperatorOverloadMethodSymbol op:
                    {
                        var (mapping, hasBacker) = this.MapStandaloneOperatorMapping(op, backerAlias, ns, speller, diagnostics);
                        mappings.Add(mapping);
                        if (!hasBacker)
                        {
                            break;
                        }

                        if (!operatorGroups.TryGetValue(op.Operator, out var group))
                        {
                            group = [];
                            operatorGroups[op.Operator] = group;
                        }

                        group.Add(op);
                        break;
                    }
                    case ObjectMethodSymbol method:
                    {
                        var (mapping, hasBacker) = this.MapStandaloneFunctionMapping(method, backerAlias, ns, speller, diagnostics);
                        mappings.Add(mapping);
                        if (hasBacker)
                        {
                            backerMethods.Add(this.MapBackerMethod(method, ns));
                        }

                        break;
                    }
                }
            }
        }

        private static ObjectDeclarationScope? FindObjectScope(ObjectDeclarationSymbol obj)
        {
            if (obj.ContainingScope == null)
            {
                return null;
            }

            foreach (var child in obj.ContainingScope.GetAllChildScopes())
            {
                if (child is ObjectDeclarationScope scope && ReferenceEquals(scope.DeclarationSymbol, obj))
                {
                    return scope;
                }
            }

            return null;
        }

        private static List<IBaseSymbol> MembersOf(ObjectDeclarationScope scope)
        {
            var members = new List<IBaseSymbol>();
            foreach (var symbol in ((IBaseScope)scope).GetAllChildSymbols())
            {
                if (symbol is ObjectMethodSymbol method && !IsOmittedFromPublicApi(method))
                {
                    members.Add(method);
                }
            }

            return members;
        }

        private (TyhpdefExtensionMember Mapping, bool HasPhpBacker) MapStandaloneFunctionMapping(
            ObjectMethodSymbol method,
            string backerAlias,
            string ns,
            TyhpdefExpressionSpeller speller,
            DiagnosticBag diagnostics)
        {
            var parameters = this.MapParameters(method.Parameters, ns, rewriteThisAlias: false, method.DeclaringAstNode);
            var args = string.Join(", ", parameters.Select(p => FormatCallArg(p.Name)));
            var (emittedParameters, byRefReceiver) = StripImplicitReceiver(parameters);
            var backerCall = backerAlias + "::" + method.Name + "(" + args + ")";
            var (body, hasBacker) = this.MappingBody(
                method.DeclaringAstNode,
                speller,
                backerCall,
                method.ContainingScope,
                method.Name,
                diagnostics);
            var attributes = this.MapDeclarationAttributes(method.DeclaringAstNode);
            this.StampGenericRuntime(
                attributes,
                this.BinderStamp(method, backerAlias + "::" + method.Name + GeneratedNames.GenericVariantSuffix));
            return (new TyhpdefExtensionMember
            {
                Kind = "fn",
                Name = method.Name,
                Parameters = emittedParameters,
                ReturnType = this.SpellRequired(method.ReturnType, ns),
                GenericParameters = this.MapGenerics(method.GenericParameters, ns),
                Body = body,
                DocComment = this.CopyDoc(method.DocComment),
                IsDeprecated = method.IsDeprecated,
                IsObsolete = method.IsObsolete,
                ByRefReceiver = byRefReceiver,
                Attributes = attributes,
            }, hasBacker);
        }

        private (TyhpdefExtensionMember Mapping, bool HasPhpBacker) MapStandaloneOperatorMapping(
            ObjectOperatorOverloadMethodSymbol op,
            string backerAlias,
            string ns,
            TyhpdefExpressionSpeller speller,
            DiagnosticBag diagnostics)
        {
            var compiledName = OperatorMethodNameGenerator.GetMethodName(op.Operator);
            string? returnTypeOverride = null;
            if (op.Operator == OverloadableOperator.Convert)
            {
                // Mirror PHP emit: convert-to is `E::__toInt($value)`, convert-from is
                // `E::__from($value)`. GetMethodName(Convert) is empty; do not fall back to
                // a nonexistent `convert(...)` backer.
                if (this.IsOwnedConvertTo(op, ns))
                {
                    compiledName = ConvertToCompiledMethodName(op, ns);
                }
                else
                {
                    compiledName = OperatorMethodNameGenerator.ConvertFromMethodName;
                    returnTypeOverride = "self";
                }
            }
            else if (string.IsNullOrEmpty(compiledName))
            {
                compiledName = op.Name;
            }

            var parameters = this.MapParameters(op.Parameters, ns, rewriteThisAlias: false, op.DeclaringAstNode);
            var args = string.Join(", ", parameters.Select(p => FormatCallArg(p.Name)));
            var backerCall = backerAlias + "::" + compiledName + "(" + args + ")";
            var (body, hasBacker) = this.MappingBody(
                op.DeclaringAstNode,
                speller,
                backerCall,
                op.ContainingScope,
                "operator " + OperatorToken(op),
                diagnostics);
            var attributes = this.MapDeclarationAttributes(op.DeclaringAstNode);
            this.StampGenericRuntime(
                attributes,
                this.BinderStamp(op, backerAlias + "::" + compiledName + GeneratedNames.GenericVariantSuffix));
            return (new TyhpdefExtensionMember
            {
                Kind = "operator",
                Name = OperatorToken(op),
                Parameters = parameters,
                ReturnType = returnTypeOverride ?? this.SpellRequired(op.ReturnType, ns),
                GenericParameters = this.MapGenerics(op.GenericParameters, ns),
                Body = body,
                DocComment = this.CopyDoc(op.DocComment),
                IsDeprecated = op.IsDeprecated,
                IsObsolete = op.IsObsolete,
                Attributes = attributes,
            }, hasBacker);
        }

        /// <summary>
        /// Picks the tyhpdef mapping body and whether a PHP backer stub accompanies it. The
        /// backer's existence is decided by <paramref name="declaringNode"/>'s form alone
        /// (<see cref="TyhpdefExtensionBody.HasPhpBacker"/>), matching Phase 5's emit exactly —
        /// a short <c>=&gt;</c> member never gets a PHP method, so falling back to
        /// <paramref name="backerCall"/> for one would reference a method that was never emitted.
        /// When a brace-bodied member's expression cannot be copied, its real backer call is a
        /// safe fallback (rows 2/3 always emit that method). When a short <c>=&gt;</c> member's
        /// expression cannot be copied, there is no PHP behind it to fall back to, so this is a
        /// generation error: the author must give it a single-<c>return</c> brace body instead.
        /// </summary>
        private (string Body, bool HasPhpBacker) MappingBody(
            IBase2Ast? declaringNode,
            TyhpdefExpressionSpeller speller,
            string backerCall,
            IBaseScope? fromScope,
            string memberName,
            DiagnosticBag diagnostics)
        {
            var form = TyhpdefExtensionBody.Classify(declaringNode);
            var hasBacker = TyhpdefExtensionBody.HasPhpBacker(form);
            if (TyhpdefExtensionBody.TryGetCopyableExpression(declaringNode, out var expression)
                && speller.TrySpell(expression, fromScope, out var copied))
            {
                return (copied, hasBacker);
            }

            if (hasBacker)
            {
                return (backerCall, true);
            }

            diagnostics.AddError(
                MessageCode.TyhpdefGenerationError,
                "package.tyhpdef",
                0,
                0,
                "extension member '" + memberName + "' has a `=>` body with no PHP backer, and its "
                    + "expression cannot be copied into the generated tyhpdef (it references a "
                    + "private or protected declaration, or another unspellable form); give it a "
                    + "single-`return` brace body to keep a callable backer.");
            return ("", false);
        }

        private TyhpdefMethod MapBackerMethod(ObjectMethodSymbol method, string ns)
        {
            var mapped = new TyhpdefMethod
            {
                Name = method.Name,
                Modifiers = ["public", "static"],
                Parameters = this.MapParameters(method.Parameters, ns, rewriteThisAlias: true, method.DeclaringAstNode),
                ReturnType = this.SpellRequired(method.ReturnType, ns),
                GenericParameters = this.BackerGenerics(method, ns),
                DocComment = this.CopyDoc(method.DocComment),
                IsDeprecated = method.IsDeprecated,
                IsObsolete = method.IsObsolete,
                IsAsync = method.IsAsync,
                Attributes = this.MapDeclarationAttributes(method.DeclaringAstNode),
            };
            this.StampGenericRuntime(
                mapped.Attributes,
                this.BinderStamp(method, method.Name + GeneratedNames.GenericVariantSuffix));
            return this.RewriteBackerSelfTypes(mapped, method, ns);
        }

        /// <summary>
        /// A backer class's <c>self</c> is the extension, not the block target. Signatures
        /// copied from the member still say <c>self</c>; spell that atom as the target so a
        /// later include of <c>package.tyhpdef</c> type-checks the operand as the target.
        /// </summary>
        private TyhpdefMethod RewriteBackerSelfTypes(TyhpdefMethod mapped, ObjectMethodSymbol method, string ns)
        {
            var target = this.ExtensionBlockTargetSpelling(method, ns);
            if (string.IsNullOrEmpty(target))
            {
                return mapped;
            }

            return mapped with
            {
                ReturnType = ReplaceSelfTypeAtom(mapped.ReturnType, target),
                Parameters = mapped.Parameters
                    .Select(parameter => parameter with { Type = ReplaceSelfTypeAtom(parameter.Type, target) })
                    .ToList(),
                GenericParameters = mapped.GenericParameters
                    .Select(parameter => parameter with
                    {
                        Constraint = parameter.Constraint is null
                            ? null
                            : ReplaceSelfTypeAtom(parameter.Constraint, target),
                    })
                    .ToList(),
            };
        }

        private string? ExtensionBlockTargetSpelling(ObjectMethodSymbol method, string ns)
        {
            string? spelled = null;
            if (method.ContainingScope is ObjectDeclarationScope scope
                && scope.DeclarationSymbol is ObjectDeclarationSymbol block
                && (block.IsExtension || block.IsExtensionTargetGroup))
            {
                spelled = this.Spell(block.PendingExtensionBlockTarget, ns);
            }

            if (string.IsNullOrEmpty(spelled) && method is ObjectOperatorOverloadMethodSymbol op)
            {
                spelled = this.Spell(op.PendingExtensionTargetType, ns);
                if (string.IsNullOrEmpty(spelled))
                {
                    spelled = SpellTargetSymbol(op.ExtensionTargetSymbol);
                }
            }

            if (string.IsNullOrEmpty(spelled)
                || string.Equals(spelled, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(spelled, "static", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return spelled;
        }

        private static string ReplaceSelfTypeAtom(string? spelled, string target)
        {
            if (string.IsNullOrEmpty(spelled))
            {
                return spelled ?? "";
            }

            return SelfTypeAtom.Replace(spelled, target);
        }

        private TyhpdefMethod MapBackerOperatorMethod(
            string compiledName,
            List<ObjectOperatorOverloadMethodSymbol> group,
            string ns)
        {
            var primary = group[0];
            var mapped = this.MapBackerMethod(primary, ns);
            mapped = mapped with { Name = compiledName };
            if (group.Count == 1)
            {
                return mapped;
            }

            // Prefer overloads when related forms cannot share a single generic T.
            var overloads = group.Skip(1).Select(op => this.MapBackerMethod(op, ns) with { Name = compiledName }).ToList();
            return mapped with { Overloads = overloads };
        }

        /// <summary>
        /// Standalone <c>extension E { operator convert }</c> PHP backers match
        /// <c>EmitConvertGroup(isExtension: true)</c>: one static <c>__to{T}</c> per convert-to
        /// target (operand is the extension's <c>self</c>), plus one static <c>__from</c> whose
        /// parameter unions every convert-from source and whose return is the target type.
        /// A multi-target standalone extension (nested <c>extends&lt;T&gt;</c> groups) can have
        /// two different targets compile a to-form or a from-form to the *same* PHP method name
        /// (<see cref="ConvertToCompiledMethodName"/> keys off the return type, and <c>__from</c>
        /// is always shared) — group by method name first so a same-named form from a second
        /// target unions into the signature instead of being dropped.
        /// </summary>
        private void AddStandaloneConvertBackerMethods(
            List<ObjectOperatorOverloadMethodSymbol> group,
            string ns,
            List<TyhpdefMethod> backerMethods)
        {
            var toForms = new Dictionary<string, List<ObjectOperatorOverloadMethodSymbol>>(StringComparer.Ordinal);
            List<ObjectOperatorOverloadMethodSymbol>? fromForms = null;
            foreach (var op in group)
            {
                if (this.IsOwnedConvertTo(op, ns))
                {
                    var methodName = ConvertToCompiledMethodName(op, ns);
                    if (!toForms.TryGetValue(methodName, out var forms))
                    {
                        forms = [];
                        toForms[methodName] = forms;
                    }

                    forms.Add(op);
                    continue;
                }

                fromForms ??= [];
                fromForms.Add(op);
            }

            foreach (var (methodName, forms) in toForms)
            {
                backerMethods.Add(this.MapStandaloneConvertToBackerMethod(forms, methodName, ns));
            }

            if (fromForms is { Count: > 0 })
            {
                backerMethods.Add(this.MapStandaloneConvertFromBackerMethod(fromForms, ns));
            }
        }

        /// <summary>
        /// Builds one <c>__to{T}</c> backer method for every to-form compiling to
        /// <paramref name="methodName"/>. A single form just reuses <see cref="MapBackerMethod"/>
        /// (already self-rewritten to its own target). Multiple forms — two different nested
        /// <c>extends</c> targets returning the same type — union their (already self-rewritten,
        /// per-target) operand types and merge their generic parameters, mirroring how
        /// <c>EmitConvertTo</c> unions the real PHP operand type and branches by guard.
        /// </summary>
        private TyhpdefMethod MapStandaloneConvertToBackerMethod(
            List<ObjectOperatorOverloadMethodSymbol> forms,
            string methodName,
            string ns)
        {
            var mapped = this.MapBackerMethod(forms[0], ns) with { Name = methodName };
            if (forms.Count == 1 || mapped.Parameters.Count == 0)
            {
                return mapped;
            }

            var operandType = UnionSpelledTypes(forms.Select(form =>
            {
                var parameters = this.MapBackerMethod(form, ns).Parameters;
                return parameters.Count > 0 ? parameters[0].Type : null;
            }));

            return mapped with
            {
                Parameters = [mapped.Parameters[0] with { Type = operandType }],
                GenericParameters = this.MergeBackerGenerics(forms, ns),
            };
        }

        /// <summary>
        /// Builds the single shared <c>__from</c> backer method. Its return type must union
        /// every distinct target its forms construct (mirroring <c>EmitConvertFrom</c>'s
        /// <c>Distinct()</c> union across nested targets) rather than only the first form's —
        /// a second target silently vanishing from the declared return type is unsound. Its
        /// generic parameters must likewise carry every contributing target group's own type
        /// parameters (e.g. a nested <c>extends&lt;T&gt;</c> group's <c>T</c>), or the spelled
        /// return/parameter types reference a type variable the method never declares.
        /// </summary>
        private TyhpdefMethod MapStandaloneConvertFromBackerMethod(
            List<ObjectOperatorOverloadMethodSymbol> fromForms,
            string ns)
        {
            var mapped = this.MapConvertFromCompiledMethod(fromForms, ns);
            var targets = new List<string>();
            foreach (var form in fromForms)
            {
                var target = this.ExtensionBlockTargetSpelling(form, ns);
                if (!string.IsNullOrEmpty(target) && !targets.Contains(target, StringComparer.Ordinal))
                {
                    targets.Add(target);
                }
            }

            return mapped with
            {
                ReturnType = targets.Count > 0 ? string.Join("|", targets) : mapped.ReturnType,
                GenericParameters = this.MergeBackerGenerics(fromForms, ns),
            };
        }

        /// <summary>
        /// Unions every contributing form's own block/method generic parameters
        /// (<see cref="BackerGenerics"/>) into one list, deduplicated by name. Used when several
        /// convert forms from different nested <c>extends&lt;T&gt;</c> targets share one backer
        /// method, so every target's type parameters that the merged signature's spelled types
        /// reference stay declared on that method. Each form's own constraint is self-rewritten
        /// against *that form's own* target (<see cref="RewriteBackerSelfTypes"/> only fixes up
        /// the single form <see cref="MapBackerMethod"/> maps directly — a constraint pulled in
        /// here from a second contributing form still says <c>self</c>/<c>static</c> unless this
        /// does the same rewrite per-form, since two forms can belong to two different targets).
        /// Sibling nested groups are free to name their own type parameter the same letter (the
        /// binder scopes each group's generic to its own subtree, so nothing stops two groups
        /// both writing <c>extends&lt;U extends self&gt;</c>) — when that happens here, the
        /// second form's rewritten bound must not be dropped in favor of the first form's: the
        /// merged declaration unions both targets into the constraint, the same way a merged
        /// operand/return type already unions every contributing target's type.
        /// </summary>
        private List<TyhpdefGenericParameter> MergeBackerGenerics(
            IEnumerable<ObjectMethodSymbol> forms,
            string ns)
        {
            var generics = new List<TyhpdefGenericParameter>();
            foreach (var form in forms)
            {
                var target = this.ExtensionBlockTargetSpelling(form, ns);
                foreach (var generic in this.BackerGenerics(form, ns))
                {
                    var rewritten = target is null || generic.Constraint is null
                        ? generic
                        : generic with { Constraint = ReplaceSelfTypeAtom(generic.Constraint, target) };

                    var existingIndex = generics.FindIndex(
                        existing => string.Equals(existing.Name, rewritten.Name, StringComparison.Ordinal));
                    if (existingIndex < 0)
                    {
                        generics.Add(rewritten);
                        continue;
                    }

                    var merged = MergeGenericConstraint(generics[existingIndex].Constraint, rewritten.Constraint);
                    if (!string.Equals(generics[existingIndex].Constraint, merged, StringComparison.Ordinal))
                    {
                        generics[existingIndex] = generics[existingIndex] with { Constraint = merged };
                    }
                }
            }

            return generics;
        }

        /// <summary>
        /// Merges two sibling forms' bounds for the *same-named* generic parameter. Either side
        /// unconstrained means the merged parameter must stay unconstrained (some contributing
        /// form already accepts anything under that name, so re-adding a bound would reject
        /// values that form's own real signature allows). Otherwise union the spelled bounds —
        /// same as any other merged operand/return type — so a value satisfying *either*
        /// contributing target's bound still type-checks against the shared declaration.
        /// </summary>
        private static string? MergeGenericConstraint(string? existing, string? incoming)
        {
            if (existing is null || incoming is null)
            {
                return null;
            }

            if (string.Equals(existing, incoming, StringComparison.Ordinal))
            {
                return existing;
            }

            return UnionSpelledTypes([existing, incoming]);
        }

        private TyhpdefClassDeclaration MapType(ObjectDeclarationSymbol obj, string ns)
        {
            var kind = obj.IsStruct
                ? "struct"
                : obj.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => "interface",
                    PhpTypeDeclType.Trait => "trait",
                    PhpTypeDeclType.Enum => "enum",
                    _ => "class",
                };

            var methods = new List<TyhpdefMethod>();
            var properties = new List<TyhpdefProperty>();
            var constants = new List<TyhpdefConstant>();
            var enumCases = new List<TyhpdefEnumCase>();
            var extensionMembers = new List<TyhpdefExtensionMember>();
            var typeAliases = new List<TyhpdefTypeAlias>();

            var compiledOperatorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ObjectOperatorOverloadMethodSymbol>? convertFromForms = null;

            foreach (var member in EnumeratePublicMembers(obj))
            {
                switch (member)
                {
                    case ObjectOperatorOverloadMethodSymbol op when !op.IsExtensionOperator && !op.IsNativePassthrough:
                        // PHP emit collapses every form of an operator into one static method,
                        // except convert: to-forms are instance `__to{T}()` and from-forms share
                        // one static `__from`. Include-layer occupancy is name-only (8002), so
                        // Emit each of those backers once. Each form still gets its
                        // own extension mapping.
                        if (op.Operator == OverloadableOperator.Convert)
                        {
                            this.MapOwnedConvertOperator(
                                op,
                                obj,
                                ns,
                                methods,
                                extensionMembers,
                                compiledOperatorNames,
                                ref convertFromForms);
                            break;
                        }

                        if (compiledOperatorNames.Add(CompiledOperatorMethodName(op)))
                        {
                            methods.Add(this.MapCompiledOperatorMethod(op, ns));
                        }

                        extensionMembers.Add(this.MapOwnedInlineOperator(op, ns));
                        break;
                    case ObjectOperatorOverloadMethodSymbol:
                        break;
                    case ObjectMethodSymbol method:
                        methods.Add(this.MapMethod(method, ns));
                        break;
                    case ObjectPropertySymbol prop:
                        properties.Add(this.MapProperty(prop, ns, kind == "struct"));
                        break;
                    case ObjectTypeAliasSymbol objectAlias:
                        typeAliases.Add(this.MapObjectTypeAlias(objectAlias, obj, ns));
                        break;
                    case ObjectConstantSymbol constant when constant.IsEnumCase:
                        enumCases.Add(new TyhpdefEnumCase
                        {
                            Name = constant.Name,
                            BackingValue = this.SpellExpr(constant.ValueExpression, ns),
                            DocComment = this.CopyDoc(constant.DocComment),
                            Attributes = this.MapDeclarationAttributes(constant.DeclaringAstNode),
                        });
                        break;
                    case ObjectConstantSymbol constant:
                        constants.Add(this.MapObjectConstant(constant, ns));
                        break;
                }
            }

            var phpName = DeclaredTypeName(obj.Name);
            var attributes = this.MapDeclarationAttributes(obj.DeclaringAstNode);
            this.StampGenericRuntime(attributes, this.FactoryStamp(obj));
            return new TyhpdefClassDeclaration
            {
                Kind = kind,
                Name = phpName,
                Modifiers = FormatTypeModifiers(obj.Visibility),
                Extends = this.Spell(obj.ExtendsType, ns),
                BackingType = kind == "enum" ? this.Spell(obj.BackingType, ns) : null,
                Implements = obj.ImplementsTypes.Select(t => this.SpellRequired(t, ns)).Where(s => s.Length > 0).ToList(),
                GenericParameters = this.MapGenerics(obj.GenericParameters, ns),
                Methods = methods,
                Properties = properties,
                Constants = constants,
                TypeAliases = typeAliases,
                EnumCases = enumCases,
                ExtensionMembers = extensionMembers,
                DocComment = this.CopyDoc(obj.DocComment),
                IsDeprecated = obj.IsDeprecated,
                IsObsolete = obj.IsObsolete,
                Attributes = attributes,
            };
        }

        private void MapOwnedConvertOperator(
            ObjectOperatorOverloadMethodSymbol op,
            ObjectDeclarationSymbol obj,
            string ns,
            List<TyhpdefMethod> methods,
            List<TyhpdefExtensionMember> extensionMembers,
            HashSet<string> compiledOperatorNames,
            ref List<ObjectOperatorOverloadMethodSymbol>? convertFromForms)
        {
            if (this.IsOwnedConvertTo(op, ns))
            {
                var methodName = ConvertToCompiledMethodName(op, ns);
                if (compiledOperatorNames.Add(methodName))
                {
                    methods.Add(this.MapConvertToCompiledMethod(op, ns, methodName));
                }

                extensionMembers.Add(this.MapOwnedConvertToOperator(op, ns, methodName));
                return;
            }

            convertFromForms ??= this.CollectOwnedConvertFromForms(obj, ns);
            if (convertFromForms.Count > 0
                && compiledOperatorNames.Add(OperatorMethodNameGenerator.ConvertFromMethodName))
            {
                methods.Add(this.MapConvertFromCompiledMethod(convertFromForms, ns));
            }

            // Convert-from always constructs `self` (mirrors `MapConvertFromCompiledMethod`'s
            // hardcoded backer return) — never the source's omitted/spelled return type.
            extensionMembers.Add(this.MapOwnedInlineOperator(
                op,
                ns,
                OperatorMethodNameGenerator.ConvertFromMethodName,
                returnTypeOverride: "self"));
        }

        private TyhpdefMethod MapConvertToCompiledMethod(
            ObjectOperatorOverloadMethodSymbol op,
            string ns,
            string methodName)
        {
            var mapped = this.MapMethod(op, ns);
            var modifiers = mapped.Modifiers
                .Where(m => !string.Equals(m, "static", StringComparison.OrdinalIgnoreCase))
                .ToList();
            EnsurePublicVisibility(modifiers);

            // convert-to is an instance method with no parameters (`$this->__toInt()`).
            return mapped with
            {
                Name = methodName,
                Modifiers = modifiers,
                Parameters = [],
            };
        }

        private TyhpdefMethod MapConvertFromCompiledMethod(
            IReadOnlyList<ObjectOperatorOverloadMethodSymbol> fromForms,
            string ns)
        {
            var mapped = this.MapMethod(fromForms[0], ns);
            var modifiers = mapped.Modifiers.ToList();
            if (!modifiers.Contains("static", StringComparer.OrdinalIgnoreCase))
            {
                modifiers.Add("static");
            }

            EnsurePublicVisibility(modifiers);

            return mapped with
            {
                Name = OperatorMethodNameGenerator.ConvertFromMethodName,
                Modifiers = modifiers,
                Parameters =
                [
                    new TyhpdefParameter
                    {
                        Name = "$from",
                        Type = this.BuildConvertFromParameterUnion(fromForms, ns),
                    },
                ],
                ReturnType = "self",
            };
        }

        private TyhpdefExtensionMember MapOwnedConvertToOperator(
            ObjectOperatorOverloadMethodSymbol op,
            string ns,
            string methodName)
        {
            var parameters = this.MapParameters(op.Parameters, ns, rewriteThisAlias: false, op.DeclaringAstNode);
            var receiver = parameters.Count > 0 ? FormatCallArg(parameters[0].Name) : "$this";
            var attributes = this.MapDeclarationAttributes(op.DeclaringAstNode);
            this.StampGenericRuntime(
                attributes,
                this.BinderStamp(op, methodName + GeneratedNames.GenericVariantSuffix));
            return new TyhpdefExtensionMember
            {
                Kind = "operator",
                Name = OperatorToken(op),
                Parameters = parameters,
                ReturnType = this.SpellRequired(op.ReturnType, ns),
                GenericParameters = this.MapGenerics(op.GenericParameters, ns),
                Body = receiver + "->" + methodName + "()",
                DocComment = this.CopyDoc(op.DocComment),
                IsDeprecated = op.IsDeprecated,
                IsObsolete = op.IsObsolete,
                Attributes = attributes,
            };
        }

        private List<ObjectOperatorOverloadMethodSymbol> CollectOwnedConvertFromForms(
            ObjectDeclarationSymbol obj,
            string ns)
        {
            var fromForms = new List<ObjectOperatorOverloadMethodSymbol>();
            foreach (var member in EnumeratePublicMembers(obj))
            {
                if (member is ObjectOperatorOverloadMethodSymbol op
                    && !op.IsExtensionOperator
                    && !op.IsNativePassthrough
                    && op.Operator == OverloadableOperator.Convert
                    && !this.IsOwnedConvertTo(op, ns))
                {
                    fromForms.Add(op);
                }
            }

            return fromForms;
        }

        private bool IsOwnedConvertTo(ObjectOperatorOverloadMethodSymbol op, string ns)
        {
            if (op.Parameters.Count == 0)
            {
                return false;
            }

            var spelled = this.Spell(op.Parameters[0].DeclaredType, ns) ?? "";
            return string.Equals(spelled, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(spelled, "static", StringComparison.OrdinalIgnoreCase);
        }

        private string ConvertToCompiledMethodName(ObjectOperatorOverloadMethodSymbol op, string ns)
            => OperatorMethodNameGenerator.GetConvertToMethodName(this.Spell(op.ReturnType, ns));

        private string BuildConvertFromParameterUnion(
            IReadOnlyList<ObjectOperatorOverloadMethodSymbol> fromForms,
            string ns)
        {
            return UnionSpelledTypes(fromForms.Select(form =>
                form.Parameters.Count > 0 ? this.Spell(form.Parameters[0].DeclaredType, ns) : null));
        }

        /// <summary>
        /// Unions already-spelled type strings the way tyhpdef parameter/return unions are
        /// built elsewhere in this file: split each on <c>|</c>, drop <c>null</c> (nullability
        /// is not tracked here) and blank/<c>mixed</c> atoms fold the whole union to
        /// <c>mixed</c>, dedup case-insensitively, preserve first-seen order.
        /// </summary>
        private static string UnionSpelledTypes(IEnumerable<string?> spelledTypes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var atoms = new List<string>();
            var hasMixed = false;

            foreach (var type in spelledTypes)
            {
                if (string.IsNullOrEmpty(type))
                {
                    hasMixed = true;
                    continue;
                }

                foreach (var rawPart in type.Split('|'))
                {
                    var part = rawPart.Trim().TrimStart('?');
                    if (part.Length == 0)
                    {
                        continue;
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

            return string.Join("|", atoms);
        }

        private static void EnsurePublicVisibility(List<string> modifiers)
        {
            if (!modifiers.Contains("public", StringComparer.OrdinalIgnoreCase)
                && !modifiers.Contains("protected", StringComparer.OrdinalIgnoreCase))
            {
                modifiers.Insert(0, "public");
            }
        }

        private TyhpdefMethod MapCompiledOperatorMethod(ObjectOperatorOverloadMethodSymbol op, string ns)
        {
            var compiledName = CompiledOperatorMethodName(op);
            var mapped = this.MapMethod(op, ns);
            var modifiers = mapped.Modifiers.ToList();
            if (!modifiers.Contains("static", StringComparer.OrdinalIgnoreCase))
            {
                modifiers.Add("static");
            }

            EnsurePublicVisibility(modifiers);

            return mapped with { Name = compiledName, Modifiers = modifiers };
        }

        private TyhpdefExtensionMember MapOwnedInlineOperator(
            ObjectOperatorOverloadMethodSymbol op,
            string ns,
            string? compiledName = null,
            string? returnTypeOverride = null)
        {
            compiledName ??= CompiledOperatorMethodName(op);

            var parameters = this.MapParameters(op.Parameters, ns, rewriteThisAlias: false, op.DeclaringAstNode);
            var args = string.Join(", ", parameters.Select(p => FormatCallArg(p.Name)));
            // Owned-type operators stay as real PHP methods (`Money::__add`). The tyhpdef
            // mapping is a thin `=>` splice onto that method. Do not emit
            // `#[\Tyhp\Optimize\Inline]` — that attribute on an extension member is 4176.
            var attributes = this.MapDeclarationAttributes(op.DeclaringAstNode);
            this.StampGenericRuntime(
                attributes,
                this.BinderStamp(op, compiledName + GeneratedNames.GenericVariantSuffix));
            return new TyhpdefExtensionMember
            {
                Kind = "operator",
                Name = OperatorToken(op),
                Parameters = parameters,
                // `returnTypeOverride` lets convert-from pass the backer's true "self" contract
                // instead of the source's spelled return type, which convert-from forms always
                // omit (`operator convert(int $value) { ... }` has no `: Type`, so
                // `op.ReturnType` is null and `SpellRequired` would fall back to "mixed" —
                // unsound versus the real `__from(...): self` backer).
                ReturnType = returnTypeOverride ?? this.SpellRequired(op.ReturnType, ns),
                GenericParameters = this.MapGenerics(op.GenericParameters, ns),
                Body = "self::" + compiledName + "(" + args + ")",
                DocComment = this.CopyDoc(op.DocComment),
                IsDeprecated = op.IsDeprecated,
                IsObsolete = op.IsObsolete,
                Attributes = attributes,
            };
        }

        private TyhpdefMethod MapMethod(ObjectMethodSymbol method, string ns, bool harvestAnonymousReturns = true)
        {
            var mapped = new TyhpdefMethod
            {
                Name = method.Name,
                Modifiers = FormatMemberModifiers(method.Visibility, method.IsStatic, method.IsAbstract, method.IsAsync),
                Parameters = this.MapParameters(method.Parameters, ns, rewriteThisAlias: false, method.DeclaringAstNode),
                ReturnType = harvestAnonymousReturns
                    ? this.SpellReturnWithAnonymousShapes(
                        method.ReturnType,
                        method.DeclaringAstNode,
                        OwnerTypeName(method),
                        method.Name,
                        ns)
                    : this.Spell(method.ReturnType, ns) ?? "",
                GenericParameters = this.MapGenerics(method.GenericParameters, ns),
                DocComment = this.CopyDoc(method.DocComment),
                IsDeprecated = method.IsDeprecated,
                IsObsolete = method.IsObsolete,
                IsAsync = method.IsAsync,
                Attributes = this.MapDeclarationAttributes(method.DeclaringAstNode),
            };
            this.StampGenericRuntime(
                mapped.Attributes,
                this.BinderStamp(method, method.Name + GeneratedNames.GenericVariantSuffix));
            return mapped;
        }

        private TyhpdefFunction MapFunction(FunctionDeclarationSymbol function, string ns)
        {
            var mapped = new TyhpdefFunction
            {
                Name = function.Name,
                Parameters = this.MapParameters(function.Parameters, ns, rewriteThisAlias: false, function.DeclaringAstNode),
                ReturnType = this.SpellReturnWithAnonymousShapes(
                    function.ReturnType,
                    function.DeclaringAstNode,
                    ownerTypeName: null,
                    function.Name,
                    ns),
                GenericParameters = this.MapGenerics(function.GenericParameters, ns),
                DocComment = this.CopyDoc(function.DocComment),
                IsDeprecated = function.IsDeprecated,
                IsObsolete = function.IsObsolete,
                IsAsync = function.IsAsync,
                Overloads = function.Overloads.Select(o => (TyhpdefMethod)this.MapFunction(o, ns)).ToList(),
                Attributes = this.MapDeclarationAttributes(function.DeclaringAstNode),
            };
            this.StampGenericRuntime(
                mapped.Attributes,
                this.BinderStamp(function, function.Name + GeneratedNames.GenericVariantSuffix));
            return mapped;
        }

        private TyhpdefProperty MapProperty(ObjectPropertySymbol prop, string ns, bool isStruct)
        {
            return new TyhpdefProperty
            {
                Name = prop.Name.StartsWith('$') ? prop.Name : "$" + prop.Name,
                Type = this.Spell(prop.DeclaredType, ns) ?? "",
                Modifiers = isStruct ? [] : FormatMemberModifiers(prop.Visibility, isStatic: false, isAbstract: false, isAsync: false),
                CoalesceValue = isStruct ? null : this.SpellExpr(prop.DefaultValue, ns),
                DocComment = this.CopyDoc(prop.DocComment),
                IsDeprecated = prop.IsDeprecated,
                Attributes = this.MapDeclarationAttributes(this.PropertyAttributeHost(prop)),
                // Structs have no hook grammar. Class/interface/trait hooks copy shape only
                // (which hooks, &get, modifiers, attributes) — never bodies, never a
                // synthesized #[\Tyhp\Php(">=8.4")] gate (the 8.2–8.3 polyfill is the contract).
                Hooks = isStruct ? [] : this.MapPropertyHooks(prop),
            };
        }

        /// <summary>
        /// Bodyless hook list from bound <see cref="ObjectPropertySymbol"/> flags plus the
        /// declaring hook AST for visibility / <c>final</c> / attributes.
        /// </summary>
        private List<TyhpdefPropertyHook> MapPropertyHooks(ObjectPropertySymbol prop)
        {
            if (!prop.HasGetHook && !prop.HasSetHook)
            {
                return [];
            }

            var declared = TryGetDeclaredPropertyHooks(prop);
            var result = new List<TyhpdefPropertyHook>();
            if (prop.HasGetHook)
            {
                result.Add(this.MapPropertyHook(declared, "get", prop.GetHookReturnsRef));
            }

            if (prop.HasSetHook)
            {
                result.Add(this.MapPropertyHook(declared, "set", returnsRef: false));
            }

            return result;
        }

        private TyhpdefPropertyHook MapPropertyHook(
            PhpPropertyHookListAst? declared,
            string name,
            bool returnsRef)
        {
            var ast = FindDeclaredHook(declared, name);
            return new TyhpdefPropertyHook
            {
                Name = name,
                ReturnsRef = returnsRef,
                Modifiers = MapHookModifiers(ast?.Modifiers),
                Attributes = this.MapDeclarationAttributes(ast, skipPhpGate: true),
            };
        }

        private static PhpPropertyHookListAst? TryGetDeclaredPropertyHooks(ObjectPropertySymbol prop)
            => prop.DeclaringAstNode switch
            {
                PhpPropertyAst property => property.Hooks,
                PhpParameterAst { PropertyHooks: PhpPropertyHookListAst hooks } => hooks,
                _ => null,
            };

        private static PhpPropertyHookAst? FindDeclaredHook(PhpPropertyHookListAst? hooks, string name)
        {
            foreach (var hook in hooks?.GetAllNotNull() ?? [])
            {
                if (string.Equals(hook.Identifier?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return hook;
                }
            }

            return null;
        }

        private IBase2Ast? PropertyAttributeHost(ObjectPropertySymbol prop)
        {
            if (prop.DeclaringAstNode is PhpPropertyAst property
                && prop.ContainingScope?.DeclarationSymbol is ObjectDeclarationSymbol owner
                && owner.DeclaringAstNode is PhpObjectTypeDeclAst { Body: { } body })
            {
                foreach (var member in body.GetAllNotNull())
                {
                    if (member is PhpPropertyDeclAst decl
                        && decl.Properties?.GetAllNotNull().Contains(property) == true)
                    {
                        return decl;
                    }
                }
            }

            return prop.DeclaringAstNode;
        }

        /// <summary>
        /// Hook visibility / <c>final</c> only. Never emits an explicit <c>public</c>
        /// hook modifier, matching PHP-source harvest, so <c>{ get; }</c> stays compact.
        /// </summary>
        private static List<string> MapHookModifiers(PhpModifierListAst? list)
        {
            var modifiers = list?.Modifiers.ToList() ?? [];
            var result = new List<string>();
            if (modifiers.Contains(PhpModifier.Final))
            {
                result.Add("final");
            }

            if (modifiers.Contains(PhpModifier.Private))
            {
                result.Add("private");
            }
            else if (modifiers.Contains(PhpModifier.Protected))
            {
                result.Add("protected");
            }

            return result;
        }

        private List<TyhpdefAttribute> MapDeclarationAttributes(IBase2Ast? node, bool skipPhpGate = false)
        {
            var result = new List<TyhpdefAttribute>();
            if (node is null)
            {
                return result;
            }

            foreach (var attrNode in node.AstAttributes)
            {
                if (attrNode is not PhpAttributeAst attribute
                    || GenericRuntimeAttributeSupport.IsGenericRuntimeAttribute(attribute)
                    || EraseGenericAttributeSupport.IsEraseGenericAttribute(attribute)
                    || (skipPhpGate && IsTyhpPhpGateAttribute(attribute)))
                {
                    continue;
                }

                var name = SpellAttributeName(attribute);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new TyhpdefAttribute
                {
                    Name = name,
                    Arguments = this.SpellAttributeArguments(attribute),
                });
            }

            return result;
        }

        private static bool IsTyhpPhpGateAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: { } symbol }
                && IsTyhpPhpFullyQualifiedName(symbol.FullyQualifiedName))
            {
                return true;
            }

            var written = attribute.Name switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                { } expr => expr.Identifier,
                _ => null,
            };
            return IsTyhpPhpFullyQualifiedName(written);
        }

        private static bool IsTyhpPhpFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim().TrimStart('\\');
            return trimmed.Equals(@"Tyhp\Php", StringComparison.OrdinalIgnoreCase);
        }

        private static string SpellAttributeName(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: { } symbol }
                && !string.IsNullOrWhiteSpace(symbol.FullyQualifiedName))
            {
                var fqn = symbol.FullyQualifiedName.Trim();
                return fqn.StartsWith('\\') ? fqn : "\\" + fqn;
            }

            var written = attribute.Name switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                { } expr => expr.Identifier,
                _ => null,
            };
            return (written ?? "").Trim();
        }

        private List<string> SpellAttributeArguments(PhpAttributeAst attribute)
        {
            var result = new List<string>();
            foreach (var arg in attribute.Arguments?.GetAllNotNull() ?? [])
            {
                var spelled = this.SpellExpr(arg.Expression, ns: "");
                if (string.IsNullOrWhiteSpace(spelled))
                {
                    continue;
                }

                var name = arg.Name?.ValueString;
                result.Add(string.IsNullOrEmpty(name) ? spelled : name + ": " + spelled);
            }

            return result;
        }

        private TyhpdefConstant MapObjectConstant(ObjectConstantSymbol constant, string ns)
        {
            return new TyhpdefConstant
            {
                Name = constant.Name,
                Type = this.Spell(constant.DeclaredType, ns) ?? "",
                Value = this.SpellExpr(constant.ValueExpression, ns),
                Modifiers = FormatMemberModifiers(constant.Visibility, isStatic: false, isAbstract: false, isAsync: false),
                DocComment = this.CopyDoc(constant.DocComment),
                IsDeprecated = constant.IsDeprecated,
                Attributes = this.MapDeclarationAttributes(constant.DeclaringAstNode),
            };
        }

        private TyhpdefConstant MapConstant(ConstantSymbol constant, string ns)
        {
            return new TyhpdefConstant
            {
                Name = constant.Name,
                Type = this.Spell(constant.DeclaredType, ns) ?? "",
                Value = this.SpellExpr(constant.ValueExpression, ns),
                DocComment = this.CopyDoc(constant.DocComment),
                IsDeprecated = constant.IsDeprecated,
                Attributes = this.MapDeclarationAttributes(constant.DeclaringAstNode),
            };
        }

        private TyhpdefTypeAlias MapTypeAlias(TypeAliasSymbol alias, string ns)
        {
            var (aliasedType, intersections, shape) = this.MapAliasRhs(alias.AliasedType, ns);
            var mapped = new TyhpdefTypeAlias
            {
                Name = alias.Name,
                AliasedType = aliasedType,
                IntersectionTypes = intersections,
                ObjectShape = shape,
                GenericParameters = this.MapGenerics(alias.GenericParameters, ns),
                DocComment = this.CopyDoc(alias.DocComment),
                Attributes = this.MapDeclarationAttributes(alias.DeclaringAstNode),
            };
            this.StampGenericRuntime(mapped.Attributes, this.AliasFactoryStamp(alias, alias.Name));
            return mapped;
        }

        private TyhpdefTypeAlias MapObjectTypeAlias(
            ObjectTypeAliasSymbol alias,
            ObjectDeclarationSymbol owner,
            string ns)
        {
            var (aliasedType, intersections, shape) = this.MapAliasRhs(alias.AliasedType, ns);
            var mapped = new TyhpdefTypeAlias
            {
                Name = alias.Name,
                AliasedType = aliasedType,
                IntersectionTypes = intersections,
                ObjectShape = shape,
                GenericParameters = this.MapGenerics(alias.GenericParameters, ns),
                Modifiers = FormatMemberModifiers(alias.Visibility, isStatic: false, isAbstract: false, isAsync: false),
                DocComment = this.CopyDoc(alias.DocComment),
                Attributes = this.MapDeclarationAttributes(alias.DeclaringAstNode),
            };
            this.StampGenericRuntime(
                mapped.Attributes,
                this.AliasFactoryStamp(alias, owner.Name + "::" + alias.Name));
            return mapped;
        }

        private List<TyhpdefParameter> MapParameters(
            IEnumerable<ParameterInfo> parameters,
            string ns,
            bool rewriteThisAlias,
            IBase2Ast? declaringNode = null)
        {
            var result = new List<TyhpdefParameter>();
            foreach (var param in parameters)
            {
                var name = param.Name ?? "";
                if (rewriteThisAlias && string.Equals(name.TrimStart('$'), "this", StringComparison.OrdinalIgnoreCase))
                {
                    name = GeneratedNames.ExtensionReceiverThisAlias;
                }

                result.Add(new TyhpdefParameter
                {
                    Name = name,
                    Type = this.Spell(param.DeclaredType, ns) ?? "",
                    DefaultValue = this.SpellExpr(param.DefaultValue, ns),
                    IsVariadic = param.IsVariadic,
                    IsByReference = param.IsByReference,
                    Attributes = this.MapDeclarationAttributes(FindParameterAst(declaringNode, param.Name)),
                });
            }

            return result;
        }

        private List<TyhpdefGenericParameter> BackerGenerics(ObjectMethodSymbol method, string ns)
        {
            var generics = new List<TyhpdefGenericParameter>();
            if (method.ContainingScope is ObjectDeclarationScope scope
                && scope.DeclarationSymbol is ObjectDeclarationSymbol block
                && (block.IsExtension || block.IsExtensionTargetGroup))
            {
                generics.AddRange(this.MapGenerics(block.GenericParameters, ns));
            }

            foreach (var own in this.MapGenerics(method.GenericParameters, ns))
            {
                if (!generics.Any(existing => string.Equals(existing.Name, own.Name, StringComparison.Ordinal)))
                {
                    generics.Add(own);
                }
            }

            return generics;
        }

        private static (List<TyhpdefParameter> Parameters, bool ByRefReceiver) StripImplicitReceiver(
            List<TyhpdefParameter> parameters)
        {
            if (parameters.Count == 0 || !IsReceiverName(parameters[0].Name))
            {
                return (parameters, false);
            }

            return (parameters.Skip(1).ToList(), parameters[0].IsByReference);
        }

        private static bool IsReceiverName(string? name)
        {
            var trimmed = (name ?? "").Trim().TrimStart('$');
            return string.Equals(trimmed, "this", StringComparison.OrdinalIgnoreCase);
        }

        private List<TyhpdefGenericParameter> MapGenerics(
            IEnumerable<GenericTypeParameterSymbol>? parameters,
            string ns)
        {
            var result = new List<TyhpdefGenericParameter>();
            foreach (var param in parameters ?? [])
            {
                result.Add(new TyhpdefGenericParameter
                {
                    Name = param.Name,
                    Constraint = this.Spell(param.Constraint, ns),
                });
            }

            return result;
        }

        private string? Spell(ITypeExpression? type, string ns)
        {
            _ = ns;
            return TyhpdefTypeSpeller.Spell(type);
        }

        private string SpellRequired(ITypeExpression? type, string ns)
            => this.Spell(type, ns) ?? "mixed";

        private HashSet<string> ShapeUsedNames(string ns)
        {
            if (!this._shapeUsedNames.TryGetValue(ns, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                this._shapeUsedNames[ns] = set;
            }

            return set;
        }

        private string SpellReturnWithAnonymousShapes(
            ITypeExpression? declared,
            IBase2Ast? declaringNode,
            string? ownerTypeName,
            string callableName,
            string ns)
        {
            var spelled = this.Spell(declared, ns) ?? "";
            var body = declaringNode switch
            {
                PhpFunctionDeclAst function => function.Body,
                PhpMethodDeclAst method => method.Body,
                _ => declaringNode,
            };
            var updated = AnonymousClassShapeHarvest.ApplyToReturnType(
                spelled,
                body,
                ownerTypeName,
                callableName,
                this.ShapeUsedNames(ns),
                alias => this._harvestedShapes.Add((ns, alias)),
                anon => this.MapHarvestedAnonymousClass(anon, ns),
                name => SpellClassNameReference(name, ns));
            return updated ?? spelled;
        }

        private AnonymousClassShapeHarvest.HarvestedAnonymousClass MapHarvestedAnonymousClass(
            PhpObjectTypeDeclAst anon,
            string ns)
        {
            var methods = new List<TyhpdefMethod>();
            var properties = new List<TyhpdefProperty>();
            var constants = new List<TyhpdefConstant>();
            if (anon.BoundSymbol is ObjectDeclarationSymbol obj)
            {
                foreach (var member in EnumeratePublicMembers(obj))
                {
                    switch (member)
                    {
                        case ObjectMethodSymbol method:
                            methods.Add(this.MapMethod(method, ns, harvestAnonymousReturns: false));
                            break;
                        case ObjectPropertySymbol prop:
                            properties.Add(this.MapProperty(prop, ns, isStruct: false));
                            break;
                        case ObjectConstantSymbol constant when !constant.IsEnumCase:
                            constants.Add(this.MapObjectConstant(constant, ns));
                            break;
                    }
                }

                var parents = new List<string>();
                var extends = this.Spell(obj.ExtendsType, ns);
                if (!string.IsNullOrWhiteSpace(extends))
                {
                    parents.Add(extends);
                }

                foreach (var implemented in obj.ImplementsTypes)
                {
                    var spelled = this.Spell(implemented, ns);
                    if (!string.IsNullOrWhiteSpace(spelled))
                    {
                        parents.Add(spelled);
                    }
                }

                return new AnonymousClassShapeHarvest.HarvestedAnonymousClass(
                    AnonymousClassShapeHarvest.FilterShapeMembers(methods, properties, constants),
                    parents);
            }

            return this.MapAnonymousClassFromAst(anon, ns);
        }

        private AnonymousClassShapeHarvest.HarvestedAnonymousClass MapAnonymousClassFromAst(
            PhpObjectTypeDeclAst anon,
            string ns)
        {
            var shape = this.MapShapeAst(anon.Body, ns);
            var parents = new List<string>();
            if (anon.Extends is ITypeExpression extendsType)
            {
                var spelled = this.Spell(extendsType, ns);
                if (!string.IsNullOrWhiteSpace(spelled))
                {
                    parents.Add(spelled);
                }
            }
            else if (anon.Extends is not null)
            {
                var spelled = PhpAstTypeExtractor.SpellName(anon.Extends);
                if (spelled.Length > 0)
                {
                    parents.Add(spelled);
                }
            }

            foreach (var implemented in anon.Implements?.GetAllNotNull() ?? [])
            {
                var spelled = implemented is ITypeExpression type
                    ? this.Spell(type, ns)
                    : PhpAstTypeExtractor.SpellName(implemented);
                if (!string.IsNullOrWhiteSpace(spelled))
                {
                    parents.Add(spelled);
                }
            }

            return new AnonymousClassShapeHarvest.HarvestedAnonymousClass(shape, parents);
        }

        private (string AliasedType, List<string> Intersections, TyhpdefObjectShape? Shape) MapAliasRhs(
            ITypeExpression? type,
            string ns)
        {
            if (type is TyhpObjectShapeAst shapeAst)
            {
                return ("", [], this.MapShapeAst(shapeAst.Members, ns));
            }

            if (type is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Intersection } composite)
            {
                var intersections = new List<string>();
                TyhpdefObjectShape? shape = null;
                foreach (var child in composite.Types?.GetAllNotNull() ?? [])
                {
                    if (child is TyhpObjectShapeAst childShape)
                    {
                        shape = this.MapShapeAst(childShape.Members, ns);
                        continue;
                    }

                    var spelled = this.Spell(child, ns);
                    if (!string.IsNullOrWhiteSpace(spelled))
                    {
                        intersections.Add(spelled);
                    }
                }

                if (shape is not null)
                {
                    return ("", intersections, shape);
                }
            }

            return (this.SpellRequired(type, ns), [], null);
        }

        private TyhpdefObjectShape MapShapeAst(PhpClassBodyAst? body, string ns)
        {
            var methods = new List<TyhpdefMethod>();
            var properties = new List<TyhpdefProperty>();
            var constants = new List<TyhpdefConstant>();
            foreach (var member in body?.GetAllNotNull() ?? [])
            {
                switch (member)
                {
                    case PhpMethodDeclAst method when method.BoundSymbol is ObjectMethodSymbol methodSymbol:
                        methods.Add(this.MapMethod(methodSymbol, ns, harvestAnonymousReturns: false));
                        break;
                    case PhpMethodDeclAst method:
                    {
                        var name = method.Identifier ?? "";
                        if (name.Length == 0)
                        {
                            break;
                        }

                        methods.Add(new TyhpdefMethod
                        {
                            Name = name,
                            Modifiers = ["public"],
                            Parameters = this.MapParametersFromAst(method.Parameters, ns),
                            ReturnType = this.Spell(method.ReturnType, ns) ?? "mixed",
                            ReturnsReference = method.ReturnsRef,
                        });
                        break;
                    }
                    case PhpPropertyDeclAst property when property.BoundSymbol is ObjectPropertySymbol prop:
                        properties.Add(this.MapProperty(prop, ns, isStruct: false));
                        break;
                    case PhpPropertyDeclAst property:
                        foreach (var item in property.Properties?.GetAllNotNull() ?? [])
                        {
                            var name = item.Identifier ?? item.ValueString ?? "";
                            if (name.Length == 0)
                            {
                                continue;
                            }

                            properties.Add(new TyhpdefProperty
                            {
                                Name = name.StartsWith('$') ? name : "$" + name,
                                Type = this.Spell(property.Type, ns) ?? "mixed",
                                Modifiers = ["public"],
                            });
                        }

                        break;
                    case PhpConstDeclListAst constList:
                        foreach (var constant in constList.GetAllNotNull())
                        {
                            if (constant.BoundSymbol is ObjectConstantSymbol bound)
                            {
                                constants.Add(this.MapObjectConstant(bound, ns));
                                continue;
                            }

                            var name = constant.Identifier ?? "";
                            if (name.Length == 0)
                            {
                                continue;
                            }

                            constants.Add(new TyhpdefConstant
                            {
                                Name = name,
                                Type = this.Spell(constant.Type, ns) ?? "mixed",
                                Value = this.SpellExpr(constant.Value, ns),
                                Modifiers = ["public"],
                            });
                        }

                        break;
                }
            }

            return AnonymousClassShapeHarvest.FilterShapeMembers(methods, properties, constants);
        }

        private List<TyhpdefParameter> MapParametersFromAst(PhpParameterListAst? list, string ns)
        {
            var result = new List<TyhpdefParameter>();
            foreach (var parameter in list?.GetAllNotNull() ?? [])
            {
                var name = parameter.Name ?? "";
                if (name.Length == 0)
                {
                    continue;
                }

                result.Add(new TyhpdefParameter
                {
                    Name = name.TrimStart('$'),
                    Type = this.Spell(parameter.Type, ns) ?? "mixed",
                    DefaultValue = this.SpellExpr(parameter.DefaultValue, ns),
                    IsVariadic = parameter.IsVariadic,
                    IsByReference = parameter.IsRef,
                });
            }

            return result;
        }

        private static string SpellClassNameReference(IClassNameReference name, string ns)
        {
            _ = ns;
            if (name.BoundSymbol is { FullyQualifiedName: { Length: > 0 } fqn })
            {
                return fqn.StartsWith('\\') ? fqn : "\\" + fqn;
            }

            if (name is ITypeExpression type)
            {
                var spelled = TyhpdefTypeSpeller.Spell(type);
                if (!string.IsNullOrWhiteSpace(spelled))
                {
                    return spelled;
                }
            }

            return PhpAstTypeExtractor.SpellName(name);
        }

        private static string? OwnerTypeName(ObjectMethodSymbol method)
        {
            var scope = method.ContainingScope;
            while (scope != null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol { Name.Length: > 0 } obj
                    && !AnonymousClassShapeHarvest.IsAnonymousClassName(obj.Name))
                {
                    return obj.Name;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private string? CopyDoc(string? doc)
            => this._options.IncludeDocComments ? doc : null;

        private void WriteTyhpdef(TyhpdefFile file, string path, TyhpdefGenerationResult result)
        {
            string text;
            try
            {
                text = TyhpdefOutputWriter.Write(
                    file,
                    new TyhpdefOutputOptions { IncludeDocComments = this._options.IncludeDocComments });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefOutputWriteError,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
                return;
            }

            var parseDiagnostics = new DiagnosticBag();
            try
            {
                var ast = Tyhpdef.ParseContent(text, path, ParseMode.Tyhpdef, parseDiagnostics);
                if (ast is null || parseDiagnostics.HasErrors)
                {
                    var detail = parseDiagnostics.Errors.Count > 0
                        ? parseDiagnostics.Errors[0].Message
                        : "parse returned null";
                    Message.Error("CLI_TyhpdefParseFailed", path, detail);
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        path,
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefParseFailed", path, detail));
                    return;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Message.Error("CLI_TyhpdefParseFailed", path, ex.Message);
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    path,
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefParseFailed", path, ex.Message));
                return;
            }

            if (this._dryRun)
            {
                result.GeneratedFiles.Add(path);
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                result.GeneratedFiles.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefOutputWriteError,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
            }
        }

        private void WriteManifest(string path, TyhpdefGenerationResult result)
        {
            string? existingJson = null;
            if (File.Exists(path))
            {
                try
                {
                    existingJson = File.ReadAllText(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        path,
                        0,
                        0,
                        path,
                        ex.Message);
                    return;
                }
            }

            if (!ComposerExtraTyhpPackageManifest.TryCompose(
                    existingJson,
                    this._isTagless,
                    out var json,
                    out var changed,
                    out var composeError))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefInvalidFormat,
                    path,
                    0,
                    0,
                    path + (string.IsNullOrWhiteSpace(composeError) ? "" : ": " + composeError));
                return;
            }

            if (this._dryRun || (!changed && File.Exists(path)))
            {
                result.GeneratedFiles.Add(path);
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                result.GeneratedFiles.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefOutputWriteError,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
            }
        }

        private static void CountDeclarations(TyhpdefFile file, TyhpdefGenerationResult result)
        {
            static void CountNs(TyhpdefNamespace ns, TyhpdefGenerationResult r)
            {
                r.ClassCount += ns.Classes.Count;
                r.FunctionCount += ns.Functions.Count;
                r.ConstantCount += ns.Constants.Count;
            }

            result.ClassCount = file.GlobalTypes.Count;
            result.FunctionCount = file.GlobalFunctions.Count;
            result.ConstantCount = file.GlobalConstants.Count;
            foreach (var ns in file.Namespaces)
            {
                CountNs(ns, result);
            }

            result.TotalDeclarations = result.ClassCount + result.FunctionCount + result.ConstantCount
                + file.TypeAliases.Count;
        }

        private static bool IsProjectTyhpSource(IBaseSymbol symbol)
        {
            if (symbol is ObjectDeclarationSymbol { IsCompilerGenerated: true })
            {
                return false;
            }

            var file = symbol.SourceFile ?? "";
            return file.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Public library tyhpdef omits <c>private</c> and <c>internal</c> (same generation boundary).
        /// </summary>
        private static bool IsOmittedFromPublicApi(IBaseSymbol symbol)
            => SymbolExportVisibility.OmitFromPublicTyhpdef(symbol);

        /// <summary>
        /// Methods/properties/constants plus operator overloads. Operators are child symbols of
        /// <see cref="ObjectDeclarationScope"/> and are intentionally omitted from
        /// <see cref="ObjectDeclarationSymbol.Members"/>.
        /// </summary>
        private static IEnumerable<IBaseSymbol> EnumeratePublicMembers(ObjectDeclarationSymbol obj)
        {
            var seen = new HashSet<IBaseSymbol>();
            foreach (var member in obj.EnumerateMembersAndConstants())
            {
                if (IsOmittedFromPublicApi(member) || !seen.Add(member))
                {
                    continue;
                }

                yield return member;
            }

            foreach (var op in EnumerateOwnedOperators(obj))
            {
                if (IsOmittedFromPublicApi(op) || !seen.Add(op))
                {
                    continue;
                }

                yield return op;
            }
        }

        private static IEnumerable<ObjectOperatorOverloadMethodSymbol> EnumerateOwnedOperators(
            ObjectDeclarationSymbol obj)
        {
            var parent = obj.ContainingScope;
            if (parent == null)
            {
                yield break;
            }

            foreach (var child in parent.GetAllChildScopes())
            {
                if (child is not ObjectDeclarationScope ods
                    || !ReferenceEquals(ods.DeclarationSymbol, obj))
                {
                    continue;
                }

                foreach (var symbol in ((IBaseScope)ods).GetAllChildSymbols())
                {
                    if (symbol is ObjectOperatorOverloadMethodSymbol op)
                    {
                        yield return op;
                    }
                }

                yield break;
            }
        }

        private static string NamespaceOf(IBaseSymbol symbol)
        {
            var scope = symbol.ContainingScope;
            while (scope != null)
            {
                if (scope is NamespaceBlockScope block
                    && !string.IsNullOrWhiteSpace(block.DeclarationSymbol?.Name))
                {
                    return block.DeclarationSymbol!.Name;
                }

                if (scope is NamespaceScope ns
                    && !string.IsNullOrWhiteSpace(ns.DeclarationSymbol?.Name))
                {
                    return ns.DeclarationSymbol!.Name;
                }

                scope = scope.ParentScope;
            }

            return "";
        }

        private static string DeclaredTypeName(string name)
            => (name ?? "").Trim().TrimStart('\\');

        private static void AddTo<T>(
            string ns,
            Dictionary<string, TyhpdefNamespace> namespaces,
            List<T> globals,
            T item)
        {
            if (string.IsNullOrWhiteSpace(ns))
            {
                globals.Add(item);
                return;
            }

            if (!namespaces.TryGetValue(ns, out var bucket))
            {
                bucket = new TyhpdefNamespace { Name = ns };
                namespaces[ns] = bucket;
            }

            switch (item)
            {
                case TyhpdefClassDeclaration type:
                    bucket.Classes.Add(type);
                    break;
                case TyhpdefFunction function:
                    bucket.Functions.Add(function);
                    break;
                case TyhpdefConstant constant:
                    bucket.Constants.Add(constant);
                    break;
                case TyhpdefTypeAlias alias:
                    bucket.TypeAliases.Add(alias);
                    break;
            }
        }

        private static List<string> FormatTypeModifiers(MemberModifier visibility)
        {
            var list = new List<string>();
            if ((visibility & MemberModifier.Abstract) != 0) list.Add("abstract");
            if ((visibility & MemberModifier.Final) != 0) list.Add("final");
            return list;
        }

        private static List<string> FormatMemberModifiers(
            MemberModifier visibility,
            bool isStatic,
            bool isAbstract,
            bool isAsync)
        {
            var list = new List<string>();
            if ((visibility & MemberModifier.Public) != 0) list.Add("public");
            else if ((visibility & MemberModifier.Protected) != 0) list.Add("protected");
            else if ((visibility & MemberModifier.Private) != 0) list.Add("private");
            else list.Add("public");

            if (isStatic || (visibility & MemberModifier.Static) != 0) list.Add("static");
            if (isAbstract || (visibility & MemberModifier.Abstract) != 0) list.Add("abstract");
            if ((visibility & MemberModifier.Final) != 0) list.Add("final");
            if (isAsync || (visibility & MemberModifier.Async) != 0) list.Add("async");
            return list;
        }

        private static string CompiledOperatorMethodName(ObjectOperatorOverloadMethodSymbol op)
        {
            // Convert is routed through MapOwnedConvertOperator (instance `__to{T}` + static
            // `__from`) and must not use this name. GetMethodName returns "" for Convert.
            var compiledName = OperatorMethodNameGenerator.GetMethodName(op.Operator);
            return string.IsNullOrEmpty(compiledName) ? op.Name : compiledName;
        }

        private static string OperatorToken(ObjectOperatorOverloadMethodSymbol op)
        {
            if (op.DeclaringAstNode is TyhpOperatorOverloadAst ast)
            {
                var token = ast.Op?.ValueString ?? ast.Identifier;
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }
            }

            return string.IsNullOrEmpty(op.Name) ? "+" : op.Name;
        }

        private static string? SpellTargetSymbol(IBaseSymbol? symbol)
        {
            if (symbol == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(symbol.FullyQualifiedName)
                && symbol.FullyQualifiedName.Contains('\\', StringComparison.Ordinal))
            {
                var fqn = symbol.FullyQualifiedName;
                return fqn.StartsWith('\\') ? fqn : "\\" + fqn;
            }

            return symbol.Name;
        }

        private string? SpellExpr(IExpression? expr, string ns)
        {
            if (expr is null)
            {
                return null;
            }

            var resolver = new NameResolver(this._globalScope, this._diagnostics);
            var speller = new TyhpdefExpressionSpeller(type => this.Spell(type, ns), resolver);
            if (speller.TrySpell(expr, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return PhpAstTypeExtractor.SpellLiteral(expr);
        }

        private static PhpParameterAst? FindParameterAst(IBase2Ast? declaringNode, string? name)
        {
            if (declaringNode is null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var want = name.TrimStart('$');
            foreach (var param in EnumerateDeclaredParameters(declaringNode))
            {
                var have = (param.Name ?? param.ValueString ?? "").TrimStart('$');
                if (have.Equals(want, StringComparison.OrdinalIgnoreCase))
                {
                    return param;
                }
            }

            return null;
        }

        private static IEnumerable<PhpParameterAst> EnumerateDeclaredParameters(IBase2Ast node)
        {
            switch (node)
            {
                case PhpParameterAst param:
                    yield return param;
                    yield break;
                case PhpMethodDeclAst method:
                    foreach (var param in method.Parameters?.GetAllNotNull() ?? [])
                    {
                        yield return param;
                    }

                    yield break;
                case PhpFunctionDeclAst function:
                    foreach (var param in function.Parameters?.GetAllNotNull() ?? [])
                    {
                        yield return param;
                    }

                    yield break;
                case TyhpOperatorOverloadAst op:
                    if (op.LeftParameter is { } left)
                    {
                        yield return left;
                    }

                    if (op.RightParameter is { } right)
                    {
                        yield return right;
                    }

                    yield break;
                case PhpParameterListAst list:
                    foreach (var param in list.GetAllNotNull())
                    {
                        yield return param;
                    }

                    yield break;
            }
        }

        private void StampGenericRuntime(List<TyhpdefAttribute> attributes, GenericRuntimeInfo? stamp)
        {
            if (stamp is null)
            {
                return;
            }

            attributes.Add(new TyhpdefAttribute
            {
                Name = "\\Tyhp\\GenericRuntime",
                Arguments = GenericRuntimeAttributeSupport.FormatArgumentList(stamp).ToList(),
            });
        }

        private GenericRuntimeInfo? BinderStamp(IBaseSymbol symbol, string binderName)
        {
            var genericCount = symbol switch
            {
                ObjectMethodSymbol method => method.GenericParameters.Count,
                FunctionDeclarationSymbol function => function.GenericParameters.Count,
                _ => 0,
            };
            if (genericCount == 0)
            {
                return null;
            }

            if (symbol is ObjectMethodSymbol { IsAbstract: true })
            {
                return null;
            }

            var tracked = this._requiresGenericVariant.Contains(symbol);
            return new GenericRuntimeInfo
            {
                Erased = !tracked,
                Binder = tracked && !string.IsNullOrWhiteSpace(binderName) ? binderName : null,
                Layouts = [GenericRuntimeAttributeSupport.CurrentLayout],
                Compiler = this._compilerVersion,
            };
        }

        private GenericRuntimeInfo? FactoryStamp(ObjectDeclarationSymbol obj)
        {
            if (obj.GenericParameters.Count == 0
                || obj.IsStruct
                || obj.ObjectKind is PhpTypeDeclType.Interface or PhpTypeDeclType.Trait
                || (obj.Visibility & MemberModifier.Abstract) != 0)
            {
                return null;
            }

            var tracked = this._requiresRuntimeGenericTracking.Contains(obj);
            return new GenericRuntimeInfo
            {
                Erased = !tracked,
                Factory = tracked ? GeneratedNames.GenericFactory(obj.FullyQualifiedName) : null,
                Layouts = [GenericRuntimeAttributeSupport.CurrentLayout],
                Compiler = this._compilerVersion,
            };
        }

        private GenericRuntimeInfo? AliasFactoryStamp(IBaseSymbol alias, string factoryName)
        {
            if (!ShouldStampAliasFactory(alias) || string.IsNullOrWhiteSpace(factoryName))
            {
                return null;
            }

            return new GenericRuntimeInfo
            {
                AliasFactory = factoryName,
                Layouts = [GenericRuntimeAttributeSupport.CurrentLayout],
                Compiler = this._compilerVersion,
            };
        }

        private static bool ShouldStampAliasFactory(IBaseSymbol alias)
        {
            if (alias.DeclaringAstNode is { } node && NoEmitAttributeSupport.ShouldOmitDeclaration(node))
            {
                return false;
            }

            var file = alias.SourceFile ?? "";
            if (file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return alias is not BaseSymbol { DeclaringAstNode.LanguageMode: "tyhpdef" };
        }

        private List<string> CollectInPackageGlobalUses()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var file in this._parsedFiles)
            {
                var path = file.Identifier ?? file.FileName ?? "";
                if (!path.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                this.CollectGlobalUsesFromNode(file, seen, result);
            }

            return result;
        }

        private void CollectGlobalUsesFromNode(IBase2Ast node, HashSet<string> seen, List<string> result)
        {
            switch (node)
            {
                case TyhpImportExtensionAst import:
                    if (import.IsGlobal)
                    {
                        TryAddGlobalUse(this.SpellGlobalUseExtension(import), seen, result);
                    }

                    return;
                case PhpImportDeclListAst list when list.IsGlobal:
                    TryAddGlobalUse(this.SpellGlobalClassUse(list), seen, result);
                    return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    this.CollectGlobalUsesFromNode(child, seen, result);
                }
            }
        }

        private static void TryAddGlobalUse(string? statement, HashSet<string> seen, List<string> result)
        {
            var text = statement?.Trim();
            if (string.IsNullOrEmpty(text) || !seen.Add(text))
            {
                return;
            }

            result.Add(text);
        }

        private string? SpellGlobalUseExtension(TyhpImportExtensionAst import)
        {
            var clauses = new List<string>();
            foreach (var decl in import.UseDeclarations?.GetAllNotNull() ?? [])
            {
                var ext = this.FindExtensionByWrittenName(decl.NamespaceName);
                if (ext is null || !IsProjectTyhpSource(ext))
                {
                    continue;
                }

                var clause = SpellFqn(ext);
                if (!string.IsNullOrEmpty(decl.Identifier)
                    && !decl.Identifier.Equals(ext.Name, StringComparison.Ordinal))
                {
                    clause += " as " + decl.Identifier;
                }

                clauses.Add(clause);
            }

            if (clauses.Count == 0)
            {
                return null;
            }

            var statement = "global use extension " + string.Join(", ", clauses);
            var adaptations = this.SpellAdaptations(import.Adaptations);
            return string.IsNullOrEmpty(adaptations) ? statement + ";" : statement + " " + adaptations;
        }

        private string? SpellGlobalClassUse(PhpImportDeclListAst list)
        {
            var clauses = new List<string>();
            string? useKind = null;
            foreach (var import in list.GetAllNotNull())
            {
                var target = this.FindImportedTarget(import.NamespaceName);
                if (target is null || !IsProjectTyhpSource(target))
                {
                    continue;
                }

                var clause = SpellFqn(target);
                if (!string.IsNullOrEmpty(import.Identifier)
                    && !import.Identifier.Equals(target.Name, StringComparison.Ordinal))
                {
                    clause += " as " + import.Identifier;
                }

                clauses.Add(clause);
                useKind ??= import.UseType?.ValueString;
            }

            if (clauses.Count == 0)
            {
                return null;
            }

            var prefix = string.IsNullOrWhiteSpace(useKind)
                ? "global use "
                : "global use " + useKind.Trim() + " ";
            return prefix + string.Join(", ", clauses) + ";";
        }

        private string? SpellAdaptations(PhpTraitAdaptationListAst? adaptations)
        {
            if (adaptations is null)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var item in adaptations.GetAllNotNull())
            {
                switch (item)
                {
                    case PhpTraitAliasAst alias when alias.IsHide:
                        parts.Add(this.SpellMemberRef(alias.MethodReference) + " hide");
                        break;
                    case PhpTraitAliasAst alias:
                    {
                        var text = this.SpellMemberRef(alias.MethodReference) + " as";
                        if (alias.NewModifier is { } modifier)
                        {
                            text += " " + modifier.ToString().ToLowerInvariant();
                        }

                        if (!string.IsNullOrEmpty(alias.Identifier))
                        {
                            text += " " + alias.Identifier;
                        }

                        parts.Add(text);
                        break;
                    }
                    case PhpTraitPrecedenceAst precedence:
                    {
                        var instead = new List<string>();
                        foreach (var trait in precedence.InsteadOfTraits?.GetAllNotNull() ?? [])
                        {
                            instead.Add(this.SpellClassName(trait));
                        }

                        parts.Add(
                            this.SpellMemberRef(precedence.MethodReference)
                            + " insteadof "
                            + string.Join(", ", instead));
                        break;
                    }
                }
            }

            if (parts.Count == 0)
            {
                return null;
            }

            return "{ " + string.Join("; ", parts) + "; }";
        }

        private string SpellMemberRef(PhpTraitMemberRefAst? member)
        {
            if (member is null)
            {
                return "";
            }

            var trait = this.SpellClassName(member.TraitName);
            if (member.IsOperator)
            {
                var op = member.OperatorToken ?? member.Identifier ?? "";
                var spelled = string.IsNullOrEmpty(trait) ? "operator " + op : trait + "::operator " + op;
                if (member.OperatorTarget is { } target)
                {
                    var spelledTarget = this.Spell(target, ns: "");
                    if (!string.IsNullOrEmpty(spelledTarget))
                    {
                        spelled += "<" + spelledTarget + ">";
                    }
                }

                return spelled;
            }

            var name = member.MemberName?.Identifier ?? member.Identifier ?? "";
            return string.IsNullOrEmpty(trait) ? name : trait + "::" + name;
        }

        private string SpellClassName(IClassName? name)
        {
            if (name is IBase2Ast { BoundSymbol: { } symbol })
            {
                return SpellFqn(symbol);
            }

            var written = name?.ValueString ?? name?.Identifier ?? "";
            if (string.IsNullOrEmpty(written))
            {
                return "";
            }

            return written.StartsWith('\\') ? written : "\\" + written.TrimStart('\\');
        }

        private ObjectDeclarationSymbol? FindExtensionByWrittenName(string? written)
        {
            var simple = (written ?? "").Trim().TrimStart('\\');
            if (string.IsNullOrEmpty(simple))
            {
                return null;
            }

            foreach (var ext in this._globalScope.GloballyActivatedExtensions)
            {
                var fqn = (ext.FullyQualifiedName ?? ext.Name).TrimStart('\\');
                if (fqn.Equals(simple, StringComparison.OrdinalIgnoreCase)
                    || ext.Name.Equals(simple, StringComparison.OrdinalIgnoreCase))
                {
                    return ext;
                }
            }

            return FindSymbolByFqn(this._globalScope, simple) as ObjectDeclarationSymbol;
        }

        private IBaseSymbol? FindImportedTarget(string? written)
        {
            var simple = (written ?? "").Trim().TrimStart('\\');
            if (string.IsNullOrEmpty(simple))
            {
                return null;
            }

            return FindSymbolByFqn(this._globalScope, simple);
        }

        private static IBaseSymbol? FindSymbolByFqn(IBaseScope scope, string fqn)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is UseIncludeSymbol)
                {
                    continue;
                }

                var name = (symbol.FullyQualifiedName ?? symbol.Name ?? "").TrimStart('\\');
                if (name.Equals(fqn, StringComparison.OrdinalIgnoreCase))
                {
                    return symbol;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                if (child is null)
                {
                    continue;
                }

                var found = FindSymbolByFqn(child, fqn);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string SpellFqn(IBaseSymbol symbol)
        {
            var fqn = (symbol.FullyQualifiedName ?? symbol.Name ?? "").Trim();
            if (string.IsNullOrEmpty(fqn))
            {
                return "";
            }

            return fqn.StartsWith('\\') ? fqn : "\\" + fqn;
        }

        private static string FormatCallArg(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return "$this";
            }

            return trimmed.StartsWith('$') ? trimmed : "$" + trimmed;
        }
    }
}
