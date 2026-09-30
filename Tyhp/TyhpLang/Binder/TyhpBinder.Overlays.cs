using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        internal void CaptureTyhpdefLayer1Stamps()
        {
            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_globalScope != null)
            {
                CaptureStamps(_globalScope, stamps);
            }

            _tyhpdefLayer1Stamps = stamps;
        }

        private static void CaptureStamps(IBaseScope scope, Dictionary<string, string> stamps)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                // Methods / properties / object constants share binder FQNs like `\__construct`
                // across every global type. Record keys them as `Type::kind:member`. Top-level
                // functions / types / constants also include kind so `class Phar` and
                // `const PHAR` (and `debug()` vs `DEBUG`) do not overwrite each other.
                TyhpdefOverlayStamp.Record(symbol, stamps);
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                CaptureStamps(child, stamps);
            }
        }

        private bool TryHandleTyhpdefOverlayKeywords(
            IBase2Ast node,
            bool isPartial,
            bool isOmit,
            bool isDeprecated,
            bool isObsolete,
            bool isExtern = false,
            bool isFallback = false)
        {
            // Grammar already forbids stacking `extern` with the other four; still reject if a
            // visitor path sets both. `extern` is not counted in the four-flag combination below.
            // `fallback` may sit with `deprecated` or `obsolete`. It may not sit with
            // `partial`, `omit`, or `extern`.
            if (isExtern && (isPartial || isOmit || isDeprecated || isObsolete))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefIllegalKeywordCombination,
                    node,
                    _currentFileName);
                return true;
            }

            if (isFallback && (isPartial || isOmit || isExtern))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefIllegalKeywordCombination,
                    node,
                    _currentFileName);
                return true;
            }

            var flagCount = (isPartial ? 1 : 0)
                + (isOmit ? 1 : 0)
                + (isDeprecated ? 1 : 0)
                + (isObsolete ? 1 : 0);
            if (flagCount > 1)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefIllegalKeywordCombination,
                    node,
                    _currentFileName);
                return true;
            }

            if (isOmit && !_tyhpdefIsOverlay)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefOmitOutsideOverlay,
                    node,
                    _currentFileName);
                return true;
            }

            return false;
        }

        /// <summary>
        /// <paramref name="existing"/> is scoped/named the way the caller needs it for eviction
        /// and the unstamped-replace (8022) compatibility check — for an <c>as</c>-aliased
        /// function/class replace that is the Tyhp alias name, not the PHP original.
        /// <paramref name="phpOriginalBaseline"/> is an optional, separately-scoped lookup of the
        /// PHP original by its own short name (see call sites in <c>TyhpBinder.Tyhpdef.cs</c>) —
        /// pass it whenever <paramref name="existing"/> was found under a different name/scope
        /// than the PHP original, so <see cref="ResolveLayer1StampKey"/> can key off the real
        /// baseline instead of guessing from the alias-scoped symbol.
        /// <paramref name="isOverloadAppend"/> is true when this declaration is a later
        /// same-name overlay function or method that appends to an already-replaced name
        /// (not a full replace). Unstamped 8022 does not apply to overload appends; an
        /// authored stamp on a later overload still runs the 8021 compare.
        /// </summary>
        private void ReportOverlayStampAndCompatibility(
            IBase2Ast node,
            IBaseSymbol? existing,
            string tyhpName,
            string? layer1Key = null,
            IBaseSymbol? phpOriginalBaseline = null,
            bool isOverloadAppend = false)
        {
            if (!_tyhpdefIsOverlay)
            {
                return;
            }

            // `@overlay-against:` does not apply to `extern` (no Layer 1 member list).
            // Do not treat the placeholder as a hollow class that must match members.
            // Layer 1 still records a type-header stamp for include `extern` so a later
            // full real overlay can match against that header (real-wins; do not skip
            // when `existing` is extern and the overlay declaration is real).
            if (node is TyhpdefImportObjectDeclAst { IsExtern: true }
                or TyhpdefImportFunctionDeclAst { IsExtern: true }
                or TyhpdefImportConstAst { IsExtern: true })
            {
                return;
            }

            var isPartialFunction = node is TyhpdefImportFunctionDeclAst { IsPartial: true }
                || node is PhpMethodDeclAst { IsPartial: true };
            var authored = TyhpdefOverlayStamp.TryGetAuthoredStamp(node);
            if (isPartialFunction)
            {
                // Name-only merge never runs the unstamped 8022 replace check. A name-only
                // stamp is accepted when the target was found (caller skips this method
                // when it was not). A full Layer 1 stamp still uses the 8021 compare below.
                if (string.IsNullOrEmpty(authored)
                    || TyhpdefOverlayStamp.IsNameOnlyFunctionStamp(authored))
                {
                    return;
                }
            }

            var resolvedKey = string.IsNullOrEmpty(layer1Key)
                ? ResolveLayer1StampKey(node, phpOriginalBaseline ?? existing, tyhpName)
                : layer1Key;
            string? layer1Stamp = null;
            _tyhpdefLayer1Stamps?.TryGetValue(resolvedKey, out layer1Stamp);

            if (!string.IsNullOrEmpty(authored))
            {
                if (!TyhpdefOverlayStamp.MatchesAnyRecordedStamp(authored, layer1Stamp))
                {
                    var severity = _compilationOptions?.StrictMode == true
                        ? DiagnosticSeverity.Error
                        : DiagnosticSeverity.Warning;
                    _diagnostics.AddFromAst(
                        severity,
                        MessageCode.TyhpdefOverlayStampMismatch,
                        node,
                        _currentFileName,
                        tyhpName);
                }

                return;
            }

            if (existing == null || isOverloadAppend)
            {
                return;
            }

            if (!string.IsNullOrEmpty(layer1Stamp)
                && !TyhpdefOverlayStamp.IsCompatibleReplace(existing, node, layer1Stamp))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefOverlayIncompatibleReplace,
                    node,
                    _currentFileName,
                    tyhpName);
            }
        }

        /// <summary>
        /// Layer 1 stamps are keyed by the bound PHP / original FQN
        /// (<see cref="CaptureStamps"/>). Overlay
        /// <c>function php_name as tyhpName</c> (and the class/const equivalent) is registered
        /// under <paramref name="tyhpName"/>, which is not in Layer 1 — look up <c>php_name</c>
        /// so <c>@overlay-against:</c> can match the baseline that was actually copied.
        /// An unqualified <c>php_name</c> is resolved through <paramref name="existing"/>'s
        /// namespace-qualified FQN when that symbol is the same PHP short name, so
        /// <c>namespace Psr\Log { partial interface LoggerInterface }</c> looks up
        /// <c>\Psr\Log\LoggerInterface</c> rather than <c>\LoggerInterface</c>. A name that
        /// already contains <c>\</c> is the PHP original as written and is used as-is — never
        /// replaced by a Tyhp <c>as</c> alias FQN.
        /// Members are <c>Type::member</c> (<see cref="TyhpdefOverlayStamp.MemberKey"/>), not the
        /// binder FQN (<c>\__construct</c> collides across every global class).
        /// </summary>
        private static string ResolveLayer1StampKey(IBase2Ast node, IBaseSymbol? existing, string tyhpName)
        {
            var originalName = node switch
            {
                TyhpdefImportFunctionDeclAst function => ExtractTyhpdefName(function.NameOrAlias).originalName,
                TyhpdefImportObjectDeclAst type => ExtractTyhpdefName(type.NameOrAlias).originalName,
                TyhpdefImportConstAst constant => ExtractTyhpdefName(constant.NameOrAlias).originalName,
                _ => null,
            };

            var stampKind = TyhpdefOverlayStamp.KindOfDeclaration(node);
            if (!string.IsNullOrEmpty(originalName))
            {
                return ResolveOriginalNameStampKey(originalName, existing, stampKind);
            }

            if (existing is ObjectMethodSymbol or ObjectPropertySymbol or ObjectConstantSymbol
                && existing.ContainingScope?.DeclarationSymbol is BaseSymbol owner
                && !string.IsNullOrEmpty(owner.FullyQualifiedName))
            {
                var memberName = existing is ObjectMethodSymbol method
                    && !string.IsNullOrEmpty(method.OriginalPhpName)
                    ? method.OriginalPhpName
                    : existing.Name;
                return TyhpdefOverlayStamp.MemberKey(
                    owner.FullyQualifiedName,
                    memberName,
                    TyhpdefOverlayStamp.MemberKindOf(existing));
            }

            var fqn = existing?.FullyQualifiedName ?? tyhpName;
            var kind = existing != null ? TyhpdefOverlayStamp.KindOf(existing) : stampKind;
            return TyhpdefOverlayStamp.SymbolKey(fqn, kind);
        }

        private static string ResolveOriginalNameStampKey(
            string originalName,
            IBaseSymbol? existing,
            string stampKind)
        {
            // Overlay `function \Foo\bar as baz` / `class \Vendor\Long as Short` writes the
            // PHP original with namespace separators. Layer 1 is keyed by that FQN, not the
            // Tyhp alias `existing` may have been looked up under.
            if (originalName.IndexOf('\\') >= 0)
            {
                return TyhpdefOverlayStamp.SymbolKey(originalName, stampKind);
            }

            // Unqualified AST identifier (`partial interface LoggerInterface` inside
            // `namespace Psr\Log`). CaptureStamps keys by the bound Layer 1 FQN, which
            // includes the enclosing namespace. Use that FQN when `existing` is the same
            // PHP short name. Do not use it when `existing` was found under a Tyhp `as`
            // alias (different short name) — fall back to the raw identifier, which is
            // correct for global aliases (`function call_user_func as …`).
            if (existing is BaseSymbol existingBase
                && !string.IsNullOrEmpty(existingBase.FullyQualifiedName)
                && StampKeyLastSegmentEquals(
                    existingBase.FullyQualifiedName,
                    originalName,
                    ordinal: existing is ConstantSymbol))
            {
                var kind = TyhpdefOverlayStamp.KindOf(existingBase);
                return TyhpdefOverlayStamp.SymbolKey(existingBase.FullyQualifiedName, kind);
            }

            return TyhpdefOverlayStamp.SymbolKey(originalName, stampKind);
        }

        private static bool StampKeyLastSegmentEquals(string fqn, string shortName, bool ordinal)
        {
            var lastSep = fqn.LastIndexOf('\\');
            var lastSegment = lastSep >= 0 ? fqn[(lastSep + 1)..] : fqn;
            var comparison = ordinal
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return string.Equals(lastSegment, shortName, comparison);
        }

        private bool TryOmitTyhpdefSymbol(
            IBase2Ast node,
            IBaseScope targetScope,
            string shortName,
            bool wantFunction)
        {
            if (!_tyhpdefIsOverlay)
            {
                return true;
            }

            var existing = FindExistingTyhpdefSymbol(targetScope, shortName, wantFunction);
            if (existing == null)
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefOmitMissingSymbol,
                    node,
                    _currentFileName,
                    shortName);
                return true;
            }

            EvictPhpVersionGatedSymbol(targetScope, existing);
            RemoveTyhpdefSymbol(existing);
            if (existing is BaseSymbol omittedSymbol)
            {
                _tyhpdefRegistrar?.RetractCrossPackageDuplicate(omittedSymbol);
            }

            return true;
        }

        private IBaseSymbol? FindExistingTyhpdefSymbol(IBaseScope targetScope, string shortName, bool wantFunction)
        {
            foreach (var scope in EnumerateSamePhpNamespaceScopes(targetScope))
            {
                var hit = MatchChild(scope, shortName, wantFunction);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        private static IBaseSymbol? MatchChild(IBaseScope scope, string shortName, bool wantFunction)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (wantFunction)
                {
                    if (symbol is FunctionDeclarationSymbol
                        && string.Equals(symbol.Name, shortName, StringComparison.OrdinalIgnoreCase))
                    {
                        return symbol;
                    }

                    continue;
                }

                if (symbol is not (ObjectDeclarationSymbol or ConstantSymbol or VariableSymbol))
                {
                    continue;
                }

                // PHP keeps constants in their own case-sensitive namespace (same rule as
                // BaseScope's constant index) — `omit const foo_bar;` must not match `FOO_BAR`.
                // Classes/variables stay case-insensitive like the rest of PHP's class/variable
                // namespace.
                var comparison = symbol is ConstantSymbol
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
                if (string.Equals(symbol.Name, shortName, comparison))
                {
                    return symbol;
                }
            }

            return null;
        }

        private void RemoveTyhpdefSymbol(IBaseSymbol symbol)
        {
            if (symbol is BaseSymbol baseSymbol)
            {
                _tyhpdefRegistrar?.UntrackFullyQualifiedName(baseSymbol.FullyQualifiedName);
            }

            if (symbol is ObjectDeclarationSymbol type)
            {
                type.Members.Clear();
                type.Constants.Clear();
            }

            symbol.ContainingScope?.TryRemoveChildSymbol(symbol);
        }

        private bool TryBeginOverlayFunctionReplace(
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope targetScope,
            string symbolName,
            bool hasAlias,
            IBaseScope phpTargetScope,
            string phpShortName,
            FunctionDeclarationSymbol? existingFromExternMerge = null,
            bool realWinsReplaced = false)
        {
            if (!_tyhpdefIsOverlay)
            {
                return false;
            }

            var existing = (IBaseSymbol?)existingFromExternMerge
                ?? FindExistingTyhpdefSymbol(targetScope, symbolName, wantFunction: true);
            // `as`-aliased full replace registers/evicts under the Tyhp alias name (`symbolName`
            // in `targetScope`), which is almost never the Layer 1 baseline's name. Look the PHP
            // original up by its own short name in its own namespace scope so an unqualified
            // original (`function bar as baz` inside `namespace Foo`) still keys the stamp lookup
            // off `\Foo\bar`, not the alias.
            var phpOriginalBaseline = hasAlias
                ? FindExistingTyhpdefSymbol(phpTargetScope, phpShortName, wantFunction: true)
                : null;
            // First overlay decl of this Tyhp name is a replace (8022 if unstamped).
            // Later same-name decls in this overlay file append as overloads.
            var isOverloadAppend = _overlayReplacedFunctionNames != null
                && _overlayReplacedFunctionNames.Contains(symbolName);
            ReportOverlayStampAndCompatibility(
                funcDecl,
                existing,
                symbolName,
                phpOriginalBaseline: phpOriginalBaseline,
                isOverloadAppend: isOverloadAppend);

            var replaced = _overlayReplacedFunctionNames;
            if (replaced != null && replaced.Add(symbolName))
            {
                if (existing != null && !realWinsReplaced)
                {
                    EvictPhpVersionGatedSymbol(targetScope, existing);
                    RemoveTyhpdefSymbol(existing);
                }
                else if (existing == null)
                {
                    // Layer 1 may have declared this name only under a `#[\Tyhp\Php]` gate that
                    // does not match the compile target, so it was never registered as a real
                    // symbol here — but a record still lingers in the gated-declaration tracker.
                    // Evict it so the overlay's replacement does not collide with it (4303).
                    EvictPhpVersionGatedDeclaration(targetScope, symbolName, "function", caseSensitiveName: false);
                }
            }

            return false;
        }

        private void BindOverlayPartialFunction(
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope matchScope,
            string matchName,
            string? aliasName,
            IBaseScope aliasScope)
        {
            if (!_tyhpdefIsOverlay)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefPartialFunctionOutsideOverlay,
                    funcDecl,
                    _currentFileName);
                return;
            }

            if (ShouldSkipPhpVersionGatedOverlay(funcDecl, matchScope))
            {
                return;
            }

            var existing = FindExistingTyhpdefSymbol(matchScope, matchName, wantFunction: true)
                as FunctionDeclarationSymbol;
            if (existing == null)
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefPartialFunctionTargetNotFound,
                    funcDecl,
                    _currentFileName,
                    matchName);
                return;
            }

            if (existing.IsExtern)
            {
                ReportPartialOnExtern(funcDecl, matchName);
                return;
            }

            ReportOverlayStampAndCompatibility(funcDecl, existing, matchName);

            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, existing.Name, StringComparison.OrdinalIgnoreCase);

            if (!hasAlias)
            {
                _overlayPartialKeptNames?.Add(existing.Name);
                MergePartialCallableAttributes(existing, funcDecl);
                return;
            }

            var alias = CloneFunctionForPartialAlias(existing, aliasName!);
            MergePartialCallableAttributes(alias, funcDecl);
            RegisterOverlayPartialFunctionAlias(alias, funcDecl, aliasScope);
            _overlayPartialPendingRenames?.Add(new OverlayPartialPendingRename
            {
                Symbol = existing,
                KeepKey = existing.Name,
            });
        }

        private void RegisterOverlayPartialFunctionAlias(
            FunctionDeclarationSymbol alias,
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope aliasScope)
        {
            var existingAlias = FindExistingTyhpdefSymbol(aliasScope, alias.Name, wantFunction: true);
            if (existingAlias != null && !ReferenceEquals(existingAlias, alias))
            {
                EvictPhpVersionGatedSymbol(aliasScope, existingAlias);
                RemoveTyhpdefSymbol(existingAlias);
            }

            TryRegisterTyhpdefFunction(alias, funcDecl, aliasScope);
        }

        private void BindOverlayPartialFunctionMember(
            PhpMethodDeclAst methodDecl,
            ObjectDeclarationScope objScope)
        {
            if (!_tyhpdefIsOverlay)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefPartialFunctionOutsideOverlay,
                    methodDecl,
                    _currentFileName);
                return;
            }

            if (!_tyhpdefOverlayMemberReplace)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefPartialFunctionMemberOutsidePartialType,
                    methodDecl,
                    _currentFileName);
                return;
            }

            if (ShouldSkipPhpVersionGatedOverlay(methodDecl, objScope))
            {
                return;
            }

            var matchName = OverlayPartialMethodMatchName(methodDecl);
            var aliasName = OverlayPartialMethodAliasName(methodDecl);

            IBaseSymbol? existing = null;
            if (objScope.DeclarationSymbol is ObjectDeclarationSymbol decl
                && decl.Members.TryGetValue(matchName, out var memberHit))
            {
                existing = memberHit;
            }

            if (existing is not ObjectMethodSymbol method)
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefPartialFunctionTargetNotFound,
                    methodDecl,
                    _currentFileName,
                    matchName);
                return;
            }

            var typeFqn = objScope.DeclarationSymbol is BaseSymbol type
                ? type.FullyQualifiedName
                : matchName;
            ReportOverlayStampAndCompatibility(
                methodDecl,
                method,
                matchName,
                TyhpdefOverlayStamp.MemberKey(
                    typeFqn,
                    OverlayMemberStampName(methodDecl, matchName),
                    TyhpdefOverlayStamp.KindMethod));

            var keepKey = TyhpdefOverlayStamp.MemberKey(typeFqn, method.Name);
            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, method.Name, StringComparison.OrdinalIgnoreCase);

            if (!hasAlias)
            {
                _overlayPartialKeptNames?.Add(keepKey);
                MergePartialCallableAttributes(method, methodDecl);
                return;
            }

            var alias = CloneMethodForPartialAlias(method, aliasName!);
            MergePartialCallableAttributes(alias, methodDecl);
            RegisterOverlayPartialMethodAlias(alias, aliasName!, objScope, methodDecl);
            _overlayPartialPendingRenames?.Add(new OverlayPartialPendingRename
            {
                Symbol = method,
                KeepKey = keepKey,
                MethodScope = objScope,
            });
        }

        private void RegisterOverlayPartialMethodAlias(
            ObjectMethodSymbol alias,
            string aliasName,
            ObjectDeclarationScope objScope,
            PhpMethodDeclAst methodDecl)
        {
            if (objScope.DeclarationSymbol is ObjectDeclarationSymbol decl
                && decl.Members.TryGetValue(aliasName, out var existingAlias)
                && !ReferenceEquals(existingAlias, alias))
            {
                RemoveObjectMember(objScope, aliasName, isConstant: false);
            }

            if (!objScope.TryAddChildSymbol(alias, out var existing))
            {
                _diagnostics.AddDuplicateFromAst(
                    MessageCode.TyhpdefDuplicateDeclaration,
                    methodDecl,
                    _currentFileName,
                    existing,
                    alias.Name);
                return;
            }

            RegisterObjectMember(objScope, alias, aliasName);
            TrackTyhpdefSymbol(alias);
        }

        private static ObjectMethodSymbol CloneMethodForPartialAlias(ObjectMethodSymbol source, string newName)
        {
            var clone = new ObjectMethodSymbol(
                newName,
                source.DeclaringAstNode,
                source.SourceFile,
                source.Visibility,
                source.SymbolType)
            {
                ReturnType = source.ReturnType,
                IsAsync = source.IsAsync,
                IsAbstract = source.IsAbstract,
                IsStatic = source.IsStatic,
                IsGenerator = source.IsGenerator,
                IsDeprecated = source.IsDeprecated,
                DeprecatedMessage = source.DeprecatedMessage,
                IsObsolete = source.IsObsolete,
                OriginalPhpName = string.IsNullOrEmpty(source.OriginalPhpName)
                    ? source.Name
                    : source.OriginalPhpName,
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

            return clone;
        }

        private static string OverlayPartialMethodMatchName(PhpMethodDeclAst methodDecl)
        {
            if (methodDecl.AstGrammarAddons.TryGetValue("nameOrAlias", out var nameOrAlias))
            {
                var (originalName, _) = ExtractTyhpdefName(nameOrAlias);
                if (!string.IsNullOrEmpty(originalName))
                {
                    return originalName;
                }
            }

            return methodDecl.Identifier ?? "";
        }

        private static string? OverlayPartialMethodAliasName(PhpMethodDeclAst methodDecl)
        {
            if (methodDecl.AstGrammarAddons.TryGetValue("nameOrAlias", out var nameOrAlias))
            {
                var (_, aliasName) = ExtractTyhpdefName(nameOrAlias);
                return string.IsNullOrEmpty(aliasName) ? null : aliasName;
            }

            return null;
        }

        private void FlushOverlayPartialFunctionRenames()
        {
            if (_overlayPartialPendingRenames != null)
            {
                foreach (var pending in _overlayPartialPendingRenames)
                {
                    if (_overlayPartialKeptNames?.Contains(pending.KeepKey) == true)
                    {
                        continue;
                    }

                    if (pending.Symbol.ContainingScope == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(pending.TypeAliasName)
                        && pending.Symbol is ObjectDeclarationSymbol type)
                    {
                        FlushOverlayPartialTypeRename(type, pending.KeepKey, pending.TypeAliasName!);
                        continue;
                    }

                    if (pending.MethodScope != null)
                    {
                        if (pending.Symbol is BaseSymbol methodBase)
                        {
                            _tyhpdefRegistrar?.UntrackFullyQualifiedName(methodBase.FullyQualifiedName);
                        }

                        RemoveObjectMember(pending.MethodScope, pending.Symbol.Name, isConstant: false);
                    }
                    else
                    {
                        RemoveTyhpdefSymbol(pending.Symbol);
                    }
                }
            }

            FlushOverlayPartialTypeHeaderNoops();
        }

        private void FlushOverlayPartialTypeRename(
            ObjectDeclarationSymbol type,
            string oldName,
            string newName)
        {
            var scope = type.ContainingScope;
            if (scope == null)
            {
                return;
            }

            var oldFqn = type.FullyQualifiedName;
            if (string.IsNullOrEmpty(type.OriginalPhpName))
            {
                type.OriginalPhpName = oldFqn.TrimStart('\\');
            }

            scope.TryRemoveChildSymbolName(type, oldName);
            type.Name = newName;
            var lastSep = oldFqn.LastIndexOf('\\');
            type.FullyQualifiedName = lastSep >= 0
                ? oldFqn[..(lastSep + 1)] + newName
                : "\\" + newName;
            _tyhpdefRegistrar?.UntrackFullyQualifiedName(oldFqn);
            TrackTyhpdefSymbol(type);
        }

        private void FlushOverlayPartialTypeHeaderNoops()
        {
            if (_overlayPartialTypeHeaderNoops == null || _overlayPartialTypeHeaderNoops.Count == 0)
            {
                return;
            }

            foreach (var noop in _overlayPartialTypeHeaderNoops)
            {
                var isKeepForAlias = _overlayPartialPendingRenames?.Any(pending =>
                    !string.IsNullOrEmpty(pending.TypeAliasName)
                    && string.Equals(pending.KeepKey, noop.Name, StringComparison.OrdinalIgnoreCase)) == true;
                if (isKeepForAlias)
                {
                    continue;
                }

                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefPartialTypeHeaderNoEffect,
                    noop.Node,
                    _currentFileName,
                    noop.Kind,
                    noop.Name);
            }
        }

        private sealed class OverlayPartialPendingRename
        {
            public required IBaseSymbol Symbol { get; init; }
            public required string KeepKey { get; init; }
            public ObjectDeclarationScope? MethodScope { get; init; }
            public string? TypeAliasName { get; init; }
        }

        private sealed class OverlayPartialTypeHeaderNoop
        {
            public required IBase2Ast Node { get; init; }
            public required string Kind { get; init; }
            public required string Name { get; init; }
        }

        /// <summary>
        /// Clones each declaring AST on the current overload set and unions overlay
        /// attributes onto those clones so Layer 1 / Layer 2 ASTs stay immutable.
        /// Shared declaring nodes are cloned once and reused.
        /// </summary>
        private static void MergePartialCallableAttributes(IBaseSymbol target, IBase2Ast overlay)
        {
            var clones = new Dictionary<IBase2Ast, IBase2Ast>(new DeclaringAstReferenceComparer());

            IBase2Ast? Clone(IBase2Ast? node)
            {
                if (node == null)
                {
                    return null;
                }

                if (clones.TryGetValue(node, out var cached))
                {
                    return cached;
                }

                Base2Ast clone;
                if (node is TyhpdefImportFunctionDeclAst function)
                {
                    clone = function.CloneSignature();
                }
                else if (node is PhpMethodDeclAst method)
                {
                    clone = method.CloneSignature();
                }
                else
                {
                    return node;
                }

                clone.MergeAttributesLastWins(overlay);
                clones[node] = clone;
                return clone;
            }

            if (target is FunctionDeclarationSymbol function)
            {
                var cloned = Clone(function.DeclaringAstNode);
                if (cloned != null)
                {
                    function.DeclaringAstNode = cloned;
                }

                foreach (var overload in function.Overloads)
                {
                    var overloadClone = Clone(overload.DeclaringAstNode);
                    if (overloadClone != null)
                    {
                        overload.DeclaringAstNode = overloadClone;
                    }
                }

                return;
            }

            if (target is ObjectMethodSymbol method)
            {
                var cloned = Clone(method.DeclaringAstNode);
                if (cloned != null)
                {
                    method.DeclaringAstNode = cloned;
                }
            }
        }

        private sealed class DeclaringAstReferenceComparer : IEqualityComparer<IBase2Ast>
        {
            public bool Equals(IBase2Ast? x, IBase2Ast? y) => ReferenceEquals(x, y);

            public int GetHashCode(IBase2Ast obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private void BindTyhpdefOverlayPartialObject(
            TyhpdefImportObjectDeclAst objDecl,
            IBaseScope matchScope,
            string matchName,
            string? aliasName)
        {
            var existing = FindExistingObjectType(matchScope, matchName);
            if (existing == null || existing.IsExtension)
            {
                // Ungated overlay of a Layer 1 type that is gated off for this
                // `output.phpVersion` is skip-before-bind, not TYHP8019 — the target exists
                // in the package, it is just inactive (PHP 8.4-only DOM types at 8.2, …).
                if (existing == null
                    && HasInactiveGatedDeclaration(
                        matchScope,
                        matchName,
                        SymbolType.ObjectTypeDeclaration))
                {
                    return;
                }

                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefOverlayPartialTargetNotFound,
                    objDecl,
                    _currentFileName,
                    matchName);
                return;
            }

            if (existing.IsExtern)
            {
                ReportPartialOnExtern(objDecl, matchName);
                return;
            }

            if (!OverlayPartialKindMatches(objDecl, existing))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefOverlayPartialTargetNotFound,
                    objDecl,
                    _currentFileName,
                    matchName);
                return;
            }

            var objScope = FindObjectDeclarationScope(existing);
            if (objScope == null)
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefOverlayPartialTargetNotFound,
                    objDecl,
                    _currentFileName,
                    matchName);
                return;
            }

            ReportOverlayStampAndCompatibility(objDecl, existing, matchName);

            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, existing.Name, StringComparison.OrdinalIgnoreCase);

            if (objDecl.IsHeaderOnly)
            {
                ApplyOverlayPartialTypeHeader(objDecl, existing);
            }
            else if (objDecl.BackingType != null)
            {
                // Header-only overlays route the backing type through
                // ApplyOverlayPartialTypeHeader above. A brace-form overlay
                // (`partial enum Foo: int { … }`) reaches this branch instead —
                // still apply the written backing type so it is not silently
                // dropped from the merged symbol (and from compiled-library `package.tyhpdef`
                // regeneration, which reads `ObjectDeclarationSymbol.BackingType`).
                existing.BackingType = objDecl.BackingType;
            }

            if (objDecl.AstAttributes.Count > 0)
            {
                MergePartialTypeAttributes(existing, objDecl);
            }

            if (!hasAlias)
            {
                _overlayPartialKeptNames?.Add(existing.Name);
                if (objDecl.IsHeaderOnly && !OverlayPartialTypeHeaderHasEffect(objDecl, hasAlias: false))
                {
                    _overlayPartialTypeHeaderNoops?.Add(new OverlayPartialTypeHeaderNoop
                    {
                        Node = objDecl,
                        Kind = objDecl.DeclType?.ValueString ?? "class",
                        Name = existing.Name,
                    });
                }
            }
            else
            {
                RegisterOverlayPartialTypeAlias(existing, aliasName!, objDecl);
                _overlayPartialPendingRenames?.Add(new OverlayPartialPendingRename
                {
                    Symbol = existing,
                    KeepKey = existing.Name,
                    TypeAliasName = aliasName,
                });
            }

            if (objDecl.IsHeaderOnly)
            {
                return;
            }

            var previousReplace = _tyhpdefOverlayMemberReplace;
            var previousNames = _overlayReplacedMemberNames;
            var previousDup = _tyhpdefDuplicateMemberErrors;
            _tyhpdefOverlayMemberReplace = true;
            _overlayReplacedMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _tyhpdefDuplicateMemberErrors = false;
            try
            {
                BindTyhpdefObjectBodyCore(objDecl.Body, objScope, existing);
            }
            finally
            {
                _tyhpdefOverlayMemberReplace = previousReplace;
                _overlayReplacedMemberNames = previousNames;
                _tyhpdefDuplicateMemberErrors = previousDup;
            }
        }

        private void ApplyOverlayPartialTypeHeader(
            TyhpdefImportObjectDeclAst objDecl,
            ObjectDeclarationSymbol existing)
        {
            var classGenericList = ExtractTyhpdefObjectGenericList(objDecl.NameOrAlias);
            if (classGenericList != null)
            {
                existing.GenericParameters.Clear();
                PopulateGenericParameters(
                    classGenericList,
                    existing.GenericParameters,
                    _currentFileName,
                    SymbolType.ClassGenericTypeParameter);
            }

            if (HasWrittenOverlayPartialExtends(objDecl))
            {
                ApplyOverlayPartialExtends(objDecl, existing);
            }

            if (HasWrittenOverlayPartialImplements(objDecl))
            {
                existing.ImplementsTypes.Clear();
                foreach (var impl in objDecl.Implements!.GetAllNotNull())
                {
                    var typeExpr = AsTypeExpression(impl, objDecl);
                    if (typeExpr is not null)
                    {
                        existing.ImplementsTypes.Add(typeExpr);
                    }
                }
            }

            if (objDecl.BackingType != null)
            {
                existing.BackingType = objDecl.BackingType;
            }
        }

        private static bool OverlayPartialTypeHeaderHasEffect(TyhpdefImportObjectDeclAst objDecl, bool hasAlias)
        {
            return hasAlias
                || objDecl.AstAttributes.Count > 0
                || ExtractTyhpdefObjectGenericList(objDecl.NameOrAlias) != null
                || HasWrittenOverlayPartialExtends(objDecl)
                || HasWrittenOverlayPartialImplements(objDecl)
                || objDecl.BackingType != null;
        }

        private static bool HasWrittenOverlayPartialExtends(TyhpdefImportObjectDeclAst objDecl)
        {
            if (objDecl.Extends is PhpClassNameListAst list)
            {
                return list.GetAllNotNull().Any();
            }

            return objDecl.Extends != null;
        }

        private static bool HasWrittenOverlayPartialImplements(TyhpdefImportObjectDeclAst objDecl)
            => objDecl.Implements != null && objDecl.Implements.GetAllNotNull().Any();

        private static void ApplyOverlayPartialExtends(
            TyhpdefImportObjectDeclAst objDecl,
            ObjectDeclarationSymbol existing)
        {
            if (objDecl.Extends is PhpClassNameListAst list)
            {
                var items = list.GetAllNotNull().ToList();
                existing.ExtendsType = items.Count > 0
                    ? AsTypeExpression(items[0], objDecl)
                    : null;
                existing.ImplementsTypes.Clear();
                for (var i = 1; i < items.Count; i++)
                {
                    var typeExpr = AsTypeExpression(items[i], objDecl);
                    if (typeExpr is not null)
                    {
                        existing.ImplementsTypes.Add(typeExpr);
                    }
                }

                return;
            }

            existing.ExtendsType = objDecl.Extends as ITypeExpression
                ?? (objDecl.Extends is IExpression extendsName
                    ? PhpNamedTypeAst.WrapClassName(extendsName, objDecl)
                    : null);
        }

        private void RegisterOverlayPartialTypeAlias(
            ObjectDeclarationSymbol existing,
            string aliasName,
            TyhpdefImportObjectDeclAst objDecl)
        {
            var scope = existing.ContainingScope;
            if (scope == null)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefBindError,
                    objDecl,
                    _currentFileName,
                    aliasName);
                return;
            }

            var collision = FindExistingObjectType(scope, aliasName);
            if (collision != null && !ReferenceEquals(collision, existing))
            {
                _diagnostics.AddDuplicateFromAst(
                    MessageCode.TyhpdefDuplicateDeclaration,
                    objDecl,
                    _currentFileName,
                    collision,
                    aliasName);
                return;
            }

            if (!scope.TryAddChildSymbolAlias(existing, aliasName))
            {
                _diagnostics.AddDuplicateFromAst(
                    MessageCode.TyhpdefDuplicateDeclaration,
                    objDecl,
                    _currentFileName,
                    FindExistingObjectType(scope, aliasName),
                    aliasName);
            }
        }

        private static void MergePartialTypeAttributes(ObjectDeclarationSymbol target, IBase2Ast overlay)
        {
            if (target.DeclaringAstNode is not TyhpdefImportObjectDeclAst typeDecl)
            {
                return;
            }

            var clone = typeDecl.CloneHeader();
            clone.MergeAttributesLastWins(overlay);
            target.DeclaringAstNode = clone;
        }

        private static bool OverlayPartialKindMatches(
            TyhpdefImportObjectDeclAst objDecl,
            ObjectDeclarationSymbol existing)
        {
            var kind = objDecl.DeclType?.ValueString?.ToLowerInvariant();
            return kind switch
            {
                "class" => existing.ObjectKind == PhpTypeDeclType.Class && !existing.IsExtension,
                "interface" => existing.ObjectKind == PhpTypeDeclType.Interface,
                "trait" => existing.ObjectKind == PhpTypeDeclType.Trait,
                "enum" => existing.ObjectKind == PhpTypeDeclType.Enum,
                _ => true,
            };
        }

        private bool TryHandleOverlayMember(IBase2Ast member, ObjectDeclarationScope objScope, string name)
        {
            if (!_tyhpdefOverlayMemberReplace || string.IsNullOrEmpty(name))
            {
                return IsMemberOmit(member);
            }

            if (ShouldSkipPhpVersionGatedOverlay(member, objScope))
            {
                return true;
            }

            var typeFqn = objScope.DeclarationSymbol is BaseSymbol type
                ? type.FullyQualifiedName
                : name;
            var memberKey = TyhpdefOverlayStamp.MemberKey(typeFqn, name);
            IBaseSymbol? existing = null;
            if (objScope.DeclarationSymbol is ObjectDeclarationSymbol decl)
            {
                if (decl.Members.TryGetValue(name, out var memberHit))
                {
                    existing = memberHit;
                }
                else if (decl.TryGetConstant(name, out var constant))
                {
                    existing = constant;
                }
            }

            if (IsMemberOmit(member))
            {
                if (existing != null)
                {
                    EvictPhpVersionGatedSymbol(objScope, existing);
                }
                else
                {
                    EvictPhpVersionGatedOverlayMember(objScope, member, name);
                }

                RemoveObjectMember(objScope, name, member is TyhpdefImportConstDeclListAst or PhpConstDeclListAst);
                return true;
            }

            var stampMemberName = OverlayMemberStampName(member, name);
            // First overlay member of this name replaces the harvested member (8022 if
            // unstamped). Later same-name methods in this overlay body append as overloads.
            var isOverloadAppend = _overlayReplacedMemberNames != null
                && _overlayReplacedMemberNames.Contains(memberKey);
            ReportOverlayStampAndCompatibility(
                member,
                existing,
                name,
                TyhpdefOverlayStamp.MemberKey(
                    typeFqn,
                    stampMemberName,
                    TyhpdefOverlayStamp.MemberKindOfDeclaration(member)),
                isOverloadAppend: isOverloadAppend);

            if (_overlayReplacedMemberNames != null && _overlayReplacedMemberNames.Add(memberKey))
            {
                if (existing != null)
                {
                    EvictPhpVersionGatedSymbol(objScope, existing);
                    RemoveObjectMember(objScope, name, existing is ObjectConstantSymbol);
                }
                else
                {
                    // Layer 1 may have declared this member only under a `#[\Tyhp\Php]` gate that
                    // does not match the compile target, so it was never registered as a live
                    // object member — but a record still lingers in the gated-declaration tracker.
                    // Evict it so the overlay's replacement does not collide with it (4303).
                    EvictPhpVersionGatedOverlayMember(objScope, member, name);
                }
            }

            return false;
        }

        /// <summary>
        /// Evicts a lingering gated-declaration record for an overlay member that has no live
        /// symbol to evict via <see cref="EvictPhpVersionGatedSymbol"/>, inferring the symbol
        /// kind from the member's AST shape. Methods must resolve through
        /// <see cref="DetermineMethodSymbolType"/> (not a generic "method" kind) because magic
        /// methods (<c>__wakeup</c>, <c>__debugInfo</c>, …) are tracked under their own
        /// dedicated <see cref="SymbolType"/> values, distinct from the gate key that a plain
        /// <see cref="SymbolType.InstanceObjectMethod"/> would produce.
        /// </summary>
        private void EvictPhpVersionGatedOverlayMember(ObjectDeclarationScope objScope, IBase2Ast member, string name)
        {
            switch (member)
            {
                case PhpMethodDeclAst methodDecl:
                    var isStatic = ConvertModifiers(methodDecl.Modifiers).HasFlag(MemberModifier.Static);
                    EvictPhpVersionGatedDeclaration(objScope, name, DetermineMethodSymbolType(name, isStatic));
                    break;
                case PhpPropertyDeclAst:
                    EvictPhpVersionGatedDeclaration(objScope, name, "property", caseSensitiveName: false);
                    break;
                case TyhpdefImportConstDeclListAst or PhpConstDeclListAst:
                    EvictPhpVersionGatedDeclaration(objScope, name, "object-constant", caseSensitiveName: true);
                    break;
            }
        }

        private static bool IsMemberOmit(IBase2Ast member)
        {
            if (member is PhpMethodDeclAst method)
            {
                return method.IsOmit;
            }

            if (member.AstGrammarAddons.TryGetValue("deprecatedOrObsolete", out var token)
                && token is TokenValueAst value)
            {
                return value.ValueInt64 == TyhpParser.T_TYHPDEF_OMIT;
            }

            return false;
        }

        private static void RemoveObjectMember(ObjectDeclarationScope objScope, string name, bool isConstant)
        {
            if (objScope.DeclarationSymbol is not ObjectDeclarationSymbol decl)
            {
                return;
            }

            IBaseSymbol? existing = null;
            if (isConstant)
            {
                if (decl.TryGetConstant(name, out var constant))
                {
                    existing = constant;
                    decl.Constants.Remove(name);
                }
            }
            else if (decl.Members.TryGetValue(name, out var member))
            {
                existing = member;
                decl.Members.Remove(name);
            }

            if (existing != null)
            {
                objScope.TryRemoveChildSymbol(existing);
            }
        }

        private static string OverlayMemberStampName(IBase2Ast member, string bindName)
        {
            if (member is PhpMethodDeclAst methodDecl
                && methodDecl.AstGrammarAddons.TryGetValue("nameOrAlias", out var nameOrAlias))
            {
                var (originalName, _) = ExtractTyhpdefName(nameOrAlias);
                if (!string.IsNullOrEmpty(originalName))
                {
                    return originalName;
                }
            }

            return bindName;
        }

        private static string GetTyhpdefMethodBindName(PhpMethodDeclAst methodDecl)
        {
            if (methodDecl.AstGrammarAddons.TryGetValue("nameOrAlias", out var nameOrAlias))
            {
                var (originalName, aliasName) = ExtractTyhpdefName(nameOrAlias);
                if (!string.IsNullOrEmpty(aliasName))
                {
                    return aliasName;
                }

                if (!string.IsNullOrEmpty(originalName))
                {
                    return originalName;
                }
            }

            return methodDecl.Identifier ?? "";
        }

        private static string FirstPropertyName(PhpPropertyDeclAst propDecl)
        {
            var first = propDecl.Properties?.GetAllNotNull().FirstOrDefault();
            return first?.Identifier ?? "";
        }
    }
}
