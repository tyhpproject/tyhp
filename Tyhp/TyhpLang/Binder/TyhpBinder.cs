using System;
using System.Collections.Generic;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// The Tyhp binder performs the declaration pass over parsed AST trees,
    /// producing a populated scope/symbol hierarchy rooted at a <see cref="GlobalScope"/>.
    /// </summary>
    public partial class TyhpBinder
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly CompilationOptions? _compilationOptions;
        private GlobalScope _globalScope = null!;
        private FileScope? _currentFileScope;
        private string _currentFileName = "";
        private int _bindDepth;
        private TyhpdefSymbolRegistrar? _tyhpdefRegistrar;
        private string _currentTyhpdefPackageSource = "<tyhpdef>";

        /// <summary>
        /// Creates a new binder instance.
        /// </summary>
        /// <param name="diagnostics">Diagnostic bag for reporting binding errors.</param>
        /// <param name="compilationOptions">Optional compilation options for tyhpdef discovery.</param>
        public TyhpBinder(DiagnosticBag diagnostics, CompilationOptions? compilationOptions = null)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _compilationOptions = compilationOptions;
        }

        /// <summary>
        /// Performs the declaration pass over parsed source files, producing a populated <see cref="GlobalScope"/>.
        /// </summary>
        /// <param name="parsedFiles">The parsed AST trees to bind.</param>
        /// <returns>A populated <see cref="GlobalScope"/>, or null if binding could not proceed.</returns>
        public GlobalScope? Bind(IReadOnlyList<SrcFileAst> parsedFiles)
        {
            if (parsedFiles == null || parsedFiles.Count == 0)
            {
                _diagnostics.AddError(MessageCode.BinderUnknownError, "<input>", 0, 0, "No source files provided for binding.");
                return null;
            }

            _globalScope = new GlobalScope();
            PopulateBuiltIns(_globalScope);

            LoadTyhpdefSymbols();

            // Pass 1: Declaration walk — register all declarations
            foreach (var srcFile in parsedFiles)
            {
                if (srcFile == null)
                {
                    _diagnostics.AddError(MessageCode.BinderUnknownError, "<input>", 0, 0,
                        "Null source file entry in parsedFiles list");
                    continue;
                }
                try
                {
                    BindFile(srcFile);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) // StackOverflowException and AccessViolationException cannot be caught in managed .NET
                {
                    _diagnostics.AddError(
                        MessageCode.BinderUnknownError,
                        srcFile?.FileName ?? "<unknown>",
                        0, 0,
                        $"Unexpected error binding file: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Pass 2: Name resolution — resolve type references on symbols
            try
            {
                RunResolutionPass();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) // StackOverflowException and AccessViolationException cannot be caught in managed .NET
            {
                _diagnostics.AddError(
                    MessageCode.BinderUnknownError,
                    "<resolution>",
                    0, 0,
                    $"Unexpected error during resolution pass: {ex.GetType().Name}: {ex.Message}");
            }

            return _globalScope;
        }

        /// <summary>
        /// Populates the global scope with built-in types, constants, and variables.
        /// </summary>
        private static void PopulateBuiltIns(GlobalScope globalScope)
        {
            Types.PopulateGlobal(globalScope);
            Constants.PopulateGlobal(globalScope);
            Variables.PopulateGlobal(globalScope);
            UtilityTypes.PopulateGlobal(globalScope);
            SymbolNameTypes.PopulateGlobal(globalScope);
            StructUtilityTypes.PopulateGlobal(globalScope);
            TypeNameAlgebraTypes.PopulateGlobal(globalScope);
            Functions.PopulateGlobal(globalScope);
        }

        /// <summary>
        /// Binds a single source file, creating its FileScope and walking its declarations.
        /// </summary>
        private void BindFile(SrcFileAst srcFile)
        {
            var fileName = srcFile.FileName ?? "<unknown>";
            var fileHash = srcFile.ValueString ?? "";

            if (!_globalScope.TryAddFileScope(fileName, fileHash, fileName, out var fileScope, _diagnostics))
            {
                return;
            }

            if (fileScope is null)
            {
                _diagnostics.AddError(MessageCode.BinderUnknownError, fileName, 0, 0,
                    "FileScope was unexpectedly null after successful TryAddFileScope");
                return;
            }

            _currentFileName = fileName;
            _currentFileScope = fileScope;

            SetOwningFileRecursive(srcFile, srcFile);

            foreach (var child in srcFile.AstChildren)
            {
                if (child == null) continue;

                if (child is PhpTopStatementListAst topStmtList)
                {
                    BindTopStatementList(topStmtList, fileScope);
                }
            }
        }

        /// <summary>
        /// Recursively sets the OwningFile property on an AST node and all its descendants.
        /// </summary>
        private static void SetOwningFileRecursive(IBase2Ast node, SrcFileAst owningFile)
        {
            node.OwningFile = owningFile;
            foreach (var child in node.AstChildren)
            {
                if (child != null)
                {
                    SetOwningFileRecursive(child, owningFile);
                }
            }
        }
    }
}
