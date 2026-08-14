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
            foreach (var source in sources.OrderBy(static s => s.LoadOrder).ThenBy(static s => s.Ast.FileName, StringComparer.OrdinalIgnoreCase))
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
        }

        /// <summary>
        /// Records a newly registered tyhpdef symbol and its package source.
        /// </summary>
        internal void TrackSymbol(IBaseSymbol symbol, string packageSource)
        {
            if (symbol is not BaseSymbol baseSymbol || string.IsNullOrWhiteSpace(baseSymbol.FullyQualifiedName))
            {
                return;
            }

            _fqnPackageSources.TryAdd(baseSymbol.FullyQualifiedName, packageSource);
        }

        /// <summary>
        /// When duplicate registration fails, reports a cross-package conflict if applicable.
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

            _diagnostics.AddErrorFromAst(
                MessageCode.TyhpdefDuplicateFqnAcrossPackages,
                declaringNode,
                fileName,
                fqn,
                existingSource,
                currentPackageSource);
            return true;
        }
    }
}
