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
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
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
        private bool _tyhpdefDuplicateMemberErrors;
        private bool _tyhpdefIsOverlay;
        private bool _tyhpdefOverlayMemberReplace;
        private HashSet<string>? _overlayReplacedFunctionNames;
        private HashSet<string>? _overlayReplacedMemberNames;
        private HashSet<string>? _overlayPartialKeptNames;
        private List<OverlayPartialPendingRename>? _overlayPartialPendingRenames;
        private List<OverlayPartialTypeHeaderNoop>? _overlayPartialTypeHeaderNoops;
        private Dictionary<string, string>? _tyhpdefLayer1Stamps;
        private readonly List<string> _phpVersionConstraintStack = [];
        private readonly List<string> _extGateStack = [];
        private List<string>? _pendingFileExtGates;
        private bool _currentFilePhpGateInactive;
        private int _phpDeclareBlockDepth;

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

            _phpVersionGatedDeclarations.Clear();
            _uncompiledVersionVariants.Clear();
            _declarationNameResolver = null;
            _pendingFileUseExtensions.Clear();
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

            ReportReservedPhp86Names();

            MarkDeclarationsWithUncompiledVersionVariants();

            // Pass 2: Name resolution — resolve type references on symbols.
            // File-level `use extension` / `global use extension` wait until every declaration
            // exists (same timing as class-body pending paths) so a forward reference is not a
            // silent no-op and a missing name reports TyhpdefExtensionNotFound.
            try
            {
                ResolvePendingFileUseExtensions();
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
            SymbolNameTypes.PopulateGlobal(globalScope);
            StructUtilityTypes.PopulateGlobal(globalScope);
            MagicUtilityTypes.PopulateGlobal(globalScope);
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

            var previousInactive = _currentFilePhpGateInactive;
            var previousStackCount = _phpVersionConstraintStack.Count;
            var previousExtStackCount = _extGateStack.Count;
            var previousBlockDepth = _phpDeclareBlockDepth;
            try
            {
                ApplyFileLevelPhpGates(srcFile, fileScope.DeclarationSymbol);

                foreach (var child in srcFile.AstChildren)
                {
                    if (child == null) continue;

                    if (child is PhpTopStatementListAst topStmtList)
                    {
                        BindTopStatementList(topStmtList, fileScope);
                    }
                }
            }
            finally
            {
                _phpDeclareBlockDepth = previousBlockDepth;
                _currentFilePhpGateInactive = previousInactive;
                while (_phpVersionConstraintStack.Count > previousStackCount)
                {
                    _phpVersionConstraintStack.RemoveAt(_phpVersionConstraintStack.Count - 1);
                }

                while (_extGateStack.Count > previousExtStackCount)
                {
                    _extGateStack.RemoveAt(_extGateStack.Count - 1);
                }
            }
        }

        /// <summary>
        /// Reports a binder duplicate-name error, labeling the first declaration when known.
        /// Include-layer tyhpdef member clashes use <see cref="MessageCode.TyhpdefDuplicateDeclaration"/>
        /// with the same <c>declared here</c> label when the original span is known.
        /// </summary>
        private void ReportBinderOrTyhpdefDuplicate(
            IBase2Ast node,
            IBaseSymbol? existing,
            string name)
        {
            if (_tyhpdefDuplicateMemberErrors)
            {
                _diagnostics.AddDuplicateFromAst(
                    MessageCode.TyhpdefDuplicateDeclaration,
                    node,
                    _currentFileName,
                    existing,
                    name);
                return;
            }

            ReportBinderDuplicate(node, existing, name);
        }

        private void ReportBinderDuplicate(
            IBase2Ast node,
            IBaseSymbol? existing,
            string name,
            MessageCode code = MessageCode.BinderDuplicateSymbolDeclaration)
        {
            _diagnostics.AddDuplicateFromAst(code, node, _currentFileName, existing, name);
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
