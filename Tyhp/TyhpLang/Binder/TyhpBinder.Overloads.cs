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
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Binding of <c>.tyhp</c> overload sets: bodyless signatures that directly precede one
    /// declaration of the same name. The declaration is registered as the member; each signature
    /// becomes an <see cref="ObjectMethodSymbol"/> / <see cref="FunctionDeclarationSymbol"/> in
    /// its <c>Overloads</c> list with <see cref="SourceOverloadRole.Signature"/>.
    /// </summary>
    public partial class TyhpBinder
    {
        private readonly OverloadSignatureHelper.OverloadScan _overloadScan = new();

        private void ScanFunctionOverloads(IEnumerable<IBase2Ast?> siblingSequence)
        {
            // Cheap pre-check: most statement lists declare no overload signature at all.
            var anchor = siblingSequence.FirstOrDefault(
                s => s is PhpFunctionDeclAst f && OverloadSignatureHelper.IsErasableFunctionOverloadSignature(f));
            if (anchor is null || IsTyhpdefContext(anchor))
            {
                return;
            }

            var siblings = siblingSequence.ToList();
            var scan = OverloadSignatureHelper.ScanFunctions(siblings);
            if (scan.Sets.Count == 0 && scan.Issues.Count == 0)
            {
                return;
            }

            _overloadScan.Merge(scan);
            ReportOverloadStructureIssues(scan);
        }

        private void ReportOverloadStructureIssues(OverloadSignatureHelper.OverloadScan scan)
        {
            foreach (var issue in scan.Issues)
            {
                _diagnostics.AddErrorFromAst(
                    issue.Kind == OverloadSignatureHelper.OverloadStructureIssueKind.MissingImplementation
                        ? MessageCode.BinderOverloadMissingImplementation
                        : MessageCode.BinderOverloadNotAdjacent,
                    issue.Node,
                    _currentFileName,
                    issue.Name);
            }
        }

        private void BindFunctionOverloadSet(
            OverloadSignatureHelper.OverloadSetPlan plan,
            FunctionDeclarationSymbol registered,
            IBaseScope parentScope)
        {
            registered.OverloadRole = SourceOverloadRole.Implementation;
            var registeredAsync = registered.IsAsync;

            var seen = new List<string>();
            foreach (var signatureNode in plan.Signatures.OfType<PhpFunctionDeclAst>())
            {
                var overload = BindFunctionDeclCore(signatureNode, parentScope, registered);
                if (overload is null)
                {
                    continue;
                }

                overload.FullyQualifiedName = registered.FullyQualifiedName;
                if (overload.IsAsync != registeredAsync)
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderOverloadModifierMismatch, signatureNode, _currentFileName, overload.Name);
                }

                if (!TrackOverloadSpelling(seen, signatureNode, overload.Name))
                {
                    continue;
                }

                registered.Overloads.Add(overload);
            }

            WarnIfLoneOverload(plan, registered.Name);
        }

        private void BindMethodOverloadSet(
            OverloadSignatureHelper.OverloadSetPlan plan,
            ObjectMethodSymbol registered,
            ObjectDeclarationScope objScope)
        {
            if (registered is ObjectConstructorMethodSymbol or ObjectDestructorMethodSymbol)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderOverloadConstructorNotSupported,
                    plan.Signatures[0],
                    _currentFileName,
                    registered.Name);
                return;
            }

            registered.OverloadRole = plan.RegisteredIsImplementation
                ? SourceOverloadRole.Implementation
                : SourceOverloadRole.LastSignature;

            var registeredNode = (PhpMethodDeclAst)plan.Registered;
            var registeredShape = ModifierShape(registeredNode.Modifiers, registered.IsAsync);

            var seen = new List<string>();
            if (!plan.RegisteredIsImplementation)
            {
                seen.Add(OverloadSignatureHelper.Fingerprint(registeredNode));
            }

            foreach (var signatureNode in plan.Signatures.OfType<PhpMethodDeclAst>())
            {
                var overload = BindMethodDecl(signatureNode, objScope, registered);
                if (overload is null)
                {
                    continue;
                }

                var shape = ModifierShape(signatureNode.Modifiers, overload.IsAsync);
                if (shape != registeredShape)
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderOverloadModifierMismatch, signatureNode, _currentFileName, overload.Name);
                }

                if (!TrackOverloadSpelling(seen, signatureNode, overload.Name))
                {
                    continue;
                }

                registered.Overloads.Add(overload);
            }

            WarnIfLoneOverload(plan, registered.Name);
        }

        private bool TrackOverloadSpelling(List<string> seen, IBase2Ast signatureNode, string name)
        {
            var spelling = OverloadSignatureHelper.Fingerprint(signatureNode);
            if (seen.Contains(spelling, StringComparer.OrdinalIgnoreCase))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderOverloadDuplicate, signatureNode, _currentFileName, name);
                return false;
            }

            seen.Add(spelling);
            return true;
        }

        private void WarnIfLoneOverload(OverloadSignatureHelper.OverloadSetPlan plan, string name)
        {
            if (plan.RegisteredIsImplementation && plan.Signatures.Count == 1)
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderOverloadLone, plan.Signatures[0], _currentFileName, name);
            }
        }

        /// <summary>
        /// Static-ness, effective visibility, <c>async</c>, <c>abstract</c> and <c>final</c> — the
        /// modifiers every declaration of a set must share.
        /// </summary>
        private static MemberModifier ModifierShape(PhpModifierListAst? modifiers, bool isAsync)
        {
            var converted = ConvertModifiers(modifiers);
            var shape = converted & (MemberModifier.Static | MemberModifier.Abstract | MemberModifier.Final
                | MemberModifier.Public | MemberModifier.Protected | MemberModifier.Private);
            if ((shape & (MemberModifier.Public | MemberModifier.Protected | MemberModifier.Private)) == 0)
            {
                shape |= MemberModifier.Public;
            }

            return isAsync ? shape | MemberModifier.Async : shape;
        }
    }
}
