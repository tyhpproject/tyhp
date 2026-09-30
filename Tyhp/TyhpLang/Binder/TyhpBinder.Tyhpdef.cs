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
        /// Loads tyhpdef files and binds their declarations into the GlobalScope.
        /// This must be called before user code binding so that external type information is available.
        /// </summary>
        private void LoadTyhpdefSymbols()
        {
            var registrar = new TyhpdefSymbolRegistrar(this, _diagnostics);
            _tyhpdefRegistrar = registrar;
            try
            {
                registrar.RegisterAll(BuiltIn.Tyhpdef.GetSourceFiles(_diagnostics, _compilationOptions));
            }
            finally
            {
                _tyhpdefRegistrar = null;
            }
        }

        /// <summary>
        /// Binds a single tyhpdef source file with package provenance metadata.
        /// </summary>
        internal void BindTyhpdefSourceFile(TyhpdefSourceFile source)
        {
            _currentTyhpdefPackageSource = source.PackageSource;
            _tyhpdefIsOverlay = source.IsOverlay;
            _overlayReplacedFunctionNames = source.IsOverlay
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : null;
            _overlayPartialKeptNames = source.IsOverlay
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : null;
            _overlayPartialPendingRenames = source.IsOverlay ? [] : null;
            _overlayPartialTypeHeaderNoops = source.IsOverlay ? [] : null;
            try
            {
                BindFile(source.Ast);
                FlushOverlayPartialFunctionRenames();
            }
            finally
            {
                _currentTyhpdefPackageSource = "<tyhpdef>";
                _tyhpdefIsOverlay = false;
                _overlayReplacedFunctionNames = null;
                _overlayPartialKeptNames = null;
                _overlayPartialPendingRenames = null;
                _overlayPartialTypeHeaderNoops = null;
                _tyhpdefOverlayMemberReplace = false;
                _overlayReplacedMemberNames = null;
            }
        }

        private void TrackTyhpdefSymbol(IBaseSymbol symbol)
        {
            _tyhpdefRegistrar?.TrackSymbol(symbol, _currentTyhpdefPackageSource);
        }

        private void ReportTyhpdefDuplicateOrCrossPackage(
            IBaseSymbol? existing,
            BaseSymbol duplicateSymbol,
            IBase2Ast declaringNode)
        {
            if (_tyhpdefRegistrar?.TryReportCrossPackageConflict(
                    existing,
                    duplicateSymbol,
                    declaringNode,
                    _currentTyhpdefPackageSource,
                    _currentFileName) == true)
            {
                return;
            }

            _diagnostics.AddDuplicateFromAst(
                MessageCode.TyhpdefDuplicateDeclaration,
                declaringNode,
                _currentFileName,
                existing,
                duplicateSymbol.Name);
        }

        // Duplicate functions in the same PHP function namespace may merge as overloads.
        // FindChildSymbolByName prefers a class-like over a function of the same name, so
        // failed adds fall back to MatchChild(..., wantFunction: true) when TryAddChildSymbol
        // does not surface the existing function.
        private bool TryRegisterTyhpdefFunction(
            FunctionDeclarationSymbol symbol,
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope targetScope
        )
        {
            switch (targetScope)
            {
                case FileScope fileScope:
                    return TryRegisterTyhpdefFunctionInScope(
                        symbol,
                        funcDecl,
                        fileScope.TryAddChildSymbol(symbol, out var fileExisting),
                        fileExisting ?? MatchChild(fileScope, symbol.Name, wantFunction: true));

                case NamespaceBlockScope nsBlockScope:
                    return TryRegisterTyhpdefFunctionInScope(
                        symbol,
                        funcDecl,
                        nsBlockScope.TryAddChildSymbol(symbol, out var nsExisting),
                        nsExisting ?? MatchChild(nsBlockScope, symbol.Name, wantFunction: true));

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    return false;
            }
        }

        private bool TryRegisterTyhpdefFunctionInScope(
            FunctionDeclarationSymbol symbol,
            TyhpdefImportFunctionDeclAst funcDecl,
            bool added,
            IBaseSymbol? existing)
        {
            if (added)
            {
                TrackTyhpdefSymbol(symbol);
                return true;
            }

            if (TryAddTyhpdefFunctionOverload(existing, symbol))
            {
                return true;
            }

            ReportTyhpdefDuplicateOrCrossPackage(existing, symbol, funcDecl);
            return false;
        }

        private static bool TryAddTyhpdefFunctionOverload(IBaseSymbol? existing, FunctionDeclarationSymbol newSignature)
        {
            if (existing is not FunctionDeclarationSymbol primary
                || primary.IsExtern
                || newSignature.IsExtern)
            {
                return false;
            }

            primary.Overloads.Add(CreateFunctionOverloadSignature(newSignature));
            return true;
        }

        /// <summary>
        /// Overlay last-wins replace removes the harvested member on the first same-name
        /// declaration. Later same-name methods in that overlay body append here instead of
        /// reporting <c>BinderDuplicateSymbolDeclaration</c>, regardless of parameter shape.
        /// Outside overlay replace (a single class body, or an include-layer <c>partial class</c>
        /// merge fragment — both run with <see cref="_tyhpdefDuplicateMemberErrors"/> set), a
        /// same-name method is a genuine PHP overload (docs/content/tyhpdef_overloadedFunctions.md
        /// — e.g. <c>DatePeriod::__construct</c>'s three real signatures) only when its parameter
        /// shape is distinct from every signature already registered under this name **and** it
        /// shares the primary declaration's static/instance-ness (a static and an instance method
        /// cannot share a name in real PHP no matter how their parameters differ); an
        /// identical-shape repeat, or a static/instance mismatch, is a real duplicate and still
        /// falls through to <c>TyhpdefDuplicateDeclaration</c> (matches the existing partial-merge
        /// duplicate-member fixture, which repeats an identical signature).
        /// Not reachable for plain <c>.tyhp</c> source: bodyless overload signatures there are
        /// filtered out before binding by <see cref="OverloadSignatureHelper"/>, so
        /// <see cref="_tyhpdefDuplicateMemberErrors"/> is false and this returns early.
        /// </summary>
        private bool TryAddTyhpdefMethodOverload(ObjectDeclarationScope objScope, ObjectMethodSymbol newSignature)
        {
            if (objScope.DeclarationSymbol is not ObjectDeclarationSymbol decl
                || !decl.Members.TryGetValue(newSignature.Name, out var existing)
                || existing is not ObjectMethodSymbol primary)
            {
                return false;
            }

            if (_tyhpdefOverlayMemberReplace)
            {
                primary.Overloads.Add(newSignature);
                return true;
            }

            if (!_tyhpdefDuplicateMemberErrors
                || primary.IsExtern
                || newSignature.IsExtern
                || newSignature.DeclaringAstNode is null
                || primary.DeclaringAstNode is null
                // A static and an instance method cannot share a name in real PHP even when
                // their parameter shapes differ. Do not let a differing shape paper over that
                // real conflict as a legitimate overload merge.
                || primary.IsStatic != newSignature.IsStatic)
            {
                return false;
            }

            var newShape = PhpVersionGatedCallableSignature.FromDeclaration(newSignature.DeclaringAstNode);
            if (newShape is null)
            {
                return false;
            }

            var primaryShape = PhpVersionGatedCallableSignature.FromDeclaration(primary.DeclaringAstNode);
            if (!PhpVersionGatedCallableSignature.AreDistinctOverloads(primaryShape, newShape))
            {
                return false;
            }

            foreach (var overload in primary.Overloads)
            {
                if (overload.DeclaringAstNode is null)
                {
                    return false;
                }

                var overloadShape = PhpVersionGatedCallableSignature.FromDeclaration(overload.DeclaringAstNode);
                if (!PhpVersionGatedCallableSignature.AreDistinctOverloads(overloadShape, newShape))
                {
                    return false;
                }
            }

            primary.Overloads.Add(newSignature);
            return true;
        }

        private FunctionDeclarationSymbol CloneFunctionForPartialAlias(
            FunctionDeclarationSymbol source,
            string newName)
        {
            var clone = new FunctionDeclarationSymbol(
                newName,
                source.DeclaringAstNode,
                source.SourceFile,
                source.Visibility)
            {
                ReturnType = source.ReturnType,
                IsAsync = source.IsAsync,
                IsDeprecated = source.IsDeprecated,
                DeprecatedMessage = source.DeprecatedMessage,
                IsObsolete = source.IsObsolete,
                IsGenerator = source.IsGenerator,
                OriginalPhpName = string.IsNullOrEmpty(source.OriginalPhpName)
                    ? source.Name
                    : source.OriginalPhpName,
                IsExtern = source.IsExtern,
                ProvidedBy = source.ProvidedBy,
                DocComment = source.DocComment,
                Line = source.Line,
                Column = source.Column,
                EffectivePhpVersionConstraints = source.EffectivePhpVersionConstraints,
            };
            clone.Parameters = [.. source.Parameters];
            if (source.GenericParameters.Count > 0)
            {
                clone.GenericParameters = [.. source.GenericParameters];
            }

            foreach (var overload in source.Overloads)
            {
                clone.Overloads.Add(CloneFunctionForPartialAlias(overload, newName));
            }

            return clone;
        }

        private static FunctionDeclarationSymbol CreateFunctionOverloadSignature(FunctionDeclarationSymbol source)
        {
            var overload = new FunctionDeclarationSymbol(
                source.Name,
                source.DeclaringAstNode,
                source.SourceFile,
                source.Visibility)
            {
                ReturnType = source.ReturnType,
                IsAsync = source.IsAsync,
                IsDeprecated = source.IsDeprecated,
                DeprecatedMessage = source.DeprecatedMessage,
                IsObsolete = source.IsObsolete,
                OriginalPhpName = source.OriginalPhpName,
                IsExtern = source.IsExtern,
                ProvidedBy = source.ProvidedBy,
            };
            overload.Parameters = new List<ParameterInfo>(source.Parameters);
            if (source.GenericParameters.Count > 0)
            {
                overload.GenericParameters = new List<GenericTypeParameterSymbol>(source.GenericParameters);
            }

            return overload;
        }

        private bool TryRegisterTyhpdefTopLevelSymbol(
            BaseSymbol symbol,
            IBase2Ast declaringNode,
            IBaseScope targetScope
        )
        {
            switch (targetScope)
            {
                case FileScope fileScope:
                    if (fileScope.TryAddChildSymbol(symbol, out var fileExisting))
                    {
                        TrackTyhpdefSymbol(symbol);
                        return true;
                    }

                    ReportTyhpdefDuplicateOrCrossPackage(fileExisting, symbol, declaringNode);
                    return false;

                case NamespaceBlockScope nsBlockScope when symbol is INamespaceBlockScopeSymbol nsSymbol:
                    if (nsBlockScope.TryAddChildSymbol(nsSymbol, out var nsExisting))
                    {
                        TrackTyhpdefSymbol(symbol);
                        return true;
                    }

                    ReportTyhpdefDuplicateOrCrossPackage(nsExisting, symbol, declaringNode);
                    return false;

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    return false;
            }
        }

        private void BindTyhpdefObjectDecl(TyhpdefImportObjectDeclAst objDecl, IBaseScope parentScope)
        {
            var (originalName, aliasName) = ExtractTyhpdefName(objDecl.NameOrAlias);
            if (string.IsNullOrEmpty(originalName)) return;

            // PHP placement for the underlying name (namespace of `\Vendor\Long` in
            // `class \Vendor\Long as Short`). The Tyhp-facing symbol name prefers the `as` alias —
            // same pattern as tyhpdef functions (`FunctionDeclarationSymbol` + `OriginalPhpName`)
            // and docs/content/tyhpdef_importAliases.md (only the aliased name is visible in Tyhp).
            var phpTargetScope = ResolveNamespacedScope(originalName, parentScope, out var phpShortName);
            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, phpShortName, StringComparison.OrdinalIgnoreCase);

            string symbolName;
            IBaseScope targetScope;
            string? originalPhpName = null;
            if (hasAlias)
            {
                symbolName = aliasName!;
                // Alias is a Tyhp-facing short name in the current file/namespace, not nested under
                // the PHP namespace. The PHP name is not registered as a class symbol, so
                // `extension PhpName { }` may share that short name (compiled-library
                // backers: `class Name as Name__tyhpExtensionBacker`).
                targetScope = parentScope;
                originalPhpName = originalName.TrimStart('\\');
            }
            else
            {
                symbolName = phpShortName;
                targetScope = phpTargetScope;
            }

            if (TryHandleTyhpdefOverlayKeywords(
                    objDecl,
                    objDecl.IsPartial,
                    objDecl.IsOmit,
                    objDecl.IsDeprecated,
                    objDecl.IsObsolete,
                    objDecl.IsExtern))
            {
                return;
            }

            if (TryRejectIllegalTyhpdefExtern(objDecl, hasAlias, originalName))
            {
                return;
            }

            if (_tyhpdefIsOverlay && ShouldSkipPhpVersionGatedOverlay(objDecl, targetScope))
            {
                return;
            }

            if (objDecl.IsOmit)
            {
                TryOmitTyhpdefSymbol(objDecl, targetScope, symbolName, wantFunction: false);
                return;
            }

            if (objDecl.IsPartial)
            {
                if (objDecl.IsHeaderOnly && !_tyhpdefIsOverlay)
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefPartialTypeHeaderOutsideOverlay,
                        objDecl,
                        _currentFileName,
                        objDecl.DeclType?.ValueString ?? "class");
                    return;
                }

                if (_tyhpdefIsOverlay)
                {
                    BindTyhpdefOverlayPartialObject(
                        objDecl,
                        phpTargetScope,
                        phpShortName,
                        hasAlias ? aliasName : null);
                }
                else
                {
                    BindTyhpdefPartialObject(objDecl, targetScope, symbolName);
                }

                return;
            }

            // `as`-aliased full replace merges/evicts under the Tyhp alias name (`symbolName` in
            // `targetScope`), which is almost never the Layer 1 baseline's name. Look the PHP
            // original up by its own short name in its own namespace scope — before the extern
            // merge below can evict anything — so an unqualified original (`class Bar as Baz`
            // inside `namespace Foo`) still keys the stamp lookup off `\Foo\Bar`, not the alias.
            var phpOriginalBaseline = hasAlias
                ? FindExistingObjectType(phpTargetScope, phpShortName)
                : null;

            if (TryConsumeTyhpdefExternMerge(objDecl, targetScope, symbolName, out var existing, out var realWinsReplaced))
            {
                return;
            }

            if (_tyhpdefIsOverlay)
            {
                ReportOverlayStampAndCompatibility(
                    objDecl,
                    existing,
                    symbolName,
                    phpOriginalBaseline: phpOriginalBaseline);
                if (existing != null && !realWinsReplaced)
                {
                    EvictPhpVersionGatedSymbol(targetScope, existing);
                    RemoveTyhpdefSymbol(existing);
                }
            }

            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    objDecl,
                    targetScope,
                    symbolName,
                    SymbolType.ObjectTypeDeclaration,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var modifiers = ConvertModifiers(objDecl.Modifiers);
            var symbol = new ObjectDeclarationSymbol(symbolName, objDecl, _currentFileName, modifiers);
            StampPhpVersionConstraints(symbol, phpConstraints);
            symbol.OriginalPhpName = originalPhpName;

            // Class-level generic parameters (e.g. `class Foo<TValue>`) must be registered so
            // member signatures can resolve references to those type parameters. Depending on the
            // declaration kind, the tyhpdef visitor exposes them either as a "GenericParameters"
            // grammar addon on the name (class declarations) or as the GenericArguments child of a
            // TyhpGenericIdentifierAst name (trait/interface/enum declarations).
            var classGenericList = ExtractTyhpdefObjectGenericList(objDecl.NameOrAlias);
            if (classGenericList != null)
            {
                PopulateGenericParameters(
                    classGenericList,
                    symbol.GenericParameters,
                    _currentFileName,
                    SymbolType.ClassGenericTypeParameter);
            }

            symbol.ObjectKind = ParseTyhpdefObjectKind(objDecl);

            symbol.ExtendsType = objDecl.Extends as ITypeExpression
                ?? (objDecl.Extends is IExpression extendsName
                    ? PhpNamedTypeAst.WrapClassName(extendsName, objDecl)
                    : null);
            symbol.BackingType = objDecl.BackingType;
            if (objDecl.Implements != null)
            {
                foreach (var impl in objDecl.Implements.GetAllNotNull())
                {
                    var typeExpr = AsTypeExpression(impl, objDecl);
                    if (typeExpr is not null)
                    {
                        symbol.ImplementsTypes.Add(typeExpr);
                    }
                }
            }

            if (objDecl.IsDeprecated)
            {
                symbol.IsDeprecated = true;
            }
            symbol.IsObsolete = objDecl.IsObsolete;
            symbol.IsExtern = objDecl.IsExtern;
            symbol.ProvidedBy = objDecl.ProvidedBy;

            // TYHP4171 is for a user/PHP type whose Tyhp name is the reserved suffix. The
            // legitimate backer alias (`class Name as Name__tyhpExtensionBacker`) must not trip
            // it — check the PHP name, not the Tyhp-facing alias.
            ReportReservedExtensionBackerName(phpShortName, objDecl);

            switch (targetScope)
            {
                case FileScope fileScope:
                {
                    if (!TryRegisterTyhpdefTopLevelSymbol(symbol, objDecl, fileScope))
                    {
                        return;
                    }

                    var objScope = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScope);
                    BindTyhpdefObjectBody(objDecl.Body, objScope, symbol);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!TryRegisterTyhpdefTopLevelSymbol(symbol, objDecl, nsBlockScope))
                    {
                        return;
                    }

                    var objScope = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScope);
                    BindTyhpdefObjectBody(objDecl.Body, objScope, symbol);
                    break;
                }

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    break;
            }

            // Class aliases are the ObjectDeclarationSymbol itself (above). Do not also
            // CreateTyhpdefAlias — UseIncludeSymbol under the same name would collide, and
            // SearchGlobalNamespace skips UseIncludeSymbol so that path never resolved types.
        }

        private void BindTyhpdefPartialObject(
            TyhpdefImportObjectDeclAst objDecl,
            IBaseScope targetScope,
            string shortName)
        {
            var existing = FindExistingObjectType(targetScope, shortName);
            if (existing == null || existing.IsExtension)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefPartialTargetNotFound,
                    objDecl,
                    _currentFileName,
                    shortName);
                return;
            }

            if (existing.IsExtern)
            {
                ReportPartialOnExtern(objDecl, shortName);
                return;
            }

            var objScope = FindObjectDeclarationScope(existing);
            if (objScope == null)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefPartialTargetNotFound,
                    objDecl,
                    _currentFileName,
                    shortName);
                return;
            }

            // Include-load partial fragments are always brace-form (header-only
            // `partial enum Foo: int;` is overlay-only — TyhpdefPartialTypeHeaderOutsideOverlay),
            // but the grammar still lets a fragment declare the enum backing type alongside its
            // cases (`partial enum Foo: int { case A = 1; }`). Apply it to the merged symbol so it
            // is not silently dropped, matching the primary-declaration path in this method's caller.
            if (objDecl.BackingType != null)
            {
                existing.BackingType = objDecl.BackingType;
            }

            var previous = _tyhpdefDuplicateMemberErrors;
            _tyhpdefDuplicateMemberErrors = true;
            try
            {
                BindTyhpdefObjectBody(objDecl.Body, objScope, existing);
            }
            finally
            {
                _tyhpdefDuplicateMemberErrors = previous;
            }
        }

        /// <summary>
        /// Finds an existing object type in the same PHP symbol namespace as
        /// <paramref name="targetScope"/>. Tyhpdef files each get their own
        /// <see cref="FileScope"/> / <see cref="NamespaceBlockScope"/>, so overlay
        /// <c>partial class</c> (and last-wins replace) must look at sibling scopes —
        /// un-namespaced types on sibling files, namespaced types on sibling blocks
        /// under the same <see cref="NamespaceScope"/> — not only the current contribution.
        /// </summary>
        private ObjectDeclarationSymbol? FindExistingObjectType(IBaseScope targetScope, string shortName)
        {
            foreach (var scope in EnumerateSamePhpNamespaceScopes(targetScope))
            {
                if (scope.FindChildSymbolByName(shortName) is ObjectDeclarationSymbol hit
                    && !hit.IsExtension)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>
        /// Yields <paramref name="targetScope"/> plus every sibling scope that shares its PHP
        /// symbol namespace: other <see cref="FileScope"/>s for un-namespaced declarations,
        /// or other <see cref="NamespaceBlockScope"/>s under the same <see cref="NamespaceScope"/>.
        /// </summary>
        private IEnumerable<IBaseScope> EnumerateSamePhpNamespaceScopes(IBaseScope targetScope)
        {
            yield return targetScope;

            if (targetScope is FileScope)
            {
                foreach (var sibling in _globalScope.ChildScopes)
                {
                    if (sibling is FileScope fileScope && !ReferenceEquals(fileScope, targetScope))
                    {
                        yield return fileScope;
                    }
                }

                yield break;
            }

            if (targetScope is NamespaceBlockScope nsBlock)
            {
                var nsParent = nsBlock.Parent;
                if (nsParent == null)
                {
                    yield break;
                }

                foreach (var sibling in nsParent.ChildScopes)
                {
                    if (sibling is NamespaceBlockScope otherBlock
                        && !ReferenceEquals(otherBlock, nsBlock))
                    {
                        yield return otherBlock;
                    }
                }
            }
        }

        private static ObjectDeclarationScope? FindObjectDeclarationScope(ObjectDeclarationSymbol symbol)
        {
            if (symbol.ContainingScope is ObjectDeclarationScope direct)
            {
                return direct;
            }

            var parent = symbol.ContainingScope;
            if (parent == null)
            {
                return null;
            }

            foreach (var child in parent.GetAllChildScopes())
            {
                if (child is ObjectDeclarationScope objScope
                    && ReferenceEquals(objScope.DeclarationSymbol, symbol))
                {
                    return objScope;
                }
            }

            return null;
        }

        private void BindTyhpdefFunctionDecl(TyhpdefImportFunctionDeclAst funcDecl, IBaseScope parentScope)
        {
            var (originalName, aliasName) = ExtractTyhpdefName(funcDecl.NameOrAlias);
            if (string.IsNullOrEmpty(originalName)) return;

            // PHP placement for the underlying name (namespace of `\App\foo` in
            // `function \App\foo as bar`). The Tyhp-facing symbol name prefers the `as` alias —
            // same pattern as tyhpdef methods (`ObjectMethodSymbol` + `OriginalPhpName`) and
            // docs/content/tyhpdef_importAliases.md (only the aliased name is visible in Tyhp).
            var phpTargetScope = ResolveNamespacedScope(originalName, parentScope, out var phpShortName);
            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, phpShortName, StringComparison.OrdinalIgnoreCase);

            string symbolName;
            IBaseScope targetScope;
            string? originalPhpName = null;
            if (hasAlias)
            {
                symbolName = aliasName!;
                // Alias is a Tyhp-global short name (file scope), not nested under the PHP namespace.
                targetScope = parentScope;
                originalPhpName = originalName.TrimStart('\\');
            }
            else
            {
                symbolName = phpShortName;
                targetScope = phpTargetScope;
            }

            if (TryHandleTyhpdefOverlayKeywords(
                    funcDecl,
                    funcDecl.IsPartial,
                    funcDecl.IsOmit,
                    funcDecl.IsDeprecated,
                    funcDecl.IsObsolete,
                    funcDecl.IsExtern,
                    funcDecl.IsFallback))
            {
                return;
            }

            if (TryRejectIllegalTyhpdefExternFunction(funcDecl, hasAlias, originalName))
            {
                return;
            }

            if (_tyhpdefIsOverlay && ShouldSkipPhpVersionGatedOverlay(funcDecl, targetScope))
            {
                return;
            }

            if (funcDecl.IsPartial)
            {
                BindOverlayPartialFunction(
                    funcDecl,
                    phpTargetScope,
                    phpShortName,
                    aliasName,
                    parentScope);
                return;
            }

            if (funcDecl.IsOmit)
            {
                TryOmitTyhpdefSymbol(funcDecl, targetScope, symbolName, wantFunction: true);
                return;
            }

            if (TryConsumeTyhpdefExternFunctionMerge(
                    funcDecl,
                    targetScope,
                    symbolName,
                    out var existingExternFunction,
                    out var realWinsReplaced))
            {
                return;
            }

            if (_tyhpdefIsOverlay && !funcDecl.IsExtern)
            {
                TryBeginOverlayFunctionReplace(
                    funcDecl,
                    targetScope,
                    symbolName,
                    hasAlias,
                    phpTargetScope,
                    phpShortName,
                    existingExternFunction,
                    realWinsReplaced);
            }

            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    funcDecl,
                    targetScope,
                    symbolName,
                    SymbolType.FunctionDeclaration,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var modifiers = ConvertModifiers(null);
            var symbol = new FunctionDeclarationSymbol(symbolName, funcDecl, _currentFileName, modifiers);
            StampPhpVersionConstraints(symbol, phpConstraints);

            symbol.ReturnType = funcDecl.ReturnType;
            symbol.IsAsync = funcDecl.IsAsync;
            if (funcDecl.IsDeprecated)
            {
                symbol.IsDeprecated = true;
            }
            symbol.IsObsolete = funcDecl.IsObsolete;
            symbol.IsExtern = funcDecl.IsExtern;
            symbol.IsFallback = funcDecl.IsFallback;
            symbol.ProvidedBy = funcDecl.ProvidedBy;
            symbol.OriginalPhpName = originalPhpName;

            if (funcDecl.NameOrAlias?.AstGrammarAddons.TryGetValue("GenericArguments", out var genericAddon) == true
                && genericAddon is TyhpGenericsTypeArgumentListAst genericList)
            {
                PopulateGenericParameters(
                    genericList,
                    symbol.GenericParameters,
                    _currentFileName,
                    SymbolType.FunctionGenericTypeParameter);
            }

            if (funcDecl.Parameters != null)
            {
                foreach (var param in funcDecl.Parameters.GetAllNotNull())
                {
                    var paramModifiers = ConvertModifiers(param.Modifiers);
                    symbol.Parameters.Add(new ParameterInfo(
                        param.ValueString ?? "",
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers
                    ));
                }
            }

            if (funcDecl.IsFallback)
            {
                DeferFallbackFunction(symbol, funcDecl, targetScope);
                return;
            }

            TryRegisterTyhpdefFunction(symbol, funcDecl, targetScope);

            // Function aliases are the FunctionDeclarationSymbol itself (above). Do not also
            // CreateTyhpdefAlias — UseIncludeSymbol under the same name would collide, and
            // SearchGlobalNamespace skips UseIncludeSymbol so that path never resolved calls.
        }

        private void BindTyhpdefConstDecl(TyhpdefImportConstAst constDecl, IBaseScope parentScope)
        {
            var (originalName, aliasName) = ExtractTyhpdefName(constDecl.NameOrAlias);
            if (string.IsNullOrEmpty(originalName)) return;

            var targetScope = ResolveNamespacedScope(originalName, parentScope, out var shortName);

            if (TryHandleTyhpdefOverlayKeywords(
                    constDecl,
                    isPartial: false,
                    constDecl.IsOmit,
                    constDecl.IsDeprecated,
                    constDecl.IsObsolete,
                    constDecl.IsExtern,
                    constDecl.IsFallback))
            {
                return;
            }

            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, shortName, StringComparison.Ordinal);
            if (TryRejectIllegalTyhpdefExternConst(constDecl, hasAlias, originalName))
            {
                return;
            }

            if (_tyhpdefIsOverlay && ShouldSkipPhpVersionGatedOverlay(constDecl, targetScope))
            {
                return;
            }

            if (constDecl.IsOmit)
            {
                TryOmitTyhpdefSymbol(constDecl, targetScope, shortName, wantFunction: false);
                return;
            }

            if (TryConsumeTyhpdefExternConstMerge(
                    constDecl,
                    targetScope,
                    shortName,
                    out var existingExternConst,
                    out var realWinsReplaced))
            {
                return;
            }

            if (_tyhpdefIsOverlay && !constDecl.IsExtern)
            {
                var existing = existingExternConst
                    ?? FindExistingConstant(targetScope, shortName);
                ReportOverlayStampAndCompatibility(constDecl, existing, shortName);
                if (existing != null && !realWinsReplaced)
                {
                    EvictPhpVersionGatedSymbol(targetScope, existing);
                    RemoveTyhpdefSymbol(existing);
                }
            }

            var symbol = new ConstantSymbol(shortName, sourceFile: _currentFileName, declaringNode: constDecl);
            symbol.DeclaredType = constDecl.TypeExpr;
            symbol.ValueExpression = constDecl.CoalesceExpr;
            if (constDecl.IsDeprecated)
            {
                symbol.IsDeprecated = true;
            }
            symbol.IsObsolete = constDecl.IsObsolete;
            symbol.IsExtern = constDecl.IsExtern;
            symbol.IsFallback = constDecl.IsFallback;
            symbol.ProvidedBy = constDecl.ProvidedBy;

            if (constDecl.IsFallback)
            {
                DeferFallbackConstant(symbol, constDecl, targetScope);
                return;
            }

            TryRegisterTyhpdefTopLevelSymbol(symbol, constDecl, targetScope);

            if (!string.IsNullOrEmpty(aliasName))
            {
                CreateTyhpdefAlias(aliasName, originalName, constDecl, parentScope, PhpUseType.Const);
            }
        }

        private void BindTyhpdefVariableDecl(TyhpdefImportVariableAst varDecl, IBaseScope parentScope)
        {
            var variableName = varDecl.VariableName;
            if (string.IsNullOrEmpty(variableName)) return;

            if (TryHandleTyhpdefOverlayKeywords(
                    varDecl,
                    isPartial: false,
                    varDecl.IsOmit,
                    varDecl.IsDeprecated,
                    varDecl.IsObsolete))
            {
                return;
            }

            if (_tyhpdefIsOverlay && ShouldSkipPhpVersionGatedOverlay(varDecl, parentScope))
            {
                return;
            }

            if (varDecl.IsOmit)
            {
                TryOmitTyhpdefSymbol(varDecl, parentScope, variableName, wantFunction: false);
                return;
            }

            if (_tyhpdefIsOverlay)
            {
                var existing = FindExistingTyhpdefSymbol(parentScope, variableName, wantFunction: false);
                ReportOverlayStampAndCompatibility(varDecl, existing, variableName);
                if (existing != null)
                {
                    EvictPhpVersionGatedSymbol(parentScope, existing);
                    RemoveTyhpdefSymbol(existing);
                }
            }

            // PHP variables (superglobals like $_SERVER, $_GET, etc.) are never namespaced,
            // so namespace resolution is intentionally skipped for tyhpdef variable declarations.
            var symbol = new VariableSymbol(
                variableName,
                declaringNode: varDecl,
                sourceFile: _currentFileName);
            symbol.DeclaredType = varDecl.TypeExpr;
            if (varDecl.IsDeprecated)
            {
                symbol.IsDeprecated = true;
            }
            symbol.IsObsolete = varDecl.IsObsolete;

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!fileScope.TryAddChildSymbol(symbol, out var fileExisting))
                    {
                        _diagnostics.AddDuplicateFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            varDecl,
                            _currentFileName,
                            fileExisting,
                            symbol.Name);
                    }
                    break;

                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var nsExisting))
                    {
                        _diagnostics.AddDuplicateFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            varDecl,
                            _currentFileName,
                            nsExisting,
                            symbol.Name);
                    }
                    break;

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    break;
            }

            if (!string.IsNullOrEmpty(varDecl.AliasedAs))
            {
                CreateTyhpdefAlias(varDecl.AliasedAs, variableName, varDecl, parentScope, PhpUseType.Variable);
            }
        }

        /// <summary>
        /// Binds tyhpdef object body members into the object scope.
        /// Mirrors <see cref="BindObjectBody"/> but works directly with <see cref="PhpClassBodyAst"/>.
        /// </summary>
        private void BindTyhpdefObjectBody(PhpClassBodyAst? body, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol)
        {
            var previous = _tyhpdefDuplicateMemberErrors;
            _tyhpdefDuplicateMemberErrors = true;
            try
            {
                BindTyhpdefObjectBodyCore(body, objScope, symbol);
            }
            finally
            {
                _tyhpdefDuplicateMemberErrors = previous;
            }
        }

        private void BindTyhpdefObjectBodyCore(PhpClassBodyAst? body, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol)
        {
            Types.PopulateObject(objScope);

            if (body == null) return;

            var members = body.GetAllNotNull().ToList();

            foreach (var member in members)
            {
                switch (member)
                {
                    case TyhpdefInlineExtensionFunctionAst:
                    case TyhpOperatorOverloadAst opSkip when opSkip.IsInlineExtension:
                        continue;

                    case PhpMethodDeclAst methodDecl:
                        if (_tyhpdefIsOverlay
                            && ShouldSkipPhpVersionGatedOverlay(methodDecl, objScope))
                        {
                            break;
                        }

                        if (methodDecl.IsPartial)
                        {
                            BindOverlayPartialFunctionMember(methodDecl, objScope);
                            break;
                        }

                        if (TryHandleOverlayMember(methodDecl, objScope, GetTyhpdefMethodBindName(methodDecl)))
                        {
                            break;
                        }

                        BindMethodDecl(methodDecl, objScope);
                        break;

                    case PhpPropertyDeclAst propDecl:
                        if (_tyhpdefIsOverlay
                            && ShouldSkipPhpVersionGatedOverlay(propDecl, objScope))
                        {
                            break;
                        }

                        if (TryHandleOverlayMember(propDecl, objScope, FirstPropertyName(propDecl)))
                        {
                            break;
                        }

                        BindPropertyDecl(propDecl, objScope);
                        break;

                    case PhpConstDeclListAst constList:
                        BindObjectConstDecl(constList, objScope);
                        break;

                    case TyhpdefImportConstDeclListAst tyhpdefConstList:
                        if (_tyhpdefOverlayMemberReplace && IsMemberOmit(tyhpdefConstList))
                        {
                            foreach (var constDecl in tyhpdefConstList.GetAllNotNull())
                            {
                                var (originalName, aliasName) = ExtractTyhpdefName(constDecl.AliasedIdentifier);
                                var constName = aliasName ?? originalName;
                                if (!string.IsNullOrEmpty(constName))
                                {
                                    RemoveObjectMember(objScope, constName, isConstant: true);
                                }
                            }

                            break;
                        }

                        BindTyhpdefImportObjectConstDecl(tyhpdefConstList, objScope);
                        break;

                    case PhpEnumCaseAst enumCase:
                        BindEnumCase(enumCase, objScope, symbol);
                        break;

                    case TyhpOperatorOverloadAst opOverload:
                        BindOperatorOverload(opOverload, objScope);
                        break;

                    case TyhpTypeAliasAst typeAlias:
                    {
                        if (typeAlias.StructShape is { } structShape)
                        {
                            BindStructShapeAlias(typeAlias, structShape, objScope);
                            break;
                        }

                        var tyhpdefAliasName = typeAlias.Name?.ValueString ?? typeAlias.Identifier ?? "";
                        if (!string.IsNullOrEmpty(tyhpdefAliasName))
                        {
                            var modifiers = ConvertModifiers(typeAlias.Modifiers);
                            var aliasSymbol = new ObjectTypeAliasSymbol(
                                tyhpdefAliasName,
                                declaringNode: typeAlias,
                                sourceFile: _currentFileName,
                                visibility: modifiers);
                            aliasSymbol.AliasedType = typeAlias.TypeExpression;
                            ValidateObjectShapeAlias(typeAlias);
                            if (typeAlias.GenericArguments != null)
                            {
                                PopulateGenericParameters(
                                    typeAlias.GenericArguments,
                                    aliasSymbol.GenericParameters,
                                    _currentFileName,
                                    SymbolType.ClassGenericTypeParameter);
                            }

                            if (!objScope.TryAddChildSymbol(aliasSymbol, out var existingAlias))
                            {
                                _diagnostics.AddDuplicateFromAst(
                                    MessageCode.TyhpdefDuplicateDeclaration,
                                    typeAlias,
                                    _currentFileName,
                                    existingAlias,
                                    aliasSymbol.Name);
                            }
                            else
                            {
                                RegisterObjectMember(objScope, aliasSymbol, tyhpdefAliasName);
                            }
                        }
                        break;
                    }

                    case PhpTraitUseAst traitUse:
                        BindTraitUseBlock(traitUse, symbol);
                        break;

                    case TyhpImportExtensionAst useExt:
                        BindTyhpdefClassUseExtension(useExt, symbol);
                        break;

                    case UnexpectedNodeAst:
                        // Intentionally skipped: unexpected nodes in tyhpdef bodies are non-fatal
                        break;
                    case ErrorAst errorAst:
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefInvalidFormat,
                            errorAst,
                            _currentFileName,
                            "Error node encountered in tyhpdef object body");
                        break;
                }
            }

            foreach (var member in members)
            {
                switch (member)
                {
                    case TyhpdefInlineExtensionFunctionAst inlineFx:
                        BindTyhpdefInlineExtensionFunction(inlineFx, objScope, symbol);
                        break;

                    case TyhpOperatorOverloadAst opInline when opInline.IsInlineExtension:
                        BindOperatorOverload(opInline, objScope);
                        break;
                }
            }
        }

        /// <summary>
        /// Binds a tyhpdef class constant list (<c>const int ROUND_DOWN ?? 102, …;</c>) into the
        /// object scope. Unlike <see cref="BindObjectConstDecl"/>'s <see cref="PhpConstDeclAst"/>
        /// (type/modifiers per item), <see cref="TyhpdefImportConstDeclListAst"/> hangs a single
        /// shared type/modifiers pair off the list itself (see
        /// <c>VisitTyhpdefImportClassConst</c>) — without this, extension class constants (e.g.
        /// <c>\Decimal\Decimal::ROUND_DOWN</c>) had no <see cref="ObjectConstantSymbol"/> at all and
        /// resolved as bare <c>mixed</c> at every use site.
        /// </summary>
        private void BindTyhpdefImportObjectConstDecl(
            TyhpdefImportConstDeclListAst constList, ObjectDeclarationScope objScope)
        {
            var visibility = constList.AstGrammarAddons.TryGetValue("modifiers", out var modifiersAddon)
                && modifiersAddon is PhpModifierListAst modifiers
                    ? ConvertModifiers(modifiers)
                    : MemberModifier.None;
            var declaredType = constList.AstGrammarAddons.TryGetValue("typeExpr", out var typeAddon)
                ? typeAddon as ITypeExpression
                : null;

            foreach (var constDecl in constList.GetAllNotNull())
            {
                var (originalName, aliasName) = ExtractTyhpdefName(constDecl.AliasedIdentifier);
                var phpName = ShortConstName(originalName);
                if (string.IsNullOrEmpty(phpName))
                {
                    phpName = aliasName ?? "";
                }

                var hasAlias = !string.IsNullOrEmpty(aliasName)
                    && !string.Equals(aliasName, phpName, StringComparison.Ordinal);
                var name = hasAlias ? aliasName! : phpName;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (!ShouldRegisterPhpVersionGatedDeclaration(
                        constDecl,
                        objScope,
                        name,
                        SymbolType.ObjectConstant,
                        illegalAttributeTarget: false,
                        out var phpConstraints,
                        constList))
                {
                    continue;
                }

                var constSymbol = new ObjectConstantSymbol(
                    name,
                    sourceFile: _currentFileName,
                    declaringNode: constDecl,
                    visibility: visibility)
                {
                    DeclaredType = declaredType,
                    ValueExpression = constDecl.CoalesceExpr,
                    OriginalPhpName = hasAlias ? phpName : null,
                };
                StampPhpVersionConstraints(constSymbol, phpConstraints);
                EngineDeprecatedAttribute.Apply(constSymbol, constList);

                if (_tyhpdefOverlayMemberReplace)
                {
                    // Evict the PHP spelling so `const IS as IS_OP` replaces `IS` instead of
                    // leaving both names on the class.
                    TryHandleOverlayMember(constDecl, objScope, hasAlias ? phpName : name);
                }

                if (!objScope.TryAddChildSymbol(constSymbol, out var existingConst))
                {
                    _diagnostics.AddDuplicateFromAst(
                        MessageCode.TyhpdefDuplicateDeclaration,
                        constDecl,
                        _currentFileName,
                        existingConst,
                        constSymbol.Name);
                }
                else
                {
                    RegisterObjectMember(objScope, constSymbol, name);
                }
            }
        }

        /// <summary>
        /// Extracts the generic parameter declaration list from a tyhpdef object's name AST.
        /// Class declarations attach the list under the "GenericParameters" grammar addon, whereas
        /// trait/interface/enum declarations carry it as the GenericArguments child of a
        /// <see cref="TyhpGenericIdentifierAst"/>.
        /// </summary>
        private static TyhpGenericsTypeArgumentListAst? ExtractTyhpdefObjectGenericList(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias == null) return null;

            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericParameters", out var addon) &&
                addon is TyhpGenericsTypeArgumentListAst addonList)
            {
                return addonList;
            }

            if (nameOrAlias is TyhpGenericIdentifierAst genericId &&
                genericId.GenericArguments is TyhpGenericsTypeArgumentListAst childList)
            {
                return childList;
            }

            return null;
        }

        private static (string originalName, string? aliasName) ExtractTyhpdefName(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias is TyhpdefIdentifierAliasAst aliasNode)
            {
                var originalName = aliasNode.ValueString ?? "";
                var alias = aliasNode.Identifier ?? "";
                return (originalName, string.IsNullOrEmpty(alias) ? null : alias);
            }

            // Function / method names: `function original as alias` — visitor stores a plain
            // PhpNameAst for `original` and hangs the Tyhp-facing name on the `aliasedAs` addon.
            if (nameOrAlias?.AstGrammarAddons.TryGetValue("aliasedAs", out var aliasedAsNode) == true)
            {
                var original = GetAstNameText(nameOrAlias);
                var alias = GetAstNameText(aliasedAsNode);
                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(alias))
                {
                    return (original, alias);
                }
            }

            // Class names: `class \Vendor\Long as Short` — visitor stores a PhpNameAst for `Short`
            // and hangs the original PHP name on the `aliasOf` addon.
            if (nameOrAlias?.AstGrammarAddons.TryGetValue("aliasOf", out var aliasOfNode) == true)
            {
                var alias = GetAstNameText(nameOrAlias);
                var original = GetAstNameText(aliasOfNode);
                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(alias))
                {
                    return (original, alias);
                }
            }

            // Identifier defaults to "" (never null) on Base2Ast, while name AST nodes such as
            // PhpNameAst carry the actual name in ValueString. A null-coalesce on Identifier would
            // stop at the empty string and never reach ValueString, so check for emptiness instead.
            return (GetAstNameText(nameOrAlias), null);
        }

        private static string ShortConstName(string name)
        {
            var trimmed = name.TrimStart('\\');
            var separator = trimmed.LastIndexOf('\\');
            return separator < 0 ? trimmed : trimmed[(separator + 1)..];
        }

        private static string GetAstNameText(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(node.Identifier))
            {
                return node.Identifier;
            }

            return node.ValueString ?? "";
        }

        /// <summary>
        /// If the name contains namespace separators, splits off the namespace part and
        /// returns a <see cref="NamespaceBlockScope"/> for it. Otherwise returns the parent scope unchanged.
        /// </summary>
        private IBaseScope ResolveNamespacedScope(string name, IBaseScope parentScope, out string shortName)
        {
            var lastSep = name.LastIndexOf('\\');
            // `\array_map` has a leading separator only — that is the global namespace, not a
            // nested namespace named by the empty prefix.
            if (lastSep <= 0)
            {
                shortName = name.TrimStart('\\');
                return parentScope;
            }

            var namespacePart = name[..lastSep].TrimStart('\\');
            shortName = name[(lastSep + 1)..];

            if (string.IsNullOrEmpty(shortName))
            {
                _diagnostics.AddError(
                    MessageCode.TyhpdefInvalidFormat,
                    _currentFileName,
                    0, 0,
                    _currentFileName);
                shortName = name;
                return parentScope;
            }

            var nsScope = _globalScope.AddNamespaceScope(namespacePart);
            return GetOrCreateNamespaceBlockScope(nsScope, namespacePart);
        }

        private readonly Dictionary<NamespaceScope, NamespaceBlockScope> _tyhpdefNamespaceBlockScopes = new();

        /// <summary>
        /// Returns an existing <see cref="NamespaceBlockScope"/> within the given namespace scope,
        /// or creates a new one if none exists. Unlike user-code binding (which creates a new
        /// <see cref="NamespaceBlockScope"/> per file), tyhpdef loading intentionally shares a single
        /// block scope across all tyhpdef files declaring into the same namespace. This consolidates
        /// tyhpdef symbols into one scope per namespace for efficient lookup during name resolution.
        /// </summary>
        private NamespaceBlockScope GetOrCreateNamespaceBlockScope(NamespaceScope nsScope, string namespaceName)
        {
            if (_tyhpdefNamespaceBlockScopes.TryGetValue(nsScope, out var cached))
                return cached;

            var existing = nsScope.ChildScopes.OfType<NamespaceBlockScope>().FirstOrDefault();
            if (existing != null)
            {
                _tyhpdefNamespaceBlockScopes[nsScope] = existing;
                return existing;
            }

            var blockSymbol = new NamespaceBlockSymbol(namespaceName, _currentFileScope);
            var blockScope = new NamespaceBlockScope(nsScope, blockSymbol);
            nsScope.AddChildScope(blockScope);
            _tyhpdefNamespaceBlockScopes[nsScope] = blockScope;
            return blockScope;
        }

        private void CreateTyhpdefAlias(string aliasName, string originalName, IBase2Ast declaringNode, IBaseScope parentScope, PhpUseType useType)
        {
            var useSymbol = new UseIncludeSymbol(
                aliasName,
                originalName,
                declaringNode,
                sourceFile: _currentFileName,
                useType: useType
            );

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!fileScope.TryAddChildSymbol(useSymbol, out var fileExisting))
                    {
                        _diagnostics.AddDuplicateFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            declaringNode,
                            _currentFileName,
                            fileExisting,
                            aliasName);
                    }
                    break;
                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.TryAddChildSymbol(useSymbol, out var nsExisting))
                    {
                        _diagnostics.AddDuplicateFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            declaringNode,
                            _currentFileName,
                            nsExisting,
                            aliasName);
                    }
                    break;
                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    break;
            }
        }
    }
}
