using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter.Splice;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    internal static class PHPOutputFileSplitter
    {
        private const string OutputFileDeclareKey = "output_file";
        private const string AutoloadDeclareKey = "autoload";
        private const string PhpDeclareKey = "php";

        public static IEnumerable<PHPOutputFile> Split(SrcFileAst srcFile, EmitContext context)
        {
            // Inactive file-level `declare(php=…);` skips the whole file (binder already
            // omitted its symbols). Match that by producing no PHP output units.
            if (IsPhpVersionGateInactiveFile(srcFile, context))
            {
                return [];
            }

            var collector = new SplitCollector(srcFile, context);

            foreach (var child in srcFile.AstChildren)
            {
                switch (child)
                {
                    case PhpTopStatementListAst topList:
                        collector.ProcessTopStatementList(topList, parentNamespace: null, isAnonymousNamespace: false, atFileLevel: true);
                        break;
                    case PhpInlineOutputAst inlineOutput:
                        collector.AddInlineOutput(inlineOutput);
                        break;
                }
            }

            return collector.Finalize();
        }

        private sealed class SplitCollector
        {
            private readonly SrcFileAst _srcFile;
            private readonly EmitContext _context;
            private readonly List<PhpDeclareAst> _fileDeclares = [];
            private readonly List<PhpImportDeclListAst> _fileImports = [];
            private readonly Dictionary<string, List<PhpImportDeclListAst>> _namespaceImports = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<PhpDeclareAst>> _namespaceDeclares = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<PHPOutputFile> _objectFiles = [];
            private readonly Dictionary<string, NamespaceFunctionBucket> _functionBuckets = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<PHPOutputFile> _declareOutputFiles = [];
            private readonly List<PHPOutputFile> _entryPointFiles = [];
            private readonly List<ITopStatement> _pendingRootStatements = [];
            private string? _singleOutputFilePath;
            private bool _hasAutoloadDeclare;
            private string _autoloadDeclare = "";
            private ITopStatement? _currentNamespaceStatement;
            private string? _currentNamespaceName;
            private bool _isAnonymousNamespace;

            public SplitCollector(SrcFileAst srcFile, EmitContext context)
            {
                this._srcFile = srcFile;
                this._context = context;
            }

            private bool HasRuntimeGates(IBase2Ast node)
                => RuntimeGateEmission.GetConditions(node, this._context.Config.TargetPhpVersion).Count > 0;

            public void ProcessTopStatementList(
                PhpTopStatementListAst topList,
                ITopStatement? parentNamespace,
                bool isAnonymousNamespace,
                bool atFileLevel)
            {
                var previousNamespaceStatement = this._currentNamespaceStatement;
                var previousNamespaceName = this._currentNamespaceName;
                var previousAnonymous = this._isAnonymousNamespace;

                if (parentNamespace != null)
                {
                    this._currentNamespaceStatement = parentNamespace;
                    this._currentNamespaceName = GetNamespaceName(parentNamespace);
                    this._isAnonymousNamespace = isAnonymousNamespace;
                }

                var statements = topList.GetAllNotNull().ToList();
                var index = 0;

                if (atFileLevel)
                {
                    while (index < statements.Count && this.TryCollectFileLevelStatement(statements[index]))
                    {
                        index += 1;
                    }
                }

                for (; index < statements.Count; index++)
                {
                    this.ClassifyStatement(
                        statements[index],
                        singleFileMode: !string.IsNullOrWhiteSpace(this._singleOutputFilePath));
                }

                // Flush before restoring so entry-point root code keeps the namespace that was
                // active while the statements were classified. Statement-style `namespace Foo;`
                // sets context during the walk; restoring first (especially at file level back
                // to null) was dropping FileNameSpace and emitting bare `exit(main())`.
                this.FlushRootCode();
                this.RestoreNamespaceContext(previousNamespaceStatement, previousNamespaceName, previousAnonymous);
            }

            private bool TryCollectFileLevelStatement(ITopStatement statement)
            {
                if (statement is PhpImportDeclListAst importList)
                {
                    this._fileImports.Add(importList);
                    return true;
                }

                if (IsCompileTimeOnlyTopStatement(statement))
                {
                    return true;
                }

                return this.TryCollectFileDeclare(statement, atFileLevel: true);
            }

            public void AddInlineOutput(PhpInlineOutputAst inlineOutput)
            {
                if (inlineOutput.IsEcho)
                {
                    this._pendingRootStatements.Add(inlineOutput);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(inlineOutput.Content))
                {
                    this._pendingRootStatements.Add(inlineOutput);
                }
            }

            public IEnumerable<PHPOutputFile> Finalize()
            {
                this.FlushRootCode();

                foreach (var bucket in this._functionBuckets.Values)
                {
                    if (bucket.Statements.Count == 0 && bucket.GatedStatements.Count == 0)
                    {
                        continue;
                    }

                    // Ungated declarations first; existence-gated functions evaluate last.
                    var statements = new List<ITopStatement>(
                        bucket.Statements.Count + bucket.GatedStatements.Count);
                    statements.AddRange(bucket.Statements);
                    statements.AddRange(bucket.GatedStatements);

                    yield return this.CreateOutputFile(
                        statements,
                        OutputPathResolver.ResolveNamespaceFunctionsPath(bucket.NamespaceName, this._context.Config),
                        isPsr4Object: false,
                        namespaceStatement: bucket.NamespaceStatement,
                        isAnonymousNamespace: bucket.IsAnonymousNamespace);
                }

                foreach (var objectFile in this._objectFiles)
                {
                    yield return objectFile;
                }

                foreach (var declareFile in this._declareOutputFiles)
                {
                    yield return declareFile;
                }

                foreach (var entryPoint in this._entryPointFiles)
                {
                    yield return entryPoint;
                }
            }

            private void ClassifyStatement(ITopStatement statement, bool singleFileMode)
            {
                if (singleFileMode)
                {
                    this.AddToSingleOutputFile(statement);
                    return;
                }

                switch (statement)
                {
                    case PhpImportDeclListAst importList:
                        this.AddImportList(importList);
                        break;

                    case TyhpImportExtensionAst:
                        // `use extension` / `global use extension` are compile-time only;
                        // they never emit PHP and are not library entrypoints (TYHP7505).
                        break;

                    case PhpDeclareAst declareAst:
                        this.HandleDeclareStatement(declareAst, singleFileMode);
                        break;

                    case PhpNamespaceDeclAst namespaceDecl:
                        this.ProcessNamespace(namespaceDecl);
                        break;

                    case PhpBlockNamespaceDeclAst blockNamespaceDecl:
                        this.ProcessBlockNamespace(blockNamespaceDecl);
                        break;

                    case TyhpStructDeclAst:
                        break;

                    case TyhpTypeAliasAst typeAlias:
                        if (ShouldEmitSourceTypeAliasFactory(typeAlias)
                            && ShouldEmitPhpVersionGatedDeclaration(typeAlias)
                            && !NoEmitAttributeSupport.ShouldOmitDeclaration(typeAlias))
                        {
                            this.AddNamespaceFunction(typeAlias);
                        }

                        break;

                    case PhpObjectTypeDeclAst objectDecl when !IsWrappedTopStatement(statement):
                        if (ShouldEmitPhpVersionGatedDeclaration(objectDecl)
                            && !NoEmitAttributeSupport.ShouldOmitDeclaration(objectDecl))
                        {
                            this.AddObjectFile(objectDecl);
                        }

                        break;

                    case TyhpExtensionDeclAst extensionDecl when !IsWrappedTopStatement(statement):
                        if (ShouldEmitPhpVersionGatedDeclaration(extensionDecl)
                            && !NoEmitAttributeSupport.ShouldOmitDeclaration(extensionDecl)
                            && SpliceAst.ExtensionDeclEmitsPhpBackerClass(extensionDecl))
                        {
                            this.AddExtensionFile(extensionDecl);
                        }

                        break;

                    case PhpFunctionDeclAst functionDecl:
                        if (ShouldEmitPhpVersionGatedDeclaration(functionDecl))
                        {
                            if (FallbackDeclaration.IsFallback(functionDecl) || this.HasRuntimeGates(functionDecl))
                            {
                                // `fallback` / runtime-gated functions emit inside an `if`; like other
                                // existence gates they are evaluated after the plain declarations.
                                this.GetFunctionBucket().GatedStatements.Add(functionDecl);
                            }
                            else
                            {
                                this.AddNamespaceFunction(functionDecl);
                            }
                        }

                        break;

                    case PhpConstDeclListAst constList:
                        if (ShouldEmitPhpVersionGatedDeclaration(constList))
                        {
                            if (FallbackDeclaration.IsFallback(constList) || this.HasRuntimeGates(constList))
                            {
                                this.GetFunctionBucket().GatedStatements.Add(constList);
                            }
                            else
                            {
                                this.AddNamespaceConstants(constList);
                            }
                        }

                        break;

                    case PhpIfAst ifAst
                        when DeclarationExistenceGateHelper.TryGetValidExistenceGate(
                            ifAst,
                            this._currentNamespaceName,
                            out _,
                            out var gatedDecl):
                        this.AddGatedDeclaration(ifAst, gatedDecl);
                        break;

                    default:
                        if (IsWrappedTopStatement(statement))
                        {
                            this._pendingRootStatements.Add(statement);
                        }
                        else if (IsDeclarationStatement(statement))
                        {
                            break;
                        }
                        else
                        {
                            this._pendingRootStatements.Add(statement);
                        }

                        break;
                }
            }

            private void AddGatedDeclaration(PhpIfAst gate, ITopStatement gatedDecl)
            {
                switch (gatedDecl)
                {
                    case PhpFunctionDeclAst:
                    case TyhpTypeAliasAst:
                        this.GetFunctionBucket().GatedStatements.Add(gate);
                        break;

                    case PhpObjectTypeDeclAst objectDecl:
                        this.AddGatedObjectFile(gate, objectDecl);
                        break;

                    default:
                        this._pendingRootStatements.Add(gate);
                        break;
                }
            }

            private void AddGatedObjectFile(PhpIfAst gate, PhpObjectTypeDeclAst objectDecl)
            {
                var fullyQualifiedName = GetFullyQualifiedName(objectDecl);
                var outputFile = this.CreateOutputFile(
                    [gate],
                    OutputPathResolver.ResolveObjectPath(fullyQualifiedName, this._context.Config),
                    isPsr4Object: true,
                    namespaceStatement: this._currentNamespaceStatement,
                    isAnonymousNamespace: this._isAnonymousNamespace);
                this._objectFiles.Add(outputFile);
            }

            private void ProcessNamespace(PhpNamespaceDeclAst namespaceDecl)
            {
                if (namespaceDecl.TopStatements != null)
                {
                    var isAnonymous = string.IsNullOrWhiteSpace(namespaceDecl.Identifier);
                    this.ProcessTopStatementList(
                        namespaceDecl.TopStatements,
                        parentNamespace: namespaceDecl,
                        isAnonymousNamespace: isAnonymous,
                        atFileLevel: false);
                    return;
                }

                // Statement-style namespace switch: flush any root code that belonged to the
                // previous namespace before adopting the new one.
                this.FlushRootCode();

                this._currentNamespaceStatement = namespaceDecl;
                this._currentNamespaceName = GetNamespaceName(namespaceDecl);
                this._isAnonymousNamespace = string.IsNullOrWhiteSpace(namespaceDecl.Identifier);
            }

            private void ProcessBlockNamespace(PhpBlockNamespaceDeclAst blockNamespaceDecl)
            {
                var isAnonymous = string.IsNullOrWhiteSpace(blockNamespaceDecl.Identifier);
                if (blockNamespaceDecl.TopStatements != null)
                {
                    this.ProcessTopStatementList(
                        blockNamespaceDecl.TopStatements,
                        parentNamespace: blockNamespaceDecl,
                        isAnonymousNamespace: isAnonymous,
                        atFileLevel: false);
                }
            }

            private void AddObjectFile(PhpObjectTypeDeclAst objectDecl)
            {
                var fullyQualifiedName = GetFullyQualifiedName(objectDecl);
                var outputFile = this.CreateOutputFile(
                    [objectDecl],
                    OutputPathResolver.ResolveObjectPath(fullyQualifiedName, this._context.Config),
                    isPsr4Object: true,
                    namespaceStatement: this._currentNamespaceStatement,
                    isAnonymousNamespace: this._isAnonymousNamespace);
                this._objectFiles.Add(outputFile);
            }

            private void AddExtensionFile(TyhpExtensionDeclAst extensionDecl)
            {
                var fullyQualifiedName = GetFullyQualifiedName(extensionDecl);
                var outputFile = this.CreateOutputFile(
                    [extensionDecl],
                    OutputPathResolver.ResolveObjectPath(fullyQualifiedName, this._context.Config),
                    isPsr4Object: true,
                    namespaceStatement: this._currentNamespaceStatement,
                    isAnonymousNamespace: this._isAnonymousNamespace);
                this._objectFiles.Add(outputFile);
            }

            private void AddNamespaceFunction(ITopStatement functionOrAlias)
            {
                this.GetFunctionBucket().Statements.Add(functionOrAlias);
            }

            /// <summary>
            /// Source <c>.tyhp</c> type aliases emit a Type factory in the namespace functions
            /// file. Tyhpdef aliases stay type-only (no PHP function).
            /// </summary>
            private bool ShouldEmitSourceTypeAliasFactory(TyhpTypeAliasAst alias)
            {
                if (string.Equals(alias.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var file = this._srcFile.FileName
                    ?? this._srcFile.Identifier
                    ?? alias.BoundSymbol?.SourceFile
                    ?? string.Empty;
                return !file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                    && !file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase);
            }

            private void AddNamespaceConstants(ITopStatement constantStatement)
            {
                this.GetFunctionBucket().Statements.Add(constantStatement);
            }

            private NamespaceFunctionBucket GetFunctionBucket()
            {
                var key = this._currentNamespaceName ?? "";
                if (!this._functionBuckets.TryGetValue(key, out var bucket))
                {
                    bucket = new NamespaceFunctionBucket(
                        this._currentNamespaceName,
                        this._currentNamespaceStatement,
                        this._isAnonymousNamespace);
                    this._functionBuckets[key] = bucket;
                }

                return bucket;
            }

            private void AddDeclareOutputFile(PhpDeclareAst declareAst, string declaredPath)
            {
                var statements = new List<ITopStatement>();
                if (declareAst.Body != null)
                {
                    this.CollectDeclareBodyStatements(declareAst.Body, statements);
                }

                var outputFile = this.CreateOutputFile(
                    statements,
                    OutputPathResolver.ResolveOutputFilePath(declaredPath, this._context.Config),
                    isPsr4Object: false,
                    namespaceStatement: this._currentNamespaceStatement,
                    isAnonymousNamespace: this._isAnonymousNamespace);
                this._declareOutputFiles.Add(outputFile);
            }

            private void AddToSingleOutputFile(ITopStatement statement)
            {
                if (statement is TyhpStructDeclAst || IsCompileTimeOnlyTopStatement(statement))
                {
                    return;
                }

                if (statement is TyhpTypeAliasAst typeAlias
                    && !ShouldEmitSourceTypeAliasFactory(typeAlias))
                {
                    return;
                }

                if (statement is IBase2Ast gated && !ShouldEmitPhpVersionGatedDeclaration(gated))
                {
                    return;
                }

                if (statement is PhpObjectTypeDeclAst or TyhpExtensionDeclAst
                    && NoEmitAttributeSupport.ShouldOmitDeclaration((IBase2Ast)statement))
                {
                    return;
                }

                if (statement is PhpNamespaceDeclAst namespaceDecl && namespaceDecl.TopStatements != null)
                {
                    foreach (var nested in namespaceDecl.TopStatements.GetAllNotNull())
                    {
                        this.AddToSingleOutputFile(nested);
                    }

                    return;
                }

                if (statement is PhpBlockNamespaceDeclAst blockNamespace && blockNamespace.TopStatements != null)
                {
                    foreach (var nested in blockNamespace.TopStatements.GetAllNotNull())
                    {
                        this.AddToSingleOutputFile(nested);
                    }

                    return;
                }

                if (statement is PhpDeclareAst declareAst && declareAst.Body != null)
                {
                    if (IsPhpOnlyDeclare(declareAst) && IsPhpVersionGateInactive(declareAst))
                    {
                        return;
                    }

                    this.CollectDeclareBodyStatements(declareAst.Body, this._pendingRootStatements);
                    return;
                }

                this._pendingRootStatements.Add(statement);
            }

            private void FlushRootCode()
            {
                if (this._pendingRootStatements.Count == 0)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(this._singleOutputFilePath))
                {
                    var singleFile = this.CreateOutputFile(
                        this._pendingRootStatements,
                        OutputPathResolver.ResolveOutputFilePath(this._singleOutputFilePath, this._context.Config),
                        isPsr4Object: false,
                        namespaceStatement: this._currentNamespaceStatement,
                        isAnonymousNamespace: this._isAnonymousNamespace,
                        isEntryPoint: true);
                    this._entryPointFiles.Add(singleFile);
                    this._pendingRootStatements.Clear();
                    return;
                }

                var entryPoint = this.CreateOutputFile(
                    this._pendingRootStatements,
                    OutputPathResolver.ResolveEntryPointPath(this._srcFile.Identifier, this._context.Config),
                    isPsr4Object: false,
                    namespaceStatement: this._currentNamespaceStatement,
                    isAnonymousNamespace: this._isAnonymousNamespace,
                    isEntryPoint: true);
                this._entryPointFiles.Add(entryPoint);
                this._pendingRootStatements.Clear();
            }

            private void HandleDeclareStatement(PhpDeclareAst declareAst, bool singleFileMode)
            {
                this.TryCaptureAutoloadDeclare(declareAst);

                var outputFilePath = GetOutputFileDirectiveValue(declareAst);
                if (!string.IsNullOrWhiteSpace(outputFilePath))
                {
                    if (IsDeclareBlockBody(declareAst.Body))
                    {
                        this.AddDeclareOutputFile(declareAst, outputFilePath);
                        return;
                    }

                    if (!singleFileMode)
                    {
                        this._singleOutputFilePath = outputFilePath;
                    }

                    return;
                }

                // `declare(php=…) { … }` is compile-time only: unwrap the body so inner
                // declarations classify as PSR-4 / functions / entry-point, like
                // `output_file` extracting its body rather than emitting a PHP declare.
                if (IsPhpOnlyDeclare(declareAst) && IsDeclareBlockBody(declareAst.Body))
                {
                    if (IsPhpVersionGateInactive(declareAst) || declareAst.Body == null)
                    {
                        return;
                    }

                    foreach (var inner in EnumerateDeclareBodyStatements(declareAst.Body))
                    {
                        this.ClassifyStatement(inner, singleFileMode);
                    }

                    return;
                }

                if (IsPhpOnlyDeclare(declareAst))
                {
                    return;
                }

                if (this._currentNamespaceStatement != null || !string.IsNullOrWhiteSpace(this._currentNamespaceName))
                {
                    this.AddNamespaceDeclare(declareAst);
                    return;
                }

                if (!this._fileDeclares.Contains(declareAst))
                {
                    this._fileDeclares.Add(declareAst);
                }
            }

            private void AddNamespaceDeclare(PhpDeclareAst declareAst)
            {
                var key = this._currentNamespaceName ?? "";
                if (!this._namespaceDeclares.TryGetValue(key, out var namespaceDeclares))
                {
                    namespaceDeclares = [];
                    this._namespaceDeclares[key] = namespaceDeclares;
                }

                namespaceDeclares.Add(declareAst);
            }

            private void AddImportList(PhpImportDeclListAst importList)
            {
                if (this._currentNamespaceStatement != null || !string.IsNullOrWhiteSpace(this._currentNamespaceName))
                {
                    var key = this._currentNamespaceName ?? "";
                    if (!this._namespaceImports.TryGetValue(key, out var namespaceImports))
                    {
                        namespaceImports = [];
                        this._namespaceImports[key] = namespaceImports;
                    }

                    namespaceImports.Add(importList);
                    return;
                }

                this._fileImports.Add(importList);
            }

            private List<PhpDeclareAst> GetDeclaresForNamespace(ITopStatement? namespaceStatement)
            {
                var declares = new List<PhpDeclareAst>(this._fileDeclares);
                var namespaceName = GetNamespaceName(namespaceStatement) ?? "";
                if (this._namespaceDeclares.TryGetValue(namespaceName, out var namespaceDeclares))
                {
                    declares.AddRange(namespaceDeclares);
                }

                return declares;
            }

            private List<PhpImportDeclListAst> GetImportsForNamespace(ITopStatement? namespaceStatement)
            {
                var imports = new List<PhpImportDeclListAst>(this._fileImports);
                var namespaceName = GetNamespaceName(namespaceStatement) ?? "";
                if (this._namespaceImports.TryGetValue(namespaceName, out var namespaceImports))
                {
                    imports.AddRange(namespaceImports);
                }

                return imports;
            }

            private PHPOutputFile CreateOutputFile(
                IEnumerable<ITopStatement> statements,
                string outputFilePath,
                bool isPsr4Object,
                ITopStatement? namespaceStatement,
                bool isAnonymousNamespace,
                bool isEntryPoint = false)
            {
                return new PHPOutputFile
                {
                    SourceFileAst = this._srcFile,
                    SourceFileName = this._srcFile.FileName,
                    OutputFilePath = outputFilePath,
                    FileDeclares = this.GetDeclaresForNamespace(namespaceStatement),
                    FileImports = this.GetImportsForNamespace(namespaceStatement),
                    FileNameSpace = namespaceStatement,
                    Statements = statements.ToList(),
                    IsPSR4ObjectDeclaration = isPsr4Object,
                    IsAnonymousNamespace = isAnonymousNamespace,
                    IsEntryPoint = isEntryPoint,
                    HasAutoloadDeclare = this._hasAutoloadDeclare,
                    AutoloadDeclare = this._autoloadDeclare,
                };
            }

            private bool TryCollectFileDeclare(ITopStatement statement, bool atFileLevel)
            {
                if (statement is not PhpDeclareAst declareAst || !atFileLevel)
                {
                    return false;
                }

                this.TryCaptureAutoloadDeclare(declareAst);

                var outputFilePath = GetOutputFileDirectiveValue(declareAst);
                if (!string.IsNullOrWhiteSpace(outputFilePath))
                {
                    if (IsDeclareBlockBody(declareAst.Body))
                    {
                        return false;
                    }

                    this._singleOutputFilePath = outputFilePath;
                    return true;
                }

                if (IsPhpOnlyDeclare(declareAst))
                {
                    // Empty `declare(php=…);` is Tyhp-only (like `output_file` without a body):
                    // consume it so it never lands in FileDeclares. Blocks return false so
                    // ClassifyStatement can unwrap the body.
                    return !IsDeclareBlockBody(declareAst.Body);
                }

                this._fileDeclares.Add(declareAst);
                return true;
            }

            private void TryCaptureAutoloadDeclare(PhpDeclareAst declareAst)
            {
                foreach (var decl in declareAst.Declarations?.GetAllNotNull() ?? [])
                {
                    if (!string.Equals(decl.Identifier, AutoloadDeclareKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    this._hasAutoloadDeclare = true;
                    this._autoloadDeclare = GetConstStringValue(decl) ?? "";
                    return;
                }
            }

            private static bool IsDeclareBlockBody(IStatement? body)
                => body is PhpStatementBlockAst block && block.GetAllNotNull().Any();

            private static string? GetNamespaceName(ITopStatement? namespaceStatement)
                => namespaceStatement switch
                {
                    PhpNamespaceDeclAst namespaceDecl => string.IsNullOrWhiteSpace(namespaceDecl.Identifier)
                        ? null
                        : namespaceDecl.Identifier,
                    PhpBlockNamespaceDeclAst blockNamespace => string.IsNullOrWhiteSpace(blockNamespace.Identifier)
                        ? null
                        : blockNamespace.Identifier,
                    _ => null,
                };

            private void RestoreNamespaceContext(
                ITopStatement? namespaceStatement,
                string? namespaceName,
                bool isAnonymousNamespace)
            {
                this._currentNamespaceStatement = namespaceStatement;
                this._currentNamespaceName = namespaceName;
                this._isAnonymousNamespace = isAnonymousNamespace;
            }

            private void CollectDeclareBodyStatements(IStatement body, ICollection<ITopStatement> target)
            {
                foreach (var stmt in EnumerateDeclareBodyStatements(body))
                {
                    if (!IsCompileTimeOnlyTopStatement(stmt))
                    {
                        target.Add(stmt);
                    }
                }
            }

            private static IEnumerable<ITopStatement> EnumerateDeclareBodyStatements(IStatement body)
            {
                switch (body)
                {
                    case PhpStatementBlockAst block:
                        foreach (var stmt in block.GetAllNotNull())
                        {
                            yield return stmt;
                        }

                        break;
                    case ITopStatement topStatement:
                        yield return topStatement;
                        break;
                }
            }

            private static string? GetOutputFileDirectiveValue(PhpDeclareAst declareAst)
            {
                foreach (var decl in declareAst.Declarations?.GetAllNotNull() ?? [])
                {
                    if (string.Equals(decl.Identifier, OutputFileDeclareKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return GetConstStringValue(decl);
                    }
                }

                return null;
            }

            private static string? GetConstStringValue(PhpConstDeclAst constDecl)
                => GetExpressionStringValue(constDecl.Value);

            private static string? GetExpressionStringValue(IExpression? expression)
            {
                switch (expression)
                {
                    case PhpEncapsStringAst encapsString:
                        return UnquotePhpStringLiteral(encapsString.ValueString);
                    case PhpEncapsListAst encapsList:
                        return string.Concat(
                            encapsList.GetAllNotNull()
                                .OfType<PhpEncapsStringAst>()
                                .Select(part => UnquotePhpStringLiteral(part.ValueString) ?? ""));
                    case IBase2Ast valueAst when !string.IsNullOrWhiteSpace(valueAst.ValueString):
                        return UnquotePhpStringLiteral(valueAst.ValueString);
                    default:
                        return null;
                }
            }

            private static string? UnquotePhpStringLiteral(string? literal)
            {
                if (string.IsNullOrEmpty(literal) || literal.Length < 2)
                {
                    return literal;
                }

                if ((literal.StartsWith('"') && literal.EndsWith('"'))
                    || (literal.StartsWith('\'') && literal.EndsWith('\'')))
                {
                    return literal[1..^1];
                }

                return literal;
            }

            private string GetFullyQualifiedName(IBase2Ast declaration)
            {
                if (declaration.BoundSymbol is IBaseSymbol symbol && !string.IsNullOrWhiteSpace(symbol.FullyQualifiedName))
                {
                    return symbol.FullyQualifiedName;
                }

                var shortName = declaration.Identifier ?? "Unknown";
                if (shortName.StartsWith('\\'))
                {
                    return shortName;
                }

                return string.IsNullOrWhiteSpace(this._currentNamespaceName)
                    ? "\\" + shortName
                    : "\\" + this._currentNamespaceName + "\\" + shortName;
            }

            private static bool IsCompileTimeOnlyTopStatement(ITopStatement statement)
                => statement is TyhpImportExtensionAst;

            private static bool IsDeclarationStatement(ITopStatement statement)
                => statement is PhpObjectTypeDeclAst
                    or TyhpExtensionDeclAst
                    or PhpFunctionDeclAst
                    or TyhpStructDeclAst
                    or TyhpTypeAliasAst
                    or PhpConstDeclListAst
                    or TyhpImportExtensionAst;

            private static bool IsWrappedTopStatement(ITopStatement statement)
                => statement is IStatement stmt && IsWrappedObjectDeclaration(stmt);

            private static bool IsWrappedObjectDeclaration(IStatement statement)
            {
                return statement switch
                {
                    PhpIfAst ifAst => ContainsObjectDeclaration(ifAst.ThenStatement)
                        || ContainsObjectDeclaration(ifAst.ElseStatement),
                    PhpLoopAst { LoopType: PhpLoopType.While or PhpLoopType.DoWhile } loop =>
                        ContainsObjectDeclaration(loop.Body as IStatement),
                    PhpConditionalAst conditional when !conditional.IsMatchSyntax =>
                        ContainsObjectDeclarationInConditional(conditional),
                    _ => false,
                };
            }

            private static bool ContainsObjectDeclarationInConditional(PhpConditionalAst conditional)
            {
                foreach (var arm in conditional.Arms?.GetAllNotNull() ?? [])
                {
                    if (arm.Body != null && ContainsObjectDeclaration(arm.Body))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool ContainsObjectDeclaration(IStatement? statement)
            {
                switch (statement)
                {
                    case PhpObjectTypeDeclAst:
                    case TyhpExtensionDeclAst:
                        return true;
                    case PhpStatementBlockAst block:
                        return block.GetAllNotNull().Any(ContainsObjectDeclaration);
                    case PhpIfAst ifAst:
                        return ContainsObjectDeclaration(ifAst.ThenStatement)
                            || ContainsObjectDeclaration(ifAst.ElseStatement);
                    case PhpLoopAst { LoopType: PhpLoopType.While or PhpLoopType.DoWhile } loop:
                        return ContainsObjectDeclaration(loop.Body as IStatement);
                    case PhpConditionalAst conditional when !conditional.IsMatchSyntax:
                        return ContainsObjectDeclarationInConditional(conditional);
                    default:
                        return false;
                }
            }

            private sealed class NamespaceFunctionBucket
            {
                public NamespaceFunctionBucket(
                    string? namespaceName,
                    ITopStatement? namespaceStatement,
                    bool isAnonymousNamespace)
                {
                    this.NamespaceName = namespaceName;
                    this.NamespaceStatement = namespaceStatement;
                    this.IsAnonymousNamespace = isAnonymousNamespace;
                }

                public string? NamespaceName { get; }
                public ITopStatement? NamespaceStatement { get; }
                public bool IsAnonymousNamespace { get; }
                public List<ITopStatement> Statements { get; } = [];
                /// <summary>
                /// Valid <c>if (!function_exists(...)) { function … }</c> gates. Appended after
                /// <see cref="Statements"/> so they run after ungated declarations.
                /// </summary>
                public List<ITopStatement> GatedStatements { get; } = [];
            }
        }

        private static bool IsPhpVersionGateInactiveFile(SrcFileAst srcFile, EmitContext context)
        {
            if (srcFile.BoundSymbol is FileSymbol { IsPhpVersionGateInactive: true })
            {
                return true;
            }

            foreach (var child in ((IBaseScope)context.GlobalScope).GetAllChildScopes())
            {
                if (child is not FileScope fileScope)
                {
                    continue;
                }

                if (!FileScopeMatchesSource(fileScope, srcFile))
                {
                    continue;
                }

                var symbol = fileScope.DeclarationSymbol;
                if (symbol is null)
                {
                    continue;
                }

                return symbol.IsPhpVersionGateInactive;
            }

            return false;
        }

        private static bool FileScopeMatchesSource(FileScope fileScope, SrcFileAst srcFile)
        {
            var symbol = fileScope.DeclarationSymbol;
            if (symbol is null)
            {
                return false;
            }

            return string.Equals(symbol.FileName, srcFile.FileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(symbol.SourceFile, srcFile.FileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(symbol.FileName, srcFile.Identifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(symbol.SourceFile, srcFile.Identifier, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPhpDeclareKey(string? identifier) =>
            string.Equals(identifier, PhpDeclareKey, StringComparison.OrdinalIgnoreCase);

        private static bool IsExtDeclareKey(string? identifier) =>
            string.Equals(identifier, "ext", StringComparison.OrdinalIgnoreCase);

        private static bool IsTyhpOnlyDeclareKey(string? identifier) =>
            string.Equals(identifier, OutputFileDeclareKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(identifier, AutoloadDeclareKey, StringComparison.OrdinalIgnoreCase)
            || IsPhpDeclareKey(identifier)
            || IsExtDeclareKey(identifier);

        private static bool IsPhpOnlyDeclare(PhpDeclareAst declare)
        {
            var hasPhp = false;
            foreach (var directive in declare.Declarations?.GetAllNotNull() ?? [])
            {
                if (IsPhpDeclareKey(directive.Identifier) || IsExtDeclareKey(directive.Identifier))
                {
                    hasPhp = true;
                    continue;
                }

                if (!IsTyhpOnlyDeclareKey(directive.Identifier))
                {
                    return false;
                }
            }

            return hasPhp;
        }

        private static bool IsPhpVersionGateInactive(PhpDeclareAst declareAst)
            => declareAst.BoundSymbol is DeclareBlockSymbol { IsPhpVersionGateInactive: true };

        private static bool ShouldEmitPhpVersionGatedDeclaration(IBase2Ast node)
        {
            if (!HasTyhpPhpGateAttribute(node))
            {
                return true;
            }

            return node.BoundSymbol != null;
        }

        private static bool HasTyhpPhpGateAttribute(IBase2Ast node)
        {
            foreach (var attributeNode in node.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsTyhpPhpGateAttribute(attribute))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTyhpPhpGateAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol objectSymbol }
                && IsTyhpPhpFullyQualifiedName(objectSymbol.FullyQualifiedName))
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

            var trimmed = name.Trim();
            return trimmed.Equals("\\Tyhp\\Php", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\Php", StringComparison.OrdinalIgnoreCase);
        }
    }
}
