using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private EmitItem EmitNamespaceDeclaration(PhpNamespaceDeclAst namespaceDecl, EmitItem parent)
        {
            this._context.CurrentSourceNamespace = namespaceDecl.Identifier;
            var namespaceName = this.ApplyNamespacePrefix(namespaceDecl.Identifier);
            var nsText = string.IsNullOrWhiteSpace(namespaceName)
                ? "namespace;"
                : $"namespace {namespaceName};";
            this._context.CurrentNamespace = namespaceName;
            return this.ApplyDocComment(namespaceDecl, EmitItem.Line(namespaceDecl, EmitType.FileNamespaceDeclaration, nsText, parent));
        }

        private EmitItem EmitBlockNamespaceDeclaration(PhpBlockNamespaceDeclAst blockNamespace, EmitItem parent)
        {
            this._context.CurrentSourceNamespace = blockNamespace.Identifier;
            var namespaceName = this.ApplyNamespacePrefix(blockNamespace.Identifier);
            var nsText = string.IsNullOrWhiteSpace(namespaceName)
                ? "namespace {"
                : $"namespace {namespaceName} {{";
            this._context.CurrentNamespace = namespaceName;
            return this.ApplyDocComment(blockNamespace, EmitItem.Block(blockNamespace, EmitType.BlockNamespaceDeclaration, nsText, "}", parent));
        }

        private EmitItem EmitImportList(PhpImportDeclListAst importList, EmitItem parent)
        {
            var imports = importList.GetAllNotNull().ToList();
            if (imports.Count == 0)
            {
                return EmitItem.Empty(importList, EmitType.ImportUse, parent);
            }

            // PSR-12: one import per `use` statement (no comma-grouped clauses).
            EmitItem? last = null;
            foreach (var import in imports)
            {
                last = this.EmitImportDeclaration(import, parent);
            }

            return last ?? EmitItem.Empty(importList, EmitType.ImportUse, parent);
        }

        private EmitItem EmitImportDeclaration(PhpImportDeclAst importDecl, EmitItem parent)
        {
            var clause = this.BuildImportClause(importDecl);
            _ = clause.Fqn;
            var prefix = importDecl.UseType?.ValueString switch
            {
                "function" => "use function ",
                "const" => "use const ",
                _ => "use ",
            };
            return EmitItem.Line(importDecl, EmitType.ImportUse, prefix + clause.Text + ";", parent);
        }

        private (string Fqn, string Text) BuildImportClause(PhpImportDeclAst import)
        {
            var fqn = import.NamespaceName ?? "";
            if (!string.IsNullOrWhiteSpace(import.Identifier))
            {
                return (fqn, fqn + " as " + import.Identifier);
            }

            return (fqn, fqn);
        }

        private EmitItem EmitObjectDeclaration(PhpObjectTypeDeclAst objectDecl, EmitItem parent)
        {
            var declType = objectDecl.DeclType?.ValueString ?? "class";
            var modifiers = this.FormatModifiers(objectDecl.Modifiers);
            var namePart = objectDecl.IsAnonymousClass ? "" : " " + this.StripGenericsFromName(objectDecl.Identifier);
            var hasParentList = objectDecl.Implements?.GetAllNotNull().Any() == true;
            string extends;
            string implements;
            if (string.Equals(declType, "trait", StringComparison.OrdinalIgnoreCase))
            {
                // Trait extends/implements requirements are compile-time only (validated by checker).
                // PHP traits cannot extend or implement — strip these clauses entirely.
                extends = "";
                implements = "";
            }
            else if (string.Equals(declType, "interface", StringComparison.OrdinalIgnoreCase))
            {
                // An interface carries its parent interfaces in the Implements slot and emits them after
                // `extends` (PHP allows multiple, comma-separated parents); interfaces never use `implements`.
                extends = hasParentList ? " extends " + this.FormatClassNameList(objectDecl.Implements!) : "";
                implements = "";
            }
            else
            {
                extends = objectDecl.Extends != null ? " extends " + this.BuildClassName(objectDecl.Extends) : "";
                var implementsNames = new List<string>();
                if (hasParentList)
                {
                    implementsNames.AddRange(objectDecl.Implements!.GetAllNotNull().Select(this.BuildClassName));
                }

                // Auto-add the \Tyhp\Contracts\*Convertible interface for each convert-to overload so
                // the emitted class satisfies the corresponding instance conversion contract.
                var seenImplements = new HashSet<string>(
                    implementsNames.Select(n => "\\" + n.TrimStart('\\')), StringComparer.OrdinalIgnoreCase);
                foreach (var iface in this.CollectConvertibleInterfaces(objectDecl))
                {
                    if (seenImplements.Add(iface))
                    {
                        implementsNames.Add(iface);
                    }
                }

                implements = implementsNames.Count > 0
                    ? " implements " + string.Join(", ", implementsNames)
                    : "";
            }
            var backedType = objectDecl.BackingType != null
                ? ": " + this.BuildTypeExpression(objectDecl.BackingType)
                : "";
            // Named classes/interfaces/traits/enums: brace on next line (PSR-12 §4.1).
            // Anonymous classes: brace may stay on the same line (PSR-12 §8).
            var signature = $"{modifiers}{declType}{namePart}{extends}{implements}{backedType}";
            var block = this.ApplyDocComment(
                objectDecl,
                objectDecl.IsAnonymousClass
                    ? EmitItem.Block(objectDecl, EmitType.ObjectDeclaration, signature + " {", "}", parent)
                    : EmitItem.BlockBraceNextLine(objectDecl, EmitType.ObjectDeclaration, signature, "}", parent));
            this.AttachAttributes(objectDecl, block);

            var isInterface = string.Equals(declType, "interface", StringComparison.OrdinalIgnoreCase);
            var previousObjectShortName = this._currentObjectShortName;
            var previousObjectDecl = this._currentObjectDecl;
            var previousObjectSymbol = this._currentObjectSymbol;
            var previousNeedsTracking = this._currentObjectNeedsGenericTracking;
            var previousGenericParams = this._currentObjectGenericParams;
            var previousGenericParamNames = this._currentObjectGenericParamNames.ToHashSet(StringComparer.Ordinal);
            var previousEmittedCtor = this._currentObjectEmittedConstructor;
            var previousInGenericChain = this._currentObjectInGenericChain;
            var previousParentInGenericChain = this._currentObjectParentInGenericChain;
            var previousInPropertyHookChain = this._currentObjectInPropertyHookChain;
            var previousParentInPropertyHookChain = this._currentObjectParentInPropertyHookChain;
            var previousObjectFqn = this._currentObjectFqn;
            var previousPendingOperators = this._pendingOperatorOverloads.ToList();

            this._currentObjectShortName = objectDecl.IsAnonymousClass
                ? this._currentObjectShortName
                : this.StripGenericsFromName(objectDecl.Identifier)?.TrimStart('\\').Split('\\')[^1];
            this.BeginGenericObjectObjectScope(objectDecl);
            this.BeginPropertyAccessorObjectScope(objectDecl);
            this.CollectFreeGenericSetCheckProperties(objectDecl);
            this.AttachPropertyAccessorMagicPropertyDocs(objectDecl, block);
            this._pendingOperatorOverloads.Clear();
            if (objectDecl.Body != null)
            {
                this.EmitGenericObjectTraitUseIfNeeded(objectDecl, block);
                this.EmitPropertyAccessorsTraitUseIfNeeded(objectDecl, block);

                var members = objectDecl.Body.GetAllNotNull().ToList();
                var implementedMethodNames = OverloadSignatureHelper.CollectImplementedMethodNames(members);
                foreach (var member in members)
                {
                    // Overload signatures are compile-time-only; erase them and keep the implementation.
                    if (member is PhpMethodDeclAst method
                        && OverloadSignatureHelper.IsClassMethodOverloadSignature(method, implementedMethodNames))
                    {
                        continue;
                    }

                    this.EmitClassMember(member, block, isInterface);
                }

                this.EmitSynthesizedGenericObjectConstructorIfNeeded(objectDecl, block);
                this.EmitSynthesizedPropertyAccessorConstructorIfNeeded(objectDecl, block);
                this.EmitGenericInitHook(objectDecl, block);
                this.EmitPropertyHookInitHook(objectDecl, block);
                this.EmitGenericFactoryIfNeeded(objectDecl, block);

                // Collapsed static operator methods (one per operator) + convert to/from methods,
                // emitted after all class members are collected.
                this.EmitCollapsedOperatorMethods(block, this._pendingOperatorOverloads, isExtension: false);
            }

            this._currentObjectShortName = previousObjectShortName;
            this._currentObjectDecl = previousObjectDecl;
            this._currentObjectSymbol = previousObjectSymbol;
            this._currentObjectNeedsGenericTracking = previousNeedsTracking;
            this._currentObjectGenericParams = previousGenericParams;
            this._currentObjectGenericParamNames.Clear();
            foreach (var n in previousGenericParamNames)
            {
                this._currentObjectGenericParamNames.Add(n);
            }

            this._currentObjectEmittedConstructor = previousEmittedCtor;
            this._currentObjectInGenericChain = previousInGenericChain;
            this._currentObjectParentInGenericChain = previousParentInGenericChain;
            this._currentObjectInPropertyHookChain = previousInPropertyHookChain;
            this._currentObjectParentInPropertyHookChain = previousParentInPropertyHookChain;
            this._currentObjectFqn = previousObjectFqn;
            this._ctorGenericLocalVars = null;
            this._pendingOperatorOverloads.Clear();
            this._pendingOperatorOverloads.AddRange(previousPendingOperators);

            return block;
        }

        private EmitItem EmitExtensionDeclaration(TyhpExtensionDeclAst extensionDecl, EmitItem parent)
        {
            var name = this.StripGenericsFromName(extensionDecl.Identifier);
            var block = this.ApplyDocComment(
                extensionDecl,
                EmitItem.BlockBraceNextLine(extensionDecl, EmitType.ObjectDeclaration, $"class {name}", "}", parent));
            this.AttachAttributes(extensionDecl, block);

            var extensionOperators = new List<TyhpOperatorOverloadAst>();
            foreach (var member in extensionDecl.FunctionList?.GetAllNotNull() ?? [])
            {
                if (member is PhpFunctionDeclAst function)
                {
                    this.EmitExtensionMethod(function, block);
                }
                else if (member is TyhpOperatorOverloadAst overload)
                {
                    extensionOperators.Add(overload);
                }
            }

            if (extensionOperators.Count > 0)
            {
                this.EmitCollapsedOperatorMethods(block, extensionOperators, isExtension: true);
            }

            return block;
        }

        private void EmitExtensionMethod(PhpFunctionDeclAst function, EmitItem parent)
        {
            var previousAlias = this._context.ExtensionReceiverThisAlias;
            try
            {
                this.BeginExtensionReceiverThisRenameIfNeeded(function.Parameters);

                if (this.IsAsyncModifiers(function))
                {
                    var asyncSig = "public static function " + this.BuildAsyncOuterSignature(function);
                    var methodBlock = EmitItem.BlockBraceNextLine(function, EmitType.ObjectStaticMethods, asyncSig, "}", parent);
                    this.AttachAttributes(function, methodBlock);
                    this.EmitAsyncWrappedBody(function, methodBlock, captureThis: false);
                    return;
                }

                // BuildFunctionSignature is name+params+return only; callers must supply `function`.
                var signature = "public static function " + this.BuildFunctionSignature(function);
                var methodBlock2 = EmitItem.BlockBraceNextLine(function, EmitType.ObjectStaticMethods, signature, "}", parent);
                this.AttachAttributes(function, methodBlock2);
                this.EmitFunctionBody(function.Body, methodBlock2);
            }
            finally
            {
                this._context.ExtensionReceiverThisAlias = previousAlias;
            }
        }

        private EmitItem EmitFunctionDeclaration(PhpFunctionDeclAst functionDecl, EmitItem parent)
        {
            // Overload signatures are compile-time-only; erase them and keep the implementation.
            // Named short functions (`fn name(...) => expr;`) are already desugared by the visitor
            // into a normal PhpFunctionDeclAst with a `return expr;` body, so they emit here as
            // ordinary `function` declarations. Anonymous PHP arrows (`fn($x) => …`) are a
            // different AST (`PhpInlineFunctionAst`) and keep arrow syntax in Expressions.
            if (OverloadSignatureHelper.IsErasableFunctionOverloadSignature(functionDecl))
            {
                return EmitItem.Empty(functionDecl, EmitType.RootStatement, parent);
            }

            var previousCallableGenerics = this._currentCallableGenericParamNames.ToHashSet(StringComparer.Ordinal);
            this.PushCallableGenericParamNames(functionDecl.BoundSymbol as FunctionDeclarationSymbol);
            try
            {
                return this.EmitFunctionDeclarationCore(functionDecl, parent);
            }
            finally
            {
                this._currentCallableGenericParamNames.Clear();
                foreach (var n in previousCallableGenerics)
                {
                    this._currentCallableGenericParamNames.Add(n);
                }
            }
        }

        private EmitItem EmitFunctionDeclarationCore(PhpFunctionDeclAst functionDecl, EmitItem parent)
        {
            var functionVariantGenerics = this.ResolveVariantGenericParams(functionDecl);
            if (functionVariantGenerics.Count > 0)
            {
                return this.EmitGenericVariantPair(functionDecl, parent, functionVariantGenerics);
            }

            if (this.IsAsyncModifiers(functionDecl))
            {
                var asyncSig = "function " + this.BuildAsyncOuterSignature(functionDecl);
                var asyncBlock = this.ApplyDocComment(
                    functionDecl,
                    EmitItem.BlockBraceNextLine(functionDecl, EmitType.RootStatement, asyncSig, "}", parent));
                this.AttachAttributes(functionDecl, asyncBlock);
                this.EmitAsyncWrappedBody(functionDecl, asyncBlock, captureThis: false);
                return asyncBlock;
            }

            var signature = "function " + this.BuildFunctionSignature(functionDecl);
            var block = this.ApplyDocComment(
                functionDecl,
                EmitItem.BlockBraceNextLine(functionDecl, EmitType.RootStatement, signature, "}", parent));
            this.AttachAttributes(functionDecl, block);
            this.EmitFunctionBody(functionDecl.Body, block);
            return block;
        }

        private EmitItem EmitConstDeclaration(
            PhpConstDeclAst constDecl,
            EmitItem parent,
            EmitType emitType = EmitType.ObjectConstantDeclaration,
            IBase2Ast? attributeSource = null)
        {
            // Class constants: attributes are legal since PHP 8.0 (all Tyhp targets).
            // Top-level / namespace `const`: attributes are native PHP ≥ 8.5; on lower targets strip
            // and warn (TYHP5017) because Reflection would no longer see them.
            // Attributes live on the enclosing list (comma decls share one attribute group), so
            // repeat them once per emitted line — same as modifiers/types.
            var modifiers = this.FormatModifiers(constDecl.Modifiers);
            var type = constDecl.Type != null ? this.BuildTypeExpression(constDecl.Type) + " " : "";
            var line = modifiers + "const " + type + constDecl.Identifier + " = " + this.BuildExpression(constDecl.Value) + ";";
            var item = this.ApplyDocComment(constDecl, EmitItem.Line(constDecl, emitType, line, parent));
            var source = attributeSource ?? constDecl;
            if (emitType == EmitType.ObjectConstantDeclaration)
            {
                this.AttachAttributes(source, item);
            }
            else if (emitType == EmitType.RootStatement)
            {
                if (this._context.IsPhpVersionAtLeast(8, 5))
                {
                    this.AttachAttributes(source, item);
                }
                else
                {
                    this.ReportStrippedAttributes(source, "constant", requiredPhpVersion: "8.5");
                }
            }

            return item;
        }

        private EmitItem EmitConstDeclarationList(
            PhpConstDeclListAst constList,
            EmitItem parent,
            EmitType emitType = EmitType.ObjectConstantDeclaration)
        {
            EmitItem? last = null;
            foreach (var decl in constList.GetAllNotNull())
            {
                last = this.EmitConstDeclaration(decl, parent, emitType, attributeSource: constList);
            }

            return last ?? EmitItem.Empty(constList, emitType, parent);
        }

        private EmitItem EmitDeclareStatement(PhpDeclareAst declareAst, EmitItem parent, EmitType emitType = EmitType.FileDeclare)
        {
            if (!this.ShouldEmitFileDeclare(declareAst))
            {
                return EmitItem.Empty(declareAst, emitType, parent);
            }

            var declarationParts = declareAst.Declarations?.GetAllNotNull()
                .Where(c => !IsTyhpOnlyDeclareKey(c.Identifier))
                .Select(c => c.Identifier + "=" + this.BuildExpression(c.Value))
                .ToList()
                ?? [];
            var declareText = "declare(" + string.Join(", ", declarationParts) + ")";

            if (IsEmptyDeclareBody(declareAst.Body))
            {
                // Sole Tyhp-only directives were already filtered by ShouldEmitFileDeclare; if
                // filtering left an empty declare(...) list, emit nothing.
                if (declarationParts.Count == 0)
                {
                    return EmitItem.Empty(declareAst, emitType, parent);
                }

                var lineType = emitType == EmitType.FunctionStatement ? emitType : EmitType.FileDeclare;
                return this.ApplyDocComment(declareAst, EmitItem.Line(declareAst, lineType, declareText + ";", parent));
            }

            var block = this.ApplyDocComment(declareAst, EmitItem.Block(declareAst, EmitType.BlockDeclare, declareText + " {", "}", parent));
            if (declareAst.Body is PhpStatementBlockAst bodyBlock)
            {
                foreach (var stmt in bodyBlock.GetAllNotNull())
                {
                    this.EmitStatement(stmt, block, EmitType.SubBlockStatement);
                }
            }
            else if (declareAst.Body != null)
            {
                this.EmitStatement(declareAst.Body, block, EmitType.SubBlockStatement);
            }

            return block;
        }

        private static bool IsEmptyDeclareBody(IStatement? body)
            => body == null
                || body is PhpNopStatementAst
                || (body is PhpStatementBlockAst block && !block.GetAllNotNull().Any());

        private EmitItem EmitClassMember(IClassMember member, EmitItem parent, bool isInterface = false)
            => member switch
            {
                PhpMethodDeclAst method => this.EmitMethodDeclaration(method, parent, isInterface),
                PhpPropertyDeclAst property => this.EmitPropertyDeclaration(property, parent),
                PhpTraitUseAst traitUse => this.EmitTraitUse(traitUse, parent),
                PhpEnumCaseAst enumCase => this.EmitEnumCase(enumCase, parent),
                PhpConstDeclAst constDecl => this.EmitConstDeclaration(constDecl, parent, EmitType.ObjectConstantDeclaration),
                PhpConstDeclListAst constList => this.EmitConstDeclarationList(constList, parent, EmitType.ObjectConstantDeclaration),
                TyhpOperatorOverloadAst overload => this.EmitClassOperatorOverload(overload, parent),
                _ => EmitItem.Empty(member, EmitType.ObjectInstanceMethods, parent),
            };

        private EmitItem EmitMethodDeclaration(PhpMethodDeclAst method, EmitItem parent, bool isInterface = false)
        {
            var previousIsStatic = this._currentMemberIsStatic;
            var previousCallableGenerics = this._currentCallableGenericParamNames.ToHashSet(StringComparer.Ordinal);
            this._currentMemberIsStatic = method.Modifiers?.Modifiers.Contains(PhpModifier.Static) == true;
            this.PushCallableGenericParamNames(method.BoundSymbol as ObjectMethodSymbol);
            try
            {
                return this.EmitMethodDeclarationCore(method, parent, isInterface);
            }
            finally
            {
                this._currentMemberIsStatic = previousIsStatic;
                this._currentCallableGenericParamNames.Clear();
                foreach (var n in previousCallableGenerics)
                {
                    this._currentCallableGenericParamNames.Add(n);
                }
            }
        }

        private EmitItem EmitMethodDeclarationCore(PhpMethodDeclAst method, EmitItem parent, bool isInterface)
        {
            var isAbstractDecl = method.Modifiers?.Modifiers.Contains(PhpModifier.Abstract) == true;
            var variantGenerics = this.ResolveVariantGenericParams(method);
            if (variantGenerics.Count > 0)
            {
                // A contract carries both signatures: a call through the interface or abstract type
                // targets the variant, so every implementation has to be required to declare it.
                return isInterface || isAbstractDecl
                    ? this.EmitGenericVariantContract(method, parent, variantGenerics)
                    : this.EmitGenericVariantPair(method, parent, variantGenerics);
            }

            if (this.IsAsyncModifiers(method))
            {
                var emitTypeAsync = this.GetMethodEmitType(method);
                var asyncMethodSig = this.BuildAsyncOuterMethodSignature(method);
                var isAbstractAsync = method.Modifiers?.Modifiers.Contains(PhpModifier.Abstract) == true;
                if (isInterface || isAbstractAsync)
                {
                    var asyncLine = this.ApplyDocComment(
                        method,
                        EmitItem.Line(method, emitTypeAsync, asyncMethodSig + ";", parent));
                    this.AttachAttributes(method, asyncLine);
                    return asyncLine;
                }

                var asyncMethodBlock = this.ApplyDocComment(
                    method,
                    EmitItem.BlockBraceNextLine(method, emitTypeAsync, asyncMethodSig, "}", parent));
                this.AttachAttributes(method, asyncMethodBlock);
                this.EmitConstructorParentCall(method, asyncMethodBlock);
                this.EmitAsyncWrappedMethodBody(method, asyncMethodBlock);
                return asyncMethodBlock;
            }

            var emitType = this.GetMethodEmitType(method);
            var signature = this.BuildMethodSignature(method);

            // Interface methods and abstract methods declare no body: they must terminate with `;`
            // rather than an empty `{}` block, which PHP rejects ("cannot contain body"). Use the
            // declaration's interface/abstract status rather than method.Body, since a concrete method
            // could also legitimately have an empty body.
            var isAbstract = method.Modifiers?.Modifiers.Contains(PhpModifier.Abstract) == true;
            if (isInterface || isAbstract)
            {
                var abstractLine = this.ApplyDocComment(
                    method,
                    EmitItem.Line(method, emitType, signature + ";", parent));
                this.AttachAttributes(method, abstractLine);
                return abstractLine;
            }

            var block = this.ApplyDocComment(
                method,
                EmitItem.BlockBraceNextLine(method, emitType, signature, "}", parent));
            this.AttachAttributes(method, block);

            var isConstructor = string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase);
            if (isConstructor)
            {
                this._currentObjectEmittedConstructor = true;

                // The generic gate has to precede the author's `: parent(...)`: reaching an ancestor
                // constructor before this level's bindings exist would let the ancestor's own gate fire
                // first and bind this level's parameters to their declared defaults.
                this.EmitGenericObjectConstructorPrologue(method, block, includeUserParamChecks: true);
                this.EmitPropertyAccessorConstructorPrologue(method, block, parent);
                this.EmitConstructorParentCall(method, block);
            }
            else if (this._currentObjectNeedsGenericTracking
                && this._context.IsRuntimeGenericChecks())
            {
                this.EmitRuntimeGenericParamChecks(method, block, preferCtorLocals: false);
            }

            this.EmitMagicTryInjectPreambleIfNeeded(method, block);

            var previousReturnCheck = this._currentMethodGenericReturnCheck;
            try
            {
                this._currentMethodGenericReturnCheck = this.ResolveMethodGenericReturnCheck(method, isConstructor);
                this.EmitFunctionBody(method.Body, block);
            }
            finally
            {
                this._currentMethodGenericReturnCheck = previousReturnCheck;
            }

            if (isConstructor)
            {
                // After author body (and promoted init) so ctor writes stay unchecked until now.
                this.EmitGenericObjectEnablePropertyChecksIfNeeded(method, block);
            }

            return block;
        }

        /// <summary>
        /// Builds the runtime expected-type expression for generic return checks, or null when
        /// checks do not apply to this method.
        /// </summary>
        private string? ResolveMethodGenericReturnCheck(PhpMethodDeclAst method, bool isConstructor)
        {
            if (isConstructor
                || !this._currentObjectNeedsGenericTracking
                || !this._context.IsRuntimeGenericChecks()
                || method.ReturnType is null
                || !this.TypeAstInvolvesGenerics(method.ReturnType))
            {
                return null;
            }

            return this.BuildRuntimeTypeExpression(method.ReturnType, preferCtorLocals: false);
        }

        // A Tyhp constructor may delegate to its parent via `): parent(args)`. That delegation is
        // captured as a `ctorReturnType` grammar addon and must be emitted as the first body
        // statement (`parent::__construct(args);`); otherwise the parent constructor is never
        // invoked and the call is silently dropped.
        private void EmitConstructorParentCall(PhpMethodDeclAst method, EmitItem parent)
        {
            if (!method.AstGrammarAddons.TryGetValue("ctorReturnType", out var addon)
                || addon is not TyhpCtorReturnTypeAst ctorReturn)
            {
                return;
            }

            if (!string.Equals(ctorReturn.TypeToken?.ValueString, "parent", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var args = this.FormatArgumentList(ctorReturn.Arguments);
            EmitItem.Line(method, EmitType.FunctionStatement, $"parent::__construct({args});", parent);
        }

        private EmitType GetMethodEmitType(PhpMethodDeclAst method)
        {
            var name = method.Identifier ?? "";
            if (string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase))
            {
                return EmitType.ObjectConstructor;
            }

            if (string.Equals(name, "__destruct", StringComparison.OrdinalIgnoreCase))
            {
                return EmitType.ObjectDestructor;
            }

            if (method.Modifiers?.Modifiers.Contains(PhpModifier.Static) == true)
            {
                return EmitType.ObjectStaticMethods;
            }

            return EmitType.ObjectInstanceMethods;
        }

        private EmitItem EmitPropertyDeclaration(PhpPropertyDeclAst propertyDecl, EmitItem parent)
        {
            var isStatic = propertyDecl.Modifiers?.Modifiers.Contains(PhpModifier.Static) == true;
            var emitType = isStatic
                ? EmitType.ObjectStaticPropertyDeclaration
                : EmitType.ObjectInstancePropertyDeclaration;

            // Attributes belong to the declaration; comma-separated names each need their own
            // attribute line (PHP attaches attributes to a single property, not a group).
            EmitItem? last = null;
            foreach (var property in propertyDecl.Properties?.GetAllNotNull() ?? [])
            {
                if (this.ShouldSkipEmittingHookedProperty(property))
                {
                    // Property line (and native hook attributes) are omitted under the polyfill;
                    // diagnose stripped hook attributes here because BuildPropertyLine is not reached.
                    this.ReportStrippedPropertyHookAttributes(property.Hooks);
                    continue;
                }

                var line = this.BuildPropertyLine(propertyDecl, property);
                last = this.ApplyDocComment(property, EmitItem.Line(property, emitType, line, parent));
                this.AttachAttributes(propertyDecl, last);
            }

            return last ?? EmitItem.Empty(propertyDecl, emitType, parent);
        }

        private string BuildPropertyLine(PhpPropertyDeclAst decl, PhpPropertyAst property)
        {
            var modifiers = this.FormatModifiers(decl.Modifiers);
            var type = decl.Type != null ? this.BuildTypeExpression(decl.Type) + " " : "";
            var defaultValue = property.DefaultValue != null
                ? " = " + this.BuildExpression(property.DefaultValue)
                : "";
            var variableName = property.Identifier?.TrimStart('$') ?? "";
            // When ShouldSkipEmittingHookedProperty applies, this method is not called. The lowering
            // branch below is a safety net if a hooked property is still emitted without hooks.
            var hooks = this.ShouldLowerPropertyAccessors()
                ? ""
                : this.BuildPropertyHooks(property.Hooks);
            if (this.ShouldLowerPropertyAccessors() && property.Hooks != null)
            {
                this.ReportStrippedPropertyHookAttributes(property.Hooks);
            }

            if (string.IsNullOrEmpty(hooks)
                && !this.ShouldLowerPropertyAccessors()
                && this.NeedsSyntheticGenericSetHook(variableName))
            {
                hooks = this.BuildSyntheticGenericSetHookBlock(variableName);
            }

            // PHP 8.4+: a hook block replaces the trailing semicolon on the property.
            var terminator = string.IsNullOrEmpty(hooks) ? ";" : "";
            return $"{modifiers}{type}${variableName}{defaultValue}{hooks}{terminator}";
        }

        private string BuildPropertyHooks(PhpPropertyHookListAst? hooks)
        {
            if (hooks == null)
            {
                return "";
            }

            var hookTexts = hooks.GetAllNotNull().Select(this.BuildPropertyHook).ToList();
            if (hookTexts.Count == 0)
            {
                return "";
            }

            // Multiline hook blocks (PSR-12 / PHPCS): each hook and each body statement on its own
            // line. Compact single-line hooks trip DisallowMultipleStatements and confuse several
            // brace / type-spacing sniffs.
            var indentedHooks = hookTexts.Select(h => IndentPhpBlock(h, 4));
            return " {\n" + string.Join("\n", indentedHooks) + "\n}";
        }

        private string BuildPropertyHook(PhpPropertyHookAst hook)
        {
            // PHP 8.4+: attributes precede the hook name (`#[Attr] get {}`). Only reached when
            // native hooks emit (ShouldLowerPropertyAccessors is false).
            var attrPrefix = this.FormatInlineAttributes(hook);
            var modifiers = this.FormatModifiers(hook.Modifiers);
            // PHP requires `&get` (ampersand before the hook name), never a trailing `&`.
            var refPrefix = hook.ReturnsRef ? "&" : "";
            // get must not have a parameter list; set may omit params (implicit $value) or declare them.
            // Empty `()` is illegal for both, so only emit parentheses when there are real parameters.
            var paramsText = this.FormatParameterList(hook.Parameters);
            var paramsSuffix = string.IsNullOrEmpty(paramsText) ? "" : $"({paramsText})";
            // Null body = abstract/interface hook (`get;`), matching VisitPropertyHookBody's
            // T_SYM_SEMICOLON → null (same as abstract methods). Do not emit `{}`.
            if (hook.Body is null)
            {
                return $"{attrPrefix}{modifiers}{refPrefix}{hook.Identifier}{paramsSuffix};";
            }

            // Arrow-form hooks (`get/set => expr`) parse as a one-statement `return expr;` block
            // (see PhpPropertyHookAst.IsExpressionBody). A `set` hook is void, so re-emitting that
            // block literally as `set { return expr; }` is a fatal error ("A void method must not
            // return a value"). Reconstruct the original arrow syntax instead of the block form.
            if (hook.IsExpressionBody && TryGetSingleReturnOperand(hook, out var arrowOperand) && arrowOperand != null)
            {
                var exprText = this.BuildExpression(arrowOperand);
                return $"{attrPrefix}{modifiers}{refPrefix}{hook.Identifier}{paramsSuffix} => {exprText};";
            }

            var body = hook.Body is PhpStatementBlockAst block
                ? this.BuildMethodBodyInline(block, compact: false)
                : "{\n}";
            return $"{attrPrefix}{modifiers}{refPrefix}{hook.Identifier}{paramsSuffix} {body}";
        }

        private EmitItem EmitTraitUse(PhpTraitUseAst traitUse, EmitItem parent)
        {
            var traitNames = traitUse.TraitNames?.GetAllNotNull().Select(this.BuildClassName).ToList() ?? [];
            if (traitNames.Count == 0)
            {
                return EmitItem.Empty(traitUse, EmitType.ObjectTraitUse, parent);
            }

            var adaptations = traitUse.Adaptations?.GetAllNotNull().Select(this.BuildTraitAdaptation).ToList()
                ?? [];

            // PSR-12 §4.2: one trait per `use` statement. When adaptations are present with multiple
            // traits, PHP requires those traits to appear together for `insteadof` — keep them on one
            // statement in that case and format the adaptation block multiline.
            if (adaptations.Count == 0)
            {
                EmitItem? last = null;
                foreach (var traitName in traitNames)
                {
                    last = this.ApplyDocComment(
                        traitUse,
                        EmitItem.Line(traitUse, EmitType.ObjectTraitUse, $"use {traitName};", parent));
                }

                return last ?? EmitItem.Empty(traitUse, EmitType.ObjectTraitUse, parent);
            }

            var traits = string.Join(", ", traitNames);
            var lines = new List<string> { $"use {traits} {{" };
            foreach (var adaptation in adaptations)
            {
                lines.Add($"    {adaptation};");
            }

            lines.Add("}");
            return this.ApplyDocComment(
                traitUse,
                EmitItem.MultiLine(traitUse, EmitType.ObjectTraitUse, lines, parent));
        }

        private string BuildTraitAdaptation(ITraitAdaptation adaptation)
            => adaptation switch
            {
                PhpTraitPrecedenceAst precedence =>
                    this.BuildTraitMemberRef(precedence.MethodReference) + "::"
                    + this.BuildClassMemberName(precedence.MethodReference?.MemberName)
                    + " insteadof " + this.FormatClassNameList(precedence.InsteadOfTraits),
                PhpTraitAliasAst alias =>
                    this.BuildTraitMemberRef(alias.MethodReference) + "::"
                    + this.BuildClassMemberName(alias.MethodReference?.MemberName)
                    + " as " + (alias.NewModifier.HasValue ? alias.NewModifier.Value.ToString().ToLowerInvariant() + " " : "")
                    + alias.Identifier,
                _ => "",
            };

        private string BuildClassMemberName(IClassMemberName? memberName)
            => memberName switch
            {
                PhpVariableAst variable => this.BuildExpression(variable),
                TyhpGenericIdentifierAst generic => generic.ValueString ?? "",
                PhpNameAst name => name.ValueString ?? "",
                _ => "",
            };

        private string BuildTraitMemberRef(PhpTraitMemberRefAst? reference)
        {
            if (reference?.TraitName != null)
            {
                return this.BuildClassName(reference.TraitName);
            }

            return "";
        }

        private EmitItem EmitEnumCase(PhpEnumCaseAst enumCase, EmitItem parent)
        {
            var name = enumCase.Name?.ValueString ?? "";
            var line = enumCase.Value != null
                ? $"case {name} = {this.BuildExpression(enumCase.Value)};"
                : $"case {name};";
            var item = this.ApplyDocComment(
                enumCase,
                EmitItem.Line(enumCase, EmitType.ObjectConstantDeclaration, line, parent));
            this.AttachAttributes(enumCase, item);
            return item;
        }

        private EmitItem EmitClassOperatorOverload(TyhpOperatorOverloadAst overload, EmitItem parent)
        {
            // Collected and emitted together (collapsed) after all class members; see
            // EmitCollapsedOperatorMethods in TyhpEmitter.OperatorOverloads.cs.
            this._pendingOperatorOverloads.Add(overload);
            return EmitItem.Empty(overload, EmitType.ObjectInstanceMethods, parent);
        }

        private string BuildFunctionSignature(PhpFunctionDeclAst function)
        {
            var refPrefix = function.ReturnsRef ? "&" : "";
            var name = this.ApplyVariantNaming(function.Identifier);
            var paramsText = this.BuildDeclarationParameterList(function.Parameters);
            var returnType = function.ReturnType != null
                ? ": " + this.BuildTypeExpression(function.ReturnType)
                : "";
            return $"{refPrefix}{name}({paramsText}){returnType}";
        }

        private string BuildAsyncOuterSignature(PhpFunctionDeclAst function)
        {
            this._context.RequirePackage("tyhp/async");
            var refPrefix = function.ReturnsRef ? "&" : "";
            var name = this.ApplyVariantNaming(function.Identifier);
            var paramsText = this.BuildDeclarationParameterList(function.Parameters);
            return $"{refPrefix}{name}({paramsText}): \\Tyhp\\Promise";
        }

        private string BuildAsyncOuterMethodSignature(PhpMethodDeclAst method)
        {
            this._context.RequirePackage("tyhp/async");
            var modifiers = this.FormatModifiers(method.Modifiers);
            // Strip async from emitted modifiers if present in FormatModifiers output — async is Tyhp-only.
            modifiers = System.Text.RegularExpressions.Regex.Replace(modifiers, @"\basync\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            modifiers = string.Join(" ", modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrEmpty(modifiers))
            {
                modifiers += " ";
            }

            modifiers = this.EnsureMethodVisibility(modifiers);
            var refPrefix = method.ReturnsRef ? "&" : "";
            var name = this.ApplyVariantNaming(method.Identifier);
            var paramsText = this.BuildDeclarationParameterList(method.Parameters);
            return $"{modifiers}function {refPrefix}{name}({paramsText}): \\Tyhp\\Promise";
        }

        private void EmitAsyncWrappedBody(PhpFunctionDeclAst function, EmitItem parent, bool captureThis)
        {
            this._context.RequirePackage("tyhp/async");
            var useParts = new List<string>();
            if (function.Parameters != null)
            {
                foreach (var parameter in function.Parameters.GetAllNotNull())
                {
                    if (!string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        useParts.Add(this.EmitParameterVariableName(parameter.Name));
                    }
                }
            }

            _ = captureThis;
            useParts.AddRange(this.BuildVariantCaptureNames());
            var useClause = useParts.Count > 0 ? " use (" + string.Join(", ", useParts) + ")" : "";
            var innerReturn = function.ReturnType != null
                ? ": " + this.BuildTypeExpression(function.ReturnType)
                : "";
            var open = $"return \\Tyhp\\Promise::_async(function (){useClause}{innerReturn} {{";
            var block = EmitItem.Block(function, EmitType.FunctionStatement, open, "});", parent);
            this.EmitFunctionBody(function.Body, block);
        }

        private void EmitAsyncWrappedMethodBody(PhpMethodDeclAst method, EmitItem parent)
        {
            this._context.RequirePackage("tyhp/async");
            // Instance methods: $this is available in the closure; still capture explicit params.
            var useParts = new List<string>();
            if (method.Parameters != null)
            {
                foreach (var parameter in method.Parameters.GetAllNotNull())
                {
                    if (!string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        useParts.Add(parameter.Name);
                    }
                }
            }

            useParts.AddRange(this.BuildVariantCaptureNames());
            var useClause = useParts.Count > 0 ? " use (" + string.Join(", ", useParts) + ")" : "";
            var innerReturn = method.ReturnType != null
                ? ": " + this.BuildTypeExpression(method.ReturnType)
                : "";
            var open = $"return \\Tyhp\\Promise::_async(function (){useClause}{innerReturn} {{";
            var block = EmitItem.Block(method, EmitType.FunctionStatement, open, "});", parent);
            this.EmitFunctionBody(method.Body, block);
        }

        private string BuildMethodSignature(PhpMethodDeclAst method)
        {
            var modifiers = this.EnsureMethodVisibility(this.FormatModifiers(method.Modifiers));
            var refPrefix = method.ReturnsRef ? "&" : "";
            var declaredName = this.StripGenericsFromName(method.Identifier);
            var name = this.ApplyVariantNaming(method.Identifier);
            var paramsText = string.Equals(declaredName, "__construct", StringComparison.OrdinalIgnoreCase)
                ? this.BuildConstructorParameterList(method.Parameters)
                : this.BuildDeclarationParameterList(method.Parameters);

            // PHP rejects return type declarations on __construct / __destruct. Tyhp requires
            // `: void` (or `): parent(...)`) at the source level; erase it for PHP output.
            var omitReturnType = string.Equals(declaredName, "__construct", StringComparison.OrdinalIgnoreCase)
                || string.Equals(declaredName, "__destruct", StringComparison.OrdinalIgnoreCase);
            var returnType = !omitReturnType && method.ReturnType != null
                ? ": " + this.BuildTypeExpression(method.ReturnType)
                : "";
            return $"{modifiers}function {refPrefix}{name}({paramsText}){returnType}";
        }

        /// <summary>
        /// PSR-12 §4.4: visibility MUST be declared on all methods. Tyhp allows omitting it
        /// (PHP defaults to public); emit an explicit <c>public</c>, keeping <c>abstract</c>/<c>final</c>
        /// before visibility per §4.6.
        /// </summary>
        private string EnsureMethodVisibility(string modifiers)
        {
            if (modifiers.Contains("public", StringComparison.Ordinal)
                || modifiers.Contains("protected", StringComparison.Ordinal)
                || modifiers.Contains("private", StringComparison.Ordinal))
            {
                return modifiers;
            }

            if (modifiers.StartsWith("abstract ", StringComparison.Ordinal))
            {
                return "abstract public " + modifiers["abstract ".Length..];
            }

            if (modifiers.StartsWith("final ", StringComparison.Ordinal))
            {
                return "final public " + modifiers["final ".Length..];
            }

            return "public " + modifiers;
        }

        private string FormatParameterList(PhpParameterListAst? parameters)
        {
            if (parameters == null)
            {
                return "";
            }

            var formatted = parameters.GetAllNotNull().Select(p => this.FormatParameter(p)).ToList();
            if (formatted.Count == 0)
            {
                return "";
            }

            // Promoted ctor params with property hooks (or any other multiline parameter text) must
            // break the signature onto multiple lines; a one-line signature with nested `{ … }`
            // bodies confuses PHPCS return-type / statement sniffs.
            if (formatted.Any(p => p.Contains('\n')))
            {
                var inner = string.Join(",\n", formatted.Select(p => IndentPhpBlock(p, 4)));
                return "\n" + inner + "\n";
            }

            return string.Join(", ", formatted);
        }

        /// <summary>
        /// Prefixes every line of <paramref name="text"/> with <paramref name="spaces"/> spaces.
        /// Used when embedding multiline fragments (property hooks) inside a larger declaration.
        /// </summary>
        private static string IndentPhpBlock(string text, int spaces)
        {
            var pad = new string(' ', spaces);
            return string.Join(
                "\n",
                text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => pad + l));
        }

        /// <summary>
        /// Parameter list for a function or method declaration. While emitting a Mechanism D binder
        /// the list is type arguments only (the declared value parameters live on the returned
        /// Closure). Closure and constructor lists have their own builders and are unaffected.
        /// </summary>
        private string BuildDeclarationParameterList(PhpParameterListAst? parameters)
        {
            var hidden = this.BuildVariantHiddenParameters();
            if (hidden.Count > 0)
            {
                // Mechanism D binder: type args only. Value params are on the Closure.
                return string.Join(", ", hidden);
            }

            return this.FormatParameterList(parameters);
        }

        private string FormatParameter(PhpParameterAst parameter, ICheckedType? inferredType = null)
        {
            var parts = new List<string>();
            if (parameter.Modifiers != null)
            {
                var modifiers = this.FormatModifiers(parameter.Modifiers).TrimEnd();
                if (!string.IsNullOrWhiteSpace(modifiers))
                {
                    parts.Add(modifiers);
                }
            }

            if (parameter.Type != null)
            {
                var type = this.BuildTypeExpression(parameter.Type);
                if (!string.IsNullOrWhiteSpace(type))
                {
                    parts.Add(type);
                }
            }
            else if (inferredType is not null)
            {
                var type = this.BuildCheckedTypeExpression(inferredType);
                if (!ShouldOmitInferredPhpTypehint(type))
                {
                    parts.Add(type);
                }
            }

            // The by-reference (`&`) and variadic (`...`) markers must bind directly to the
            // variable name with no intervening whitespace, and ref must precede variadic:
            // e.g. `int &...$args`, `string ...$values`, `int &$out`.
            var refPrefix = parameter.IsRef ? "&" : "";
            var variadicPrefix = parameter.IsVariadic ? "..." : "";
            parts.Add(refPrefix + variadicPrefix + this.EmitParameterVariableName(parameter.Name));

            if (parameter.DefaultValue != null)
            {
                parts.Add("= " + this.BuildExpression(parameter.DefaultValue));
            }

            // PHP 8.4+: promoted ctor params may carry a property-hook block after the name
            // (and optional default). Below 8.4, hooks (author or synthetic free-generic set
            // checks) are lowered to PropertyAccessor registration and promotion is stripped so
            // magic methods own the property.
            //
            // Synthetic free-generic set hooks must only attach to *promoted* parameters. A plain
            // ctor arg that happens to share a name with a free-generic property (e.g.
            // `private T $x; function __construct(T $x)`) is not a property declaration — attaching
            // a hook block would promote it and redeclare the property.
            var paramName = parameter.Name?.TrimStart('$') ?? "";
            var isPromoted = parameter.Modifiers is { } mods && mods.Modifiers.Any();
            if (isPromoted
                && this.ShouldLowerPropertyAccessors()
                && ((parameter.PropertyHooks is PhpPropertyHookListAst hooks
                        && hooks.GetAllNotNull().Any())
                    || this.NeedsSyntheticGenericSetHook(paramName)))
            {
                if (parameter.PropertyHooks is PhpPropertyHookListAst attributedHooks)
                {
                    this.ReportStrippedPropertyHookAttributes(attributedHooks);
                }

                return this.FormatParameterWithoutPromotion(parameter);
            }

            var hooksText = this.BuildPropertyHooks(parameter.PropertyHooks as PhpPropertyHookListAst);
            if (isPromoted
                && string.IsNullOrEmpty(hooksText)
                && this.NeedsSyntheticGenericSetHook(paramName))
            {
                hooksText = this.BuildSyntheticGenericSetHookBlock(paramName);
            }

            return string.Join(" ", parts) + hooksText;
        }

        /// <summary>
        /// Same as <see cref="FormatParameter"/> minus any promotion modifiers, for a signature that
        /// only forwards the parameter on. Promotion belongs to the constructor that declared it;
        /// repeating it on a forwarding signature would redeclare the property.
        /// </summary>
        private string FormatParameterWithoutPromotion(PhpParameterAst parameter)
        {
            var parts = new List<string>();

            if (parameter.Type != null)
            {
                var type = this.BuildTypeExpression(parameter.Type);
                if (!string.IsNullOrWhiteSpace(type))
                {
                    parts.Add(type);
                }
            }

            var refPrefix = parameter.IsRef ? "&" : "";
            var variadicPrefix = parameter.IsVariadic ? "..." : "";
            parts.Add(refPrefix + variadicPrefix + this.EmitParameterVariableName(parameter.Name));

            if (parameter.DefaultValue != null)
            {
                parts.Add("= " + this.BuildExpression(parameter.DefaultValue));
            }

            return string.Join(" ", parts);
        }

        private void EmitFunctionBody(PhpStatementBlockAst? body, EmitItem parent)
        {
            if (body == null)
            {
                return;
            }

            this.EmitBlockContents(body, parent, EmitType.FunctionStatement);
        }

        private string StripGenericsFromName(string? name)
            => name ?? "";
    }
}
