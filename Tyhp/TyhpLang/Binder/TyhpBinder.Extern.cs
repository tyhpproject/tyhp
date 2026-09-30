using System;
using System.Linq;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        /// <summary>
        /// Applies the tyhpdef <c>extern</c> merge table for an incoming object declaration.
        /// Returns <see langword="true"/> when the incoming declaration must not be registered
        /// (keep the existing real or first extern, or a kind-mismatch was reported).
        /// On real-wins, the placeholder is evicted/removed here (so
        /// <see cref="TryRegisterTyhpdefTopLevelSymbol"/> does not see a stale duplicate) and this
        /// returns <see langword="false"/> so the caller registers the real type.
        /// <paramref name="existing"/> stays set to the removed placeholder — even though it is no
        /// longer reachable from any scope — so the overlay caller can still read its
        /// <c>FullyQualifiedName</c> for <c>ReportOverlayStampAndCompatibility</c>'s Layer 1 stamp
        /// lookup. <paramref name="realWinsReplaced"/> tells that same caller the placeholder was
        /// already evicted here, so it must not evict/remove <paramref name="existing"/> again.
        /// </summary>
        private bool TryConsumeTyhpdefExternMerge(
            TyhpdefImportObjectDeclAst objDecl,
            IBaseScope targetScope,
            string symbolName,
            out ObjectDeclarationSymbol? existing,
            out bool realWinsReplaced)
        {
            realWinsReplaced = false;
            existing = FindExistingObjectType(targetScope, symbolName);
            if (existing == null || (!existing.IsExtern && !objDecl.IsExtern))
            {
                return false;
            }

            var incomingKind = ParseTyhpdefObjectKind(objDecl);
            if (!TyhpdefExternKindsCompatible(existing.ObjectKind, incomingKind))
            {
                ReportTyhpdefExternKindMismatch(existing, objDecl, incomingKind);
                return true;
            }

            if (existing.IsExtern && !objDecl.IsExtern)
            {
                EvictPhpVersionGatedSymbol(targetScope, existing);
                RemoveTyhpdefSymbol(existing);
                realWinsReplaced = true;
                return false;
            }

            // Existing real + incoming extern: keep real. Both extern: keep the first
            // (including the first @provided-by). If the first is kind-unspecified and
            // the incoming claim is specific, record that kind on the surviving symbol.
            if (existing.IsExtern
                && objDecl.IsExtern
                && existing.ObjectKind == PhpTypeDeclType.Unspecified
                && incomingKind != PhpTypeDeclType.Unspecified)
            {
                existing.ObjectKind = incomingKind;
            }

            return true;
        }

        private bool TryRejectIllegalTyhpdefExtern(
            TyhpdefImportObjectDeclAst objDecl,
            bool hasAlias,
            string typeName)
        {
            if (!objDecl.IsExtern)
            {
                return false;
            }

            var hasMembers = objDecl.Body != null && objDecl.Body.GetAllNotNull().Any();
            var hasImplements = objDecl.Implements != null && objDecl.Implements.GetAllNotNull().Any();
            var hasModifiers = objDecl.Modifiers != null && objDecl.Modifiers.Modifiers.Any();
            var hasGenerics = ExtractTyhpdefObjectGenericList(objDecl.NameOrAlias) != null;
            if (!hasMembers
                && objDecl.Extends == null
                && !hasImplements
                && !hasAlias
                && !hasGenerics
                && !hasModifiers
                && objDecl.BackingType == null)
            {
                return false;
            }

            _diagnostics.AddErrorFromAst(
                MessageCode.TyhpdefExternIllegalDeclaration,
                objDecl,
                _currentFileName,
                typeName);
            return true;
        }

        /// Applies the tyhpdef <c>extern</c> merge table for an incoming function declaration.
        /// Occupancy is the function PHP name space (class/const of the same FQCN may coexist).
        /// </summary>
        private bool TryConsumeTyhpdefExternFunctionMerge(
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope targetScope,
            string symbolName,
            out FunctionDeclarationSymbol? existing,
            out bool realWinsReplaced)
        {
            realWinsReplaced = false;
            existing = FindExistingTyhpdefSymbol(targetScope, symbolName, wantFunction: true)
                as FunctionDeclarationSymbol;
            if (existing == null || (!existing.IsExtern && !funcDecl.IsExtern))
            {
                return false;
            }

            if (existing.IsExtern && !funcDecl.IsExtern)
            {
                EvictPhpVersionGatedSymbol(targetScope, existing);
                RemoveTyhpdefSymbol(existing);
                realWinsReplaced = true;
                return false;
            }

            // Existing real + incoming extern: keep real. Both extern: keep the first
            // (including the first @provided-by).
            return true;
        }

        /// <summary>
        /// Applies the tyhpdef <c>extern</c> merge table for an incoming const declaration.
        /// Occupancy is the const PHP name space (class/function of the same FQCN may coexist).
        /// </summary>
        private bool TryConsumeTyhpdefExternConstMerge(
            TyhpdefImportConstAst constDecl,
            IBaseScope targetScope,
            string symbolName,
            out ConstantSymbol? existing,
            out bool realWinsReplaced)
        {
            realWinsReplaced = false;
            existing = FindExistingConstant(targetScope, symbolName);
            if (existing == null || (!existing.IsExtern && !constDecl.IsExtern))
            {
                return false;
            }

            if (existing.IsExtern && !constDecl.IsExtern)
            {
                EvictPhpVersionGatedSymbol(targetScope, existing);
                RemoveTyhpdefSymbol(existing);
                realWinsReplaced = true;
                return false;
            }

            return true;
        }

        private ConstantSymbol? FindExistingConstant(IBaseScope targetScope, string symbolName)
        {
            foreach (var scope in EnumerateSamePhpNamespaceScopes(targetScope))
            {
                foreach (var symbol in scope.GetAllChildSymbols())
                {
                    if (symbol is ConstantSymbol constant
                        && string.Equals(constant.Name, symbolName, StringComparison.Ordinal))
                    {
                        return constant;
                    }
                }
            }

            return null;
        }

        private bool TryRejectIllegalTyhpdefExternFunction(
            TyhpdefImportFunctionDeclAst funcDecl,
            bool hasAlias,
            string functionName)
        {
            if (!funcDecl.IsExtern)
            {
                return false;
            }

            var hasParameters = funcDecl.Parameters != null && funcDecl.Parameters.GetAllNotNull().Any();
            var hasGenerics = funcDecl.NameOrAlias?.AstGrammarAddons.TryGetValue("GenericArguments", out _) == true;
            if (!hasParameters
                && funcDecl.ReturnType == null
                && !hasAlias
                && !hasGenerics
                && !funcDecl.ReturnsRef
                && !funcDecl.IsAsync
                && !funcDecl.IsExtension)
            {
                return false;
            }

            _diagnostics.AddErrorFromAst(
                MessageCode.TyhpdefExternIllegalDeclaration,
                funcDecl,
                _currentFileName,
                functionName);
            return true;
        }

        private bool TryRejectIllegalTyhpdefExternConst(
            TyhpdefImportConstAst constDecl,
            bool hasAlias,
            string constName)
        {
            if (!constDecl.IsExtern)
            {
                return false;
            }

            if (constDecl.TypeExpr == null
                && constDecl.CoalesceExpr == null
                && !hasAlias)
            {
                return false;
            }

            _diagnostics.AddErrorFromAst(
                MessageCode.TyhpdefExternIllegalDeclaration,
                constDecl,
                _currentFileName,
                constName);
            return true;
        }

        private void ReportPartialOnExtern(IBase2Ast node, string typeName)
        {
            _diagnostics.AddErrorFromAst(
                MessageCode.TyhpdefPartialOnExtern,
                node,
                _currentFileName,
                typeName);
        }

        private void ReportTyhpdefExternKindMismatch(
            ObjectDeclarationSymbol existing,
            TyhpdefImportObjectDeclAst incoming,
            PhpTypeDeclType incomingKind)
        {
            var fqn = string.IsNullOrEmpty(existing.FullyQualifiedName)
                ? existing.Name
                : existing.FullyQualifiedName;
            _diagnostics.AddErrorFromAst(
                MessageCode.TyhpdefExternKindMismatch,
                incoming,
                _currentFileName,
                fqn,
                FormatTyhpdefExternKind(existing.ObjectKind, existing.IsExtern),
                FormatTyhpdefExternKind(incomingKind, incoming.IsExtern));
        }

        private void ReportBinderExternInheritance(
            MessageCode code,
            IBase2Ast typeAst,
            string displayName,
            string fileName,
            ObjectDeclarationSymbol externSymbol)
        {
            DiagnosticExtensions.GetOptionalEnd(typeAst, out var endLine, out var endColumn);
            var diagnostic = Diagnostic.Error(
                code,
                fileName,
                typeAst.Line,
                typeAst.Column,
                [displayName],
                endLine,
                endColumn);

            if (externSymbol.DeclaringAstNode != null)
            {
                diagnostic = diagnostic.WithLabels(
                    DiagnosticExtensions.LabelFromAst(
                        externSymbol.DeclaringAstNode,
                        externSymbol.SourceFile ?? fileName,
                        Message.Localize("CLI_DiagnosticLabelDeclaredHere")));
            }

            if (!string.IsNullOrEmpty(externSymbol.ProvidedBy))
            {
                diagnostic = diagnostic.WithHelp(externSymbol.ProvidedBy);
            }

            _diagnostics.Add(diagnostic);
        }

        private static PhpTypeDeclType ParseTyhpdefObjectKind(TyhpdefImportObjectDeclAst objDecl)
        {
            return objDecl.DeclType?.ValueString?.ToLowerInvariant() switch
            {
                "class" => PhpTypeDeclType.Class,
                "interface" => PhpTypeDeclType.Interface,
                "trait" => PhpTypeDeclType.Trait,
                "enum" => PhpTypeDeclType.Enum,
                "extern" => PhpTypeDeclType.Unspecified,
                _ => PhpTypeDeclType.Class,
            };
        }

        private static bool TyhpdefExternKindsCompatible(
            PhpTypeDeclType existing,
            PhpTypeDeclType incoming)
        {
            if (existing == incoming)
            {
                return true;
            }

            if (existing == PhpTypeDeclType.Trait || incoming == PhpTypeDeclType.Trait)
            {
                return false;
            }

            return existing == PhpTypeDeclType.Unspecified
                || incoming == PhpTypeDeclType.Unspecified;
        }

        private static string FormatTyhpdefExternKind(PhpTypeDeclType kind, bool isExtern)
        {
            if (kind == PhpTypeDeclType.Unspecified)
            {
                return "extern";
            }

            var noun = kind switch
            {
                PhpTypeDeclType.Interface => "interface",
                PhpTypeDeclType.Trait => "trait",
                PhpTypeDeclType.Enum => "enum",
                _ => "class",
            };
            return isExtern ? "extern " + noun : noun;
        }
    }
}
