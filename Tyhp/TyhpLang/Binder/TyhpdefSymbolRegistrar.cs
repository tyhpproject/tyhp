using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Registers parsed tyhpdef ASTs into the binder's <see cref="Scopes.GlobalScope"/>,
    /// tracking package origins for cross-package FQN conflict detection.
    /// </summary>
    public sealed class TyhpdefSymbolRegistrar
    {
        private readonly TyhpBinder _binder;
        private readonly DiagnosticBag _diagnostics;
        private readonly Dictionary<string, string> _fqnPackageSources = new(StringComparer.OrdinalIgnoreCase);

        public TyhpdefSymbolRegistrar(TyhpBinder binder, DiagnosticBag diagnostics)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Binds all tyhpdef sources in load-order.
        /// </summary>
        public void RegisterAll(IEnumerable<TyhpdefSourceFile> sources)
        {
            var list = sources as IList<TyhpdefSourceFile> ?? sources.ToList();
            _binder.NoteTyhpdefPackages(list.Select(static source => source.PackageSource));
            foreach (var source in list
                .Where(static s => !s.IsOverlay)
                .OrderBy(static s => s.LoadOrder)
                .ThenBy(static s => s.Ast.FileName, StringComparer.OrdinalIgnoreCase))
            {
                BindOne(source);
            }

            _binder.ResolveFallbackFunctions();
            _binder.CaptureTyhpdefLayer1Stamps();

            foreach (var source in list
                .Where(static s => s.IsOverlay)
                .OrderBy(static s => s.OverlaySequence))
            {
                BindOne(source);
            }

            _binder.ResolveFallbackFunctions();
        }

        private void BindOne(TyhpdefSourceFile source)
        {
            try
            {
                _binder.BindTyhpdefSourceFile(source);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _diagnostics.AddError(
                    MessageCode.TyhpdefBindError,
                    source.Ast?.FileName ?? "<tyhpdef>",
                    0,
                    0,
                    ex.Message);
            }
        }

        /// <summary>
        /// Records a newly registered tyhpdef symbol and its package source.
        /// </summary>
        internal bool TryGetPackageSource(IBaseSymbol symbol, out string packageSource)
        {
            packageSource = "";
            if (symbol is not BaseSymbol baseSymbol || string.IsNullOrWhiteSpace(baseSymbol.FullyQualifiedName))
            {
                return false;
            }

            return _fqnPackageSources.TryGetValue(baseSymbol.FullyQualifiedName, out packageSource!);
        }

        internal void TrackSymbol(IBaseSymbol symbol, string packageSource)
        {
            if (symbol is not BaseSymbol baseSymbol || string.IsNullOrWhiteSpace(baseSymbol.FullyQualifiedName))
            {
                return;
            }

            _fqnPackageSources.TryAdd(baseSymbol.FullyQualifiedName, packageSource);
        }

        /// <summary>
        /// Drops a previously tracked FQN so an overlay replace can re-register it.
        /// </summary>
        internal void UntrackFullyQualifiedName(string? fullyQualifiedName)
        {
            if (string.IsNullOrWhiteSpace(fullyQualifiedName))
            {
                return;
            }

            _fqnPackageSources.Remove(fullyQualifiedName);
        }

        /// <summary>
        /// Drops include-time <see cref="MessageCode.TyhpdefDuplicateFqnAcrossPackages"/>
        /// diagnostics for <paramref name="omittedSymbol"/> after an overlay
        /// <c>omit</c> removes that declaration. A class, function, and const may share
        /// an FQCN; only the diagnostic that labels this declaration is retracted.
        /// </summary>
        internal void RetractCrossPackageDuplicate(BaseSymbol omittedSymbol)
        {
            var fullyQualifiedName = omittedSymbol.FullyQualifiedName;
            if (string.IsNullOrWhiteSpace(fullyQualifiedName))
            {
                return;
            }

            var nameComparison = omittedSymbol is ConstantSymbol
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            _diagnostics.RetractWhere(diagnostic =>
                diagnostic.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages
                && diagnostic.FormatParams.Length > 0
                && string.Equals(
                    Convert.ToString(diagnostic.FormatParams[0]),
                    fullyQualifiedName,
                    nameComparison)
                && LabelsOmittedDeclaration(diagnostic, omittedSymbol));
        }

        private static bool LabelsOmittedDeclaration(IDiagnostic diagnostic, BaseSymbol omittedSymbol)
        {
            var declaringNode = omittedSymbol.DeclaringAstNode;
            var line = declaringNode != null ? Math.Max(1, declaringNode.Line) : omittedSymbol.Line;
            var column = declaringNode != null ? Math.Max(0, declaringNode.Column) : omittedSymbol.Column;
            foreach (var label in diagnostic.Labels)
            {
                if (label.Span.Line == line
                    && label.Span.Column == column
                    && string.Equals(label.Span.FileName, omittedSymbol.SourceFile, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// When duplicate registration fails, reports a cross-package conflict if applicable.
        /// Real-wins merge for <c>extern</c> vs a real type of the same FQCN and kind is applied
        /// in <c>TyhpBinder.TryConsumeTyhpdefExternMerge</c> before this path; those replacements
        /// never reach <see cref="MessageCode.TyhpdefDuplicateFqnAcrossPackages"/>.
        /// </summary>
        internal bool TryReportCrossPackageConflict(
            IBaseSymbol? existingSymbol,
            BaseSymbol duplicateSymbol,
            IBase2Ast declaringNode,
            string currentPackageSource,
            string fileName
        )
        {
            if (existingSymbol is not BaseSymbol existingBase)
            {
                return false;
            }

            var fqn = existingBase.FullyQualifiedName;
            if (string.IsNullOrWhiteSpace(fqn))
            {
                return false;
            }

            if (!_fqnPackageSources.TryGetValue(fqn, out var existingSource))
            {
                return false;
            }

            if (string.Equals(existingSource, currentPackageSource, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _diagnostics.AddDuplicateFromAst(
                MessageCode.TyhpdefDuplicateFqnAcrossPackages,
                declaringNode,
                fileName,
                existingSymbol,
                fqn,
                existingSource,
                currentPackageSource);
            return true;
        }
    }
}
