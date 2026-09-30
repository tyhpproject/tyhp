using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services.PhpDoc;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// PHP-source harvest: C# native PHP source → tyhpdef. No PHP process.
    /// </summary>
    public class NativeTyhpdefGenerator
    {
        private readonly StubCorpusCache _stubCache;

        /// <summary>IR from the last successful PHP-source harvest (golden for <c>--verify</c>).</summary>
        internal TyhpdefFile? LastLayer1 { get; private set; }

        public NativeTyhpdefGenerator()
            : this(new StubCorpusCache())
        {
        }

        internal NativeTyhpdefGenerator(StubCorpusCache stubCache)
        {
            this._stubCache = stubCache;
        }

        public void Generate(
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            string searchRoot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            cancellationToken.ThrowIfCancellationRequested();

            List<string> files;
            string primaryFileName;
            if (options.Mode == TyhpdefGenerationMode.ComposerPackage)
            {
                var collected = ComposerAutoloadPhpCollector.Collect(
                    options.PackagePath ?? "",
                    options.IncludeDev);
                if (collected.ErrorKey is not null)
                {
                    FailUsage(result, collected.ErrorKey, collected.ErrorArgs);
                    return;
                }

                files = collected.Files.ToList();
                primaryFileName = TyhpdefOutputLayout.PackageFileName(collected.PackageName);
            }
            else
            {
                files = CollectSourceFiles(options.SourcePaths, searchRoot);
                primaryFileName = TyhpdefOutputLayout.SourceFileName();
            }

            foreach (var path in files)
            {
                if (!path.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefSourceNotPhp,
                        path,
                        0,
                        0,
                        path);
                }
            }

            if (result.Diagnostics.HasErrors)
            {
                return;
            }

            if (files.Count == 0)
            {
                FailUsage(result, "CLI_TyhpdefNoSourceFiles");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var compilation = new CompilationService();
            var compileOptions = new CompilationOptions
            {
                EnableAstCache = false,
                SkipChecking = true,
                MaxThreads = 1,
            };
            var compile = compilation.ParseFiles(files, compileOptions, cancellationToken);
            var failedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (compile.ParseErrorCount > 0)
            {
                // Errors is sorted by location, so it mixes later binder diagnostics in with
                // parse errors. Only lexer/parser/visitor failures make the AST unusable.
                // TYHP3018 on an implements/trait name must not drop the rest of the file.
                foreach (var error in compile.Diagnostics.Errors)
                {
                    if (!IsSourceParseError(error.Code))
                    {
                        continue;
                    }

                    RememberFailedSourceFile(failedFiles, error.FileName);
                    result.Diagnostics.AddWarning(
                        MessageCode.TyhpdefSourceFileSkipped,
                        error.FileName,
                        error.Line,
                        error.Column,
                        error.FileName,
                        error.Message);
                }
            }

            var parsed = (compile.ParsedFiles ?? [])
                .Where(src => !IsFailedSourceFile(failedFiles, src.FileName) && !IsFailedSourceFile(failedFiles, src.Identifier))
                .ToList();
            if (parsed.Count == 0)
            {
                foreach (var error in compile.Diagnostics.Errors)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefSourceParseError,
                        error.FileName,
                        error.Line,
                        error.Column,
                        error.FileName,
                        error.Message);
                }

                return;
            }

            var extractor = new PhpAstTypeExtractor(options)
            {
                KnownShortNames = TyhpdefExternCatalog.Build(options.CatalogRoots).UniqueGlobalAndPsrShortNames(),
            };
            var file = BuildFile(parsed, extractor, options, result);
            this.LastLayer1 = file;
            this.TryHoleFill(file, options, result, cancellationToken);
            var externPlan = TyhpdefTrackBExternPass.Apply(
                file,
                options,
                result,
                searchRoot,
                extractor.ExcludedInternalTypes);
            FillGenericConstraintTypeArguments(file);
            WriteFiles(file, options, result, primaryFileName);
            TyhpdefTrackBExternPass.WriteExternsFile(externPlan, options, result);
            TyhpdefTrackBExternPass.ApplyWrapperRequires(externPlan, options);
        }

        /// <summary>
        /// Lexer, parser, and visitor diagnostics. Binder codes start at 3001.
        /// </summary>
        private static bool IsSourceParseError(MessageCode code)
            => (int)code is > 0 and < 3000;

        private static void RememberFailedSourceFile(HashSet<string> failedFiles, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            failedFiles.Add(path);
            failedFiles.Add(AstCacheService.GetRelativePath(path));
            try
            {
                failedFiles.Add(Path.GetFullPath(path));
            }
            catch (Exception)
            {
            }
        }

        private static bool IsFailedSourceFile(HashSet<string> failedFiles, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || failedFiles.Count == 0)
            {
                return false;
            }

            if (failedFiles.Contains(path))
            {
                return true;
            }

            if (failedFiles.Contains(AstCacheService.GetRelativePath(path)))
            {
                return true;
            }

            try
            {
                return failedFiles.Contains(Path.GetFullPath(path));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Discovers PHP paths for <c>--source</c> using the same
        /// <see cref="Microsoft.Extensions.FileSystemGlobbing.Matcher"/> as
        /// <c>Project.GetProjectSourceFiles()</c>.
        /// </summary>
        public static List<string> CollectSourceFiles(IEnumerable<string> patterns, string searchRoot)
        {
            var root = string.IsNullOrWhiteSpace(searchRoot)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(searchRoot);
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in patterns ?? [])
            {
                var pattern = (raw ?? "").Trim();
                if (pattern.Length == 0)
                {
                    continue;
                }

                var asFile = ResolveExistingFile(pattern, root);
                if (asFile is not null)
                {
                    files.Add(asFile);
                    continue;
                }

                var asDir = ResolveExistingDirectory(pattern, root);
                if (asDir is not null)
                {
                    try
                    {
                        foreach (var path in Directory.EnumerateFiles(asDir, "*.php", SearchOption.AllDirectories))
                        {
                            files.Add(Path.GetFullPath(path));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }

                    continue;
                }

                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                var (globRoot, globPattern) = SplitGlob(pattern, root);
                matcher.AddInclude(globPattern);
                foreach (var path in matcher.GetResultsInFullPath(globRoot))
                {
                    files.Add(path);
                }
            }

            return files.ToList();
        }

        internal static bool IsPhpPath(string path)
            => path.EndsWith(".php", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Generic types used as <c>@template T of Bound</c> bounds must carry required type
        /// arguments. PHPDoc writes <c>of ClassMetadata</c> (or already <c>of ClassMetadata&lt;object&gt;</c>);
        /// a bare generic bound is TYHP3021 / TYHP4036 once the bound itself is generic.
        /// Missing arguments are taken from the bound parameter's own constraint, else its
        /// default, else <c>mixed</c>. Already-written arguments are kept. Intra-package
        /// qualification of those names runs first in <see cref="TyhpdefTrackBExternPass"/>.
        /// </summary>
        internal static void FillGenericConstraintTypeArguments(TyhpdefFile file)
        {
            ArgumentNullException.ThrowIfNull(file);
            var index = CollectGenericArities(file);
            if (index.ByFqcn.Count == 0)
            {
                return;
            }

            FillTypes(file.GlobalTypes, index);
            FillAliases(file.TypeAliases, index);
            FillMethods(file.GlobalFunctions, index);
            foreach (var ns in file.Namespaces ?? [])
            {
                FillTypes(ns.Classes, index);
                FillAliases(ns.TypeAliases, index);
                FillMethods(ns.Functions, index);
            }
        }

        private sealed class GenericArityIndex
        {
            public Dictionary<string, IReadOnlyList<TyhpdefGenericParameter>> ByFqcn { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, string> UniqueShort { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Regex BareConstraintTypeName = new(
            @"(?<![\w\\$])(\\)?([A-Za-z_][A-Za-z0-9_]*(?:\\[A-Za-z_][A-Za-z0-9_]*)*)(?![\w\\<])",
            RegexOptions.Compiled);

        private static GenericArityIndex CollectGenericArities(TyhpdefFile file)
        {
            var index = new GenericArityIndex();
            var shorts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            AddArityTypes(index, shorts, "", file.GlobalTypes);
            AddArityAliases(index, shorts, "", file.TypeAliases);
            foreach (var ns in file.Namespaces ?? [])
            {
                AddArityTypes(index, shorts, ns.Name, ns.Classes);
                AddArityAliases(index, shorts, ns.Name, ns.TypeAliases);
            }

            foreach (var (shortName, matches) in shorts)
            {
                if (matches.Count == 1)
                {
                    index.UniqueShort[shortName] = matches[0];
                }
            }

            return index;
        }

        private static void AddArityTypes(
            GenericArityIndex index,
            Dictionary<string, List<string>> shorts,
            string ns,
            IEnumerable<TyhpdefClassDeclaration>? types)
        {
            foreach (var type in types ?? [])
            {
                RegisterArity(index, shorts, ns, type.Name, type.GenericParameters);
            }
        }

        private static void AddArityAliases(
            GenericArityIndex index,
            Dictionary<string, List<string>> shorts,
            string ns,
            IEnumerable<TyhpdefTypeAlias>? aliases)
        {
            foreach (var alias in aliases ?? [])
            {
                RegisterArity(index, shorts, ns, alias.Name, alias.GenericParameters);
            }
        }

        private static void RegisterArity(
            GenericArityIndex index,
            Dictionary<string, List<string>> shorts,
            string ns,
            string name,
            IReadOnlyList<TyhpdefGenericParameter>? parameters)
        {
            if (parameters is null || parameters.Count == 0 || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var fqcn = TyhpdefApiIndex.Qualify(ns, name);
            index.ByFqcn[fqcn] = parameters;
            var shortName = fqcn[(fqcn.LastIndexOf('\\') + 1)..];
            if (!shorts.TryGetValue(shortName, out var list))
            {
                list = [];
                shorts[shortName] = list;
            }

            list.Add(fqcn);
        }

        private static void FillTypes(IEnumerable<TyhpdefClassDeclaration>? types, GenericArityIndex index)
        {
            foreach (var type in types ?? [])
            {
                FillParams(type.GenericParameters, index);
                FillMethods(type.Methods, index);
            }
        }

        private static void FillAliases(IEnumerable<TyhpdefTypeAlias>? aliases, GenericArityIndex index)
        {
            foreach (var alias in aliases ?? [])
            {
                FillParams(alias.GenericParameters, index);
            }
        }

        private static void FillMethods(IEnumerable<TyhpdefMethod>? methods, GenericArityIndex index)
        {
            foreach (var method in methods ?? [])
            {
                FillParams(method.GenericParameters, index);
            }
        }

        private static void FillParams(List<TyhpdefGenericParameter>? parameters, GenericArityIndex index)
        {
            if (parameters is null)
            {
                return;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (string.IsNullOrWhiteSpace(parameter.Constraint))
                {
                    continue;
                }

                var filled = FillConstraint(parameter.Constraint!, index, []);
                if (!string.Equals(filled, parameter.Constraint, StringComparison.Ordinal))
                {
                    parameters[i] = parameter with { Constraint = filled };
                }
            }
        }

        private static string FillConstraint(string constraint, GenericArityIndex index, HashSet<string> visiting)
        {
            return BareConstraintTypeName.Replace(constraint, match =>
            {
                var leadingSlash = match.Groups[1].Success;
                var ident = match.Groups[2].Value;
                if (!TryGetGenericParameters(ident, leadingSlash, index, out var fqcn, out var parameters))
                {
                    return match.Value;
                }

                if (!parameters.Any(p => string.IsNullOrWhiteSpace(p.Default)))
                {
                    return match.Value;
                }

                if (!visiting.Add(fqcn))
                {
                    return match.Value;
                }

                try
                {
                    var args = parameters.Select(p =>
                    {
                        if (!string.IsNullOrWhiteSpace(p.Default))
                        {
                            return p.Default!;
                        }

                        if (!string.IsNullOrWhiteSpace(p.Constraint))
                        {
                            return FillConstraint(p.Constraint!, index, visiting);
                        }

                        return "mixed";
                    });
                    return match.Value + "<" + string.Join(", ", args) + ">";
                }
                finally
                {
                    visiting.Remove(fqcn);
                }
            });
        }

        private static bool TryGetGenericParameters(
            string ident,
            bool leadingSlash,
            GenericArityIndex index,
            out string fqcn,
            out IReadOnlyList<TyhpdefGenericParameter> parameters)
        {
            fqcn = "";
            parameters = [];
            if (PhpAstTypeExtractor.BuiltinTypes.Contains(ident))
            {
                return false;
            }

            if (leadingSlash || ident.Contains('\\', StringComparison.Ordinal))
            {
                fqcn = ident.StartsWith('\\') ? ident : "\\" + ident;
                return index.ByFqcn.TryGetValue(fqcn, out parameters!);
            }

            if (!index.UniqueShort.TryGetValue(ident, out fqcn!))
            {
                return false;
            }

            return index.ByFqcn.TryGetValue(fqcn, out parameters!);
        }

        private static TyhpdefFile BuildFile(
            IReadOnlyList<SrcFileAst> parsedFiles,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result)
        {
            var namespaces = new Dictionary<string, TyhpdefNamespace>(StringComparer.Ordinal);
            var file = new TyhpdefFile
            {
                Header = "Generated from PHP source",
            };
            var declaredFqns = CollectDeclaredFqns(parsedFiles);
            var emit = new HarvestClassEmit { Result = result };
            var shapes = AnonShapeState.Create(file, namespaces, declaredFqns);
            var requiredGates = CollectRequiredFileGates(parsedFiles);

            foreach (var src in parsedFiles)
            {
                WalkFile(src, extractor, options, file, namespaces, declaredFqns, emit, shapes, requiredGates);
            }

            foreach (var ns in namespaces.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                file.Namespaces.Add(ns);
            }

            return file;
        }

        private static HashSet<string> CollectDeclaredFqns(IReadOnlyList<SrcFileAst> parsedFiles)
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in parsedFiles)
            {
                foreach (var child in src.AstChildren)
                {
                    if (child is PhpTopStatementListAst list)
                    {
                        CollectDeclaredFqnsFromStatements(list.GetAllNotNull(), currentNs: "", declared);
                    }
                    else if (child is ITopStatement statement)
                    {
                        CollectDeclaredFqnsFromStatements([statement], currentNs: "", declared);
                    }
                }
            }

            return declared;
        }

        private static void CollectDeclaredFqnsFromStatements(
            IEnumerable<ITopStatement?> statements,
            string currentNs,
            HashSet<string> declared)
        {
            var ns = currentNs;
            foreach (var statement in statements)
            {
                if (statement is null)
                {
                    continue;
                }

                if (statement is PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst)
                {
                    var name = statement.Identifier ?? "";
                    var inner = statement is PhpNamespaceDeclAst brace
                        ? brace.TopStatements
                        : ((PhpBlockNamespaceDeclAst)statement).TopStatements;
                    if (inner is null)
                    {
                        ns = name;
                        continue;
                    }

                    CollectDeclaredFqnsFromStatements(inner.GetAllNotNull(), name, declared);
                    continue;
                }

                if (statement is not PhpObjectTypeDeclAst typeDecl
                    || typeDecl.IsAnonymousClass
                    || string.IsNullOrWhiteSpace(typeDecl.Identifier)
                    || typeDecl.Identifier.StartsWith("anonClass@", StringComparison.Ordinal))
                {
                    continue;
                }

                var fqn = new PhpTypeNameResolver(ns, NewImports()).QualifyDeclaredName(typeDecl.Identifier);
                if (fqn.Length > 0)
                {
                    declared.Add(fqn);
                }
            }
        }

        private static void WalkFile(
            SrcFileAst src,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            IReadOnlySet<string> declaredFqns,
            HarvestClassEmit emit,
            AnonShapeState shapes,
            Dictionary<string, TyhpdefHarvestGates.State> requiredGates)
        {
            var inherited = TyhpdefHarvestGates.State.None;
            if (!string.IsNullOrWhiteSpace(src.FileName))
            {
                var full = Path.GetFullPath(src.FileName);
                if (requiredGates.TryGetValue(full, out var gate))
                {
                    inherited = gate;
                }
            }

            foreach (var child in src.AstChildren)
            {
                if (child is PhpTopStatementListAst list)
                {
                    WalkStatements(
                        list.GetAllNotNull(),
                        currentNs: "",
                        imports: NewImports(),
                        extractor,
                        options,
                        file,
                        namespaces,
                        src.DocComment,
                        declaredFqns,
                        emit,
                        shapes,
                        inherited,
                        src.FileName,
                        requiredGates);
                    continue;
                }

                if (child is ITopStatement statement)
                {
                    WalkStatements(
                        [statement],
                        currentNs: "",
                        imports: NewImports(),
                        extractor,
                        options,
                        file,
                        namespaces,
                        src.DocComment,
                        declaredFqns,
                        emit,
                        shapes,
                        inherited,
                        src.FileName,
                        requiredGates);
                }
            }
        }

        private static void WalkStatements(
            IEnumerable<ITopStatement?> statements,
            string currentNs,
            Dictionary<string, string> imports,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            string? fileDocComment,
            IReadOnlySet<string> declaredFqns,
            HarvestClassEmit emit,
            AnonShapeState shapes,
            TyhpdefHarvestGates.State gate,
            string? sourceFile = null,
            Dictionary<string, TyhpdefHarvestGates.State>? requiredGates = null)
        {
            var ns = currentNs;
            var useMap = imports;
            var state = gate;
            PhpDocLocalTypeMaterializer? fileLocalTypes = null;
            var appliedFileTypes = false;
            foreach (var statement in statements)
            {
                if (statement is null)
                {
                    continue;
                }

                if (TyhpdefHarvestGates.TryGetEarlyExit(statement, sourceFile, out var exit))
                {
                    state = state.Apply(exit.Subsequent);
                    continue;
                }

                if (statement is PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst)
                {
                    var name = statement.Identifier ?? "";
                    var inner = statement is PhpNamespaceDeclAst brace
                        ? brace.TopStatements
                        : ((PhpBlockNamespaceDeclAst)statement).TopStatements;
                    if (inner is null)
                    {
                        ns = name;
                        useMap = NewImports();
                        continue;
                    }

                    WalkStatements(inner.GetAllNotNull(), name, NewImports(), extractor, options, file, namespaces, statement.DocComment ?? fileDocComment, declaredFqns, emit, shapes, state, sourceFile, requiredGates);
                    continue;
                }

                if (statement is PhpImportDeclListAst importList)
                {
                    foreach (var import in importList.GetAllNotNull())
                    {
                        RecordImport(useMap, import);
                    }

                    continue;
                }

                if (statement is PhpImportDeclAst importDecl)
                {
                    RecordImport(useMap, importDecl);
                    continue;
                }

                var resolver = new PhpTypeNameResolver(ns, useMap, extractor.KnownShortNames, declaredFqns);
                if (!appliedFileTypes)
                {
                    fileLocalTypes = ApplyFileLevelTypes(fileDocComment, ns, useMap, file, namespaces, extractor, declaredFqns, shapes);
                    appliedFileTypes = true;
                }

                if (state.Drop)
                {
                    continue;
                }

                if (TyhpdefHarvestGates.TryGetDefinedConst(statement, out var definedName, out var definedValue))
                {
                    AddDefinedConstant(definedName, definedValue, resolver, file, namespaces, state);
                    continue;
                }

                if (DeclarationExistenceGateHelper.TryGetValidExistenceGate(statement, ns, out _, out var gated)
                    && gated is PhpFunctionDeclAst gatedFunction)
                {
                    AddFunction(
                        gatedFunction,
                        resolver,
                        extractor,
                        options,
                        file,
                        namespaces,
                        fileLocalTypes,
                        shapes,
                        state.Apply(new TyhpdefHarvestGates.State(Fallback: true)));
                    continue;
                }

                if (statement is PhpObjectTypeDeclAst typeDecl)
                {
                    AddClass(typeDecl, resolver, extractor, options, file, namespaces, fileLocalTypes, emit, shapes);
                    continue;
                }

                if (statement is PhpFunctionDeclAst function)
                {
                    AddFunction(function, resolver, extractor, options, file, namespaces, fileLocalTypes, shapes, state);
                    continue;
                }

                if (statement is PhpConstDeclListAst constList)
                {
                    foreach (var constant in constList.GetAllNotNull())
                    {
                        AddConstant(constant, resolver, extractor, options, file, namespaces, isClassMember: false, state);
                    }
                }
            }
        }

        private static PhpDocLocalTypeMaterializer? ApplyFileLevelTypes(
            string? fileDocComment,
            string ns,
            Dictionary<string, string> useMap,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            PhpAstTypeExtractor extractor,
            IReadOnlySet<string> declaredFqns,
            AnonShapeState shapes)
        {
            if (string.IsNullOrWhiteSpace(fileDocComment))
            {
                return null;
            }

            var parsed = PhpDocParser.Parse(fileDocComment);
            if (parsed.TypeAliasTags.Count == 0 && parsed.ImportTypeTags.Count == 0)
            {
                return null;
            }

            var resolver = new PhpTypeNameResolver(ns, useMap, extractor.KnownShortNames, declaredFqns);
            var materializer = PhpDocLocalTypeMaterializer.Build(parsed, ownerName: null, resolver);
            AddGeneratedLocalTypes(materializer, ns, file, namespaces, shapes);
            return materializer;
        }

        private static void AddGeneratedLocalTypes(
            PhpDocLocalTypeMaterializer materializer,
            string ns,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            AnonShapeState? shapes = null)
        {
            shapes?.RememberAliases(ns, materializer.Aliases);
            shapes?.RememberNames(ns, materializer.Structs.Select(s => s.Name));
            if (ns.Length == 0)
            {
                file.TypeAliases.AddRange(materializer.Aliases);
                file.GlobalTypes.AddRange(materializer.Structs);
                return;
            }

            var bucket = GetNamespace(namespaces, ns);
            bucket.TypeAliases.AddRange(materializer.Aliases);
            bucket.Classes.AddRange(materializer.Structs);
        }

        private static string RewriteLocalTypes(
            string type,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? ownerLocalTypes)
        {
            if (fileLocalTypes is not null)
            {
                type = fileLocalTypes.Rewrite(type);
            }

            if (ownerLocalTypes is not null)
            {
                type = ownerLocalTypes.Rewrite(type);
            }

            return PhpAstTypeExtractor.ToTyhpdefType(type);
        }

        private static Dictionary<string, string> NewImports()
            => new(StringComparer.OrdinalIgnoreCase);

        private static void RecordImport(Dictionary<string, string> imports, PhpImportDeclAst import)
        {
            var useType = import.UseType?.ValueString ?? "";
            if (useType.Equals("function", StringComparison.OrdinalIgnoreCase)
                || useType.Equals("const", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var ns = (import.NamespaceName ?? "").Trim().TrimStart('\\');
            if (ns.Length == 0)
            {
                return;
            }

            var alias = string.IsNullOrWhiteSpace(import.Identifier)
                ? ns[(ns.LastIndexOf('\\') + 1)..]
                : import.Identifier.Trim();
            if (alias.Length == 0)
            {
                return;
            }

            imports[alias] = "\\" + ns;
        }

        private static void AddClass(
            PhpObjectTypeDeclAst decl,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            HarvestClassEmit emit,
            AnonShapeState shapes)
        {
            if (AnonymousClassShapeHarvest.IsAnonymousClass(decl)
                || string.IsNullOrWhiteSpace(decl.Identifier))
            {
                return;
            }

            var kind = (decl.DeclType?.ValueString ?? "class").Trim().ToLowerInvariant();
            if (kind is not ("class" or "interface" or "trait" or "enum"))
            {
                kind = "class";
            }

            var doc = PhpDocParser.Parse(decl.DocComment);
            if (!extractor.ShouldIncludeMember(doc, isPrivate: false))
            {
                if (doc.IsInternal && !options.IncludeInternal)
                {
                    var skipped = resolver.QualifyDeclaredName(decl.Identifier);
                    if (skipped.Length > 0)
                    {
                        extractor.ExcludedInternalTypes.Add(TyhpdefApiIndex.NormalizeFqn(skipped));
                    }
                }

                return;
            }

            var localTypes = PhpDocLocalTypeMaterializer.Build(doc, decl.Identifier, resolver);
            AddGeneratedLocalTypes(localTypes, resolver.Namespace, file, namespaces, shapes);
            resolver.CurrentTypeFqn = resolver.QualifyDeclaredName(decl.Identifier);
            var mapped = ExtractClass(decl, kind, resolver, extractor, options, doc, fileLocalTypes, localTypes, shapes);
            resolver.CurrentTypeFqn = null;
            var fqcn = TyhpdefApiIndex.NormalizeFqn(resolver.QualifyDeclaredName(decl.Identifier));
            if (ShouldSkipIdenticalDuplicate(emit, fqcn, mapped))
            {
                return;
            }

            var ns = resolver.Namespace;
            if (ns.Length == 0)
            {
                file.GlobalTypes.Add(mapped);
                return;
            }

            GetNamespace(namespaces, ns).Classes.Add(mapped);
        }

        /// <summary>
        /// Keep the first harvested type per FQCN when a later copy has the same
        /// members / extends / implements / flags. Divergent bodies stay so
        /// include-layer TYHP8002 still fires. Doc comments are not compared.
        /// </summary>
        private static bool ShouldSkipIdenticalDuplicate(
            HarvestClassEmit emit,
            string fqcn,
            TyhpdefClassDeclaration mapped)
        {
            if (fqcn.Length <= 1)
            {
                return false;
            }

            if (!emit.ByFqcn.TryGetValue(fqcn, out var first))
            {
                emit.ByFqcn[fqcn] = mapped;
                return false;
            }

            if (!HarvestedBodiesMatch(first, mapped))
            {
                return false;
            }

            Warn(emit.Result, "CLI_TyhpdefSkippedIdenticalDuplicate", fqcn);
            return true;
        }

        private static bool HarvestedBodiesMatch(TyhpdefClassDeclaration first, TyhpdefClassDeclaration later)
            => string.Equals(
                HarvestedBodyFingerprint(first),
                HarvestedBodyFingerprint(later),
                StringComparison.Ordinal);

        /// <summary>
        /// Stable body digest: kind, flags, inheritance, members. Ignores
        /// <see cref="TyhpdefClassDeclaration.DocComment"/> and member docs.
        /// Member order is sorted so identical OpenAPI DTOs still match.
        /// </summary>
        private static string HarvestedBodyFingerprint(TyhpdefClassDeclaration type)
        {
            var parts = new List<string>
            {
                "kind:" + (type.Kind ?? ""),
                "alias:" + (type.AsAlias ?? ""),
                "mods:" + JoinSorted(type.Modifiers),
                "attrs:" + JoinAttributes(type.Attributes),
                "extends:" + (type.Extends ?? ""),
                "implements:" + JoinSorted(type.Implements),
                "uses:" + JoinSorted(type.Uses),
                "generics:" + JoinGenerics(type.GenericParameters),
                "backing:" + (type.BackingType ?? ""),
                "deprecated:" + (type.IsDeprecated ? "1" : "0"),
                "obsolete:" + (type.IsObsolete ? "1" : "0"),
                "phpGate:" + (type.PhpGate ?? ""),
            };
            foreach (var constant in (type.Constants ?? []).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add("const:" + string.Join('\u001f',
                    constant.Name,
                    constant.Type,
                    JoinSorted(constant.Modifiers),
                    JoinAttributes(constant.Attributes),
                    constant.Value ?? "",
                    constant.UsesCoalesce ? "1" : "0",
                    constant.IsDeprecated ? "1" : "0",
                    constant.PhpGate ?? ""));
            }

            foreach (var property in (type.Properties ?? []).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add("prop:" + string.Join('\u001f',
                    property.Name,
                    property.Type,
                    JoinSorted(property.Modifiers),
                    JoinAttributes(property.Attributes),
                    property.CoalesceValue ?? "",
                    property.IsDeprecated ? "1" : "0",
                    property.PhpGate ?? "",
                    JoinHooks(property.Hooks)));
            }

            foreach (var method in SortedMethods(type.Methods))
            {
                parts.Add("method:" + MethodFingerprint(method));
            }

            foreach (var op in SortedMethods(type.Operators))
            {
                parts.Add("op:" + MethodFingerprint(op));
            }

            foreach (var alias in (type.TypeAliases ?? []).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add("aliasDecl:" + string.Join('\u001f',
                    alias.Name,
                    alias.AliasedType,
                    JoinGenerics(alias.GenericParameters),
                    JoinSorted(alias.Modifiers),
                    JoinAttributes(alias.Attributes)));
            }

            foreach (var enumCase in (type.EnumCases ?? []).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add("case:" + string.Join('\u001f',
                    enumCase.Name,
                    enumCase.BackingValue ?? "",
                    JoinAttributes(enumCase.Attributes),
                    enumCase.PhpGate ?? ""));
            }

            foreach (var member in (type.ExtensionMembers ?? []).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(ExtensionMemberFingerprint(member));
            }

            foreach (var group in type.ExtensionGroups ?? [])
            {
                parts.Add("extgroup:" + string.Join('\u001f',
                    group.TargetType,
                    JoinGenerics(group.GenericParameters)));
                foreach (var member in (group.Members ?? []).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add(ExtensionMemberFingerprint(member));
                }
            }

            return string.Join('\n', parts);
        }

        private static string ExtensionMemberFingerprint(TyhpdefExtensionMember member)
            => "ext:" + string.Join('\u001f',
                member.Kind,
                member.Name,
                member.OperatorTarget ?? "",
                JoinParameters(member.Parameters),
                member.ReturnType,
                JoinGenerics(member.GenericParameters),
                member.Body ?? "",
                member.IsDeprecated ? "1" : "0",
                member.IsObsolete ? "1" : "0",
                JoinAttributes(member.Attributes),
                member.IsExtension ? "1" : "0",
                member.ByRefReceiver ? "1" : "0");

        private static IEnumerable<TyhpdefMethod> SortedMethods(IEnumerable<TyhpdefMethod>? methods)
            => (methods ?? []).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => JoinParameters(m.Parameters), StringComparer.Ordinal)
                .ThenBy(m => m.ReturnType, StringComparer.Ordinal);

        private static string MethodFingerprint(TyhpdefMethod method)
        {
            var overloads = string.Join('|', SortedMethods(method.Overloads).Select(MethodFingerprint));
            return string.Join('\u001f',
                method.Name,
                JoinSorted(method.Modifiers),
                JoinAttributes(method.Attributes),
                JoinParameters(method.Parameters),
                method.ReturnType,
                JoinGenerics(method.GenericParameters),
                method.IsDeprecated ? "1" : "0",
                method.IsObsolete ? "1" : "0",
                method.ReturnsReference ? "1" : "0",
                method.IsAsync ? "1" : "0",
                method.PhpGate ?? "",
                method is TyhpdefFunction { IsFallback: true } ? "1" : "0",
                overloads);
        }

        private static string JoinSorted(IEnumerable<string>? values)
            => string.Join(',', (values ?? []).OrderBy(v => v, StringComparer.OrdinalIgnoreCase));

        private static string JoinAttributes(IEnumerable<TyhpdefAttribute>? attributes)
            => string.Join(';',
                (attributes ?? [])
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => string.Join(',', a.Arguments ?? []), StringComparer.Ordinal)
                    .Select(a => a.Name + "(" + string.Join(',', a.Arguments ?? []) + ")"));

        private static string JoinGenerics(IEnumerable<TyhpdefGenericParameter>? parameters)
            => string.Join(',',
                (parameters ?? []).Select(p => p.Name + ":" + (p.Constraint ?? "") + "=" + (p.Default ?? "")));

        private static string JoinParameters(IEnumerable<TyhpdefParameter>? parameters)
            => string.Join(',',
                (parameters ?? []).Select(p => string.Join('\u001f',
                    p.Name,
                    p.Type,
                    p.DefaultValue ?? "",
                    p.IsVariadic ? "1" : "0",
                    p.IsByReference ? "1" : "0",
                    p.IsPromoted ? "1" : "0",
                    JoinAttributes(p.Attributes))));

        private static string JoinHooks(IEnumerable<TyhpdefPropertyHook>? hooks)
            => string.Join(',',
                (hooks ?? []).Select(h => string.Join('\u001f',
                    h.Name,
                    h.ReturnsRef ? "1" : "0",
                    JoinSorted(h.Modifiers),
                    JoinAttributes(h.Attributes))));

        private static void Warn(TyhpdefGenerationResult result, string key, params object[] args)
        {
            var text = Message.Localize(key, args);
            result.Warnings.Add(text);
            Message.Warn(key, args);
        }

        private sealed class HarvestClassEmit
        {
            public required TyhpdefGenerationResult Result { get; init; }

            public Dictionary<string, TyhpdefClassDeclaration> ByFqcn { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class AnonShapeState
        {
            private readonly Dictionary<string, HashSet<string>> _usedNames =
                new(StringComparer.OrdinalIgnoreCase);

            public required TyhpdefFile File { get; init; }

            public required Dictionary<string, TyhpdefNamespace> Namespaces { get; init; }

            public static AnonShapeState Create(
                TyhpdefFile file,
                Dictionary<string, TyhpdefNamespace> namespaces,
                IReadOnlySet<string> declaredFqns)
            {
                var state = new AnonShapeState
                {
                    File = file,
                    Namespaces = namespaces,
                };
                foreach (var fqn in declaredFqns)
                {
                    var trimmed = (fqn ?? "").Trim().TrimStart('\\');
                    if (trimmed.Length == 0 || AnonymousClassShapeHarvest.IsAnonymousClassName(trimmed))
                    {
                        continue;
                    }

                    var slash = trimmed.LastIndexOf('\\');
                    var ns = slash < 0 ? "" : trimmed[..slash];
                    var shortName = slash < 0 ? trimmed : trimmed[(slash + 1)..];
                    state.UsedNames(ns).Add(shortName);
                }

                return state;
            }

            public HashSet<string> UsedNames(string ns)
            {
                if (!_usedNames.TryGetValue(ns, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _usedNames[ns] = set;
                }

                return set;
            }

            public void RememberNames(string ns, IEnumerable<string> names)
            {
                var set = UsedNames(ns);
                foreach (var name in names)
                {
                    var shortName = AnonymousClassShapeHarvest.ShortName(name);
                    if (shortName.Length > 0)
                    {
                        set.Add(shortName);
                    }
                }
            }

            public void RememberAliases(string ns, IEnumerable<TyhpdefTypeAlias> aliases)
                => RememberNames(ns, aliases.Select(a => a.Name));

            public void AddAlias(string ns, TyhpdefTypeAlias alias)
            {
                UsedNames(ns).Add(alias.Name);
                if (ns.Length == 0)
                {
                    File.TypeAliases.Add(alias);
                    return;
                }

                GetNamespace(Namespaces, ns).TypeAliases.Add(alias);
            }
        }

        private static string ApplyAnonymousClassReturn(
            string returnType,
            IBase2Ast? body,
            string? ownerTypeName,
            string callableName,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            AnonShapeState shapes)
        {
            var ns = resolver.Namespace;
            var updated = AnonymousClassShapeHarvest.ApplyToReturnType(
                returnType,
                body,
                ownerTypeName,
                callableName,
                shapes.UsedNames(ns),
                alias => shapes.AddAlias(ns, alias),
                anon => MapAnonymousClass(anon, resolver, extractor, options, fileLocalTypes),
                name => resolver.ResolveName(PhpAstTypeExtractor.SpellName(name)));
            return updated ?? returnType;
        }

        private static AnonymousClassShapeHarvest.HarvestedAnonymousClass MapAnonymousClass(
            PhpObjectTypeDeclAst anon,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes)
        {
            var methods = new List<TyhpdefMethod>();
            var properties = new List<TyhpdefProperty>();
            var constants = new List<TyhpdefConstant>();
            var localTypes = PhpDocLocalTypeMaterializer.Build(
                PhpDocParser.Parse(anon.DocComment),
                ownerName: null,
                resolver);

            foreach (var member in anon.Body?.GetAllNotNull() ?? [])
            {
                switch (member)
                {
                    case PhpMethodDeclAst method:
                    {
                        var extracted = ExtractMethod(
                            method,
                            isInterface: false,
                            resolver,
                            extractor,
                            options,
                            fileLocalTypes,
                            localTypes);
                        if (extracted is not null)
                        {
                            methods.Add(extracted);
                        }

                        break;
                    }
                    case PhpPropertyDeclAst propertyDecl:
                        properties.AddRange(
                            ExtractProperties(propertyDecl, resolver, extractor, options, fileLocalTypes, localTypes));
                        break;
                    case PhpConstDeclListAst constList:
                        foreach (var constant in constList.GetAllNotNull())
                        {
                            var mapped = ExtractConstant(constant, isClassMember: true, resolver, extractor, options);
                            if (mapped is not null)
                            {
                                constants.Add(mapped);
                            }
                        }

                        break;
                }
            }

            var parents = new List<string>();
            if (anon.Extends is not null)
            {
                var spelled = resolver.ResolveName(PhpAstTypeExtractor.SpellName(anon.Extends));
                if (spelled.Length > 0)
                {
                    parents.Add(spelled);
                }
            }

            foreach (var implemented in anon.Implements?.GetAllNotNull() ?? [])
            {
                var spelled = resolver.ResolveName(PhpAstTypeExtractor.SpellName(implemented));
                if (spelled.Length > 0)
                {
                    parents.Add(spelled);
                }
            }

            return new AnonymousClassShapeHarvest.HarvestedAnonymousClass(
                AnonymousClassShapeHarvest.FilterShapeMembers(methods, properties, constants),
                parents);
        }

        private static TyhpdefClassDeclaration ExtractClass(
            PhpObjectTypeDeclAst decl,
            string kind,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocBlock classDoc,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer localTypes,
            AnonShapeState shapes)
        {
            var methods = new List<TyhpdefMethod>();
            var properties = new List<TyhpdefProperty>();
            var constants = new List<TyhpdefConstant>();
            var uses = new List<string>();
            var enumCases = new List<TyhpdefEnumCase>();
            var seenMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in decl.Body?.GetAllNotNull() ?? [])
            {
                if (member is PhpTraitUseAst traitUse)
                {
                    var traitNames = new List<string>();
                    foreach (var name in traitUse.TraitNames?.GetAllNotNull() ?? [])
                    {
                        var spelled = resolver.ResolveName(PhpAstTypeExtractor.SpellName(name));
                        if (spelled.Length > 0)
                        {
                            traitNames.Add(spelled);
                        }
                    }

                    if (traitNames.Count > 0)
                    {
                        var adaptations = SpellTraitAdaptations(traitUse, resolver);
                        if (adaptations is null)
                        {
                            uses.AddRange(traitNames);
                        }
                        else
                        {
                            uses.Add(string.Join(", ", traitNames) + " " + adaptations);
                        }
                    }

                    continue;
                }

                if (member is PhpMethodDeclAst method)
                {
                    var extracted = ExtractMethod(method, kind == "interface", resolver, extractor, options, fileLocalTypes, localTypes);
                    if (extracted is not null && seenMethods.Add(extracted.Name))
                    {
                        extracted = extracted with
                        {
                            ReturnType = ApplyAnonymousClassReturn(
                                extracted.ReturnType,
                                method.Body,
                                decl.Identifier,
                                extracted.Name,
                                resolver,
                                extractor,
                                options,
                                fileLocalTypes,
                                shapes),
                        };
                        methods.Add(extracted);
                        if (string.Equals(extracted.Name, "__construct", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var promoted in PromotedProperties(method, resolver, extractor, options, fileLocalTypes, localTypes))
                            {
                                if (seenProperties.Add(promoted.Name))
                                {
                                    properties.Add(promoted);
                                }
                            }
                        }
                    }

                    continue;
                }

                if (member is PhpPropertyDeclAst propertyDecl)
                {
                    foreach (var property in ExtractProperties(propertyDecl, resolver, extractor, options, fileLocalTypes, localTypes))
                    {
                        if (seenProperties.Add(property.Name))
                        {
                            properties.Add(property);
                        }
                    }

                    continue;
                }

                if (member is PhpConstDeclListAst constList)
                {
                    foreach (var constant in constList.GetAllNotNull())
                    {
                        var mapped = ExtractConstant(constant, isClassMember: true, resolver, extractor, options);
                        if (mapped is not null)
                        {
                            constants.Add(mapped);
                        }
                    }

                    continue;
                }

                if (member is PhpEnumCaseAst enumCase)
                {
                    var caseName = enumCase.Name?.ValueString ?? enumCase.Identifier ?? "";
                    if (caseName.Length == 0)
                    {
                        continue;
                    }

                    enumCases.Add(new TyhpdefEnumCase
                    {
                        Name = caseName,
                        BackingValue = PhpAstTypeExtractor.SpellLiteral(enumCase.Value),
                        DocComment = options.IncludeDocComments ? enumCase.DocComment : null,
                    });
                }
            }

            foreach (var magic in ExtractMagicMethods(classDoc, resolver, extractor, options, fileLocalTypes, localTypes))
            {
                if (seenMethods.Add(magic.Name))
                {
                    methods.Add(magic);
                }
            }

            foreach (var magic in ExtractMagicProperties(classDoc, extractor, options, fileLocalTypes, localTypes))
            {
                if (seenProperties.Add(magic.Name))
                {
                    properties.Add(magic);
                }
            }

            var extends = decl.Extends is null
                ? null
                : resolver.ResolveName(PhpAstTypeExtractor.SpellName(decl.Extends));
            var implements = new List<string>();
            foreach (var implemented in decl.Implements?.GetAllNotNull() ?? [])
            {
                var spelled = resolver.ResolveName(PhpAstTypeExtractor.SpellName(implemented));
                if (spelled.Length > 0)
                {
                    implements.Add(spelled);
                }
            }

            var backing = decl.BackingType is not null
                ? extractor.SpellType(decl.BackingType, resolver)
                : null;

            return new TyhpdefClassDeclaration
            {
                Kind = kind,
                Name = decl.Identifier,
                Modifiers = extractor.ClassModifiers(decl.Modifiers),
                Extends = string.IsNullOrWhiteSpace(extends) ? null : extends,
                Implements = implements,
                Uses = uses,
                GenericParameters = extractor.ExtractTemplates(classDoc, resolver),
                BackingType = string.IsNullOrWhiteSpace(backing) ? null : backing,
                Constants = constants,
                Properties = properties,
                Methods = methods,
                EnumCases = enumCases,
                DocComment = extractor.CopyDocComment(decl.DocComment, classDoc, null, null, null),
                IsDeprecated = classDoc.IsDeprecated,
            };
        }

        private static TyhpdefMethod? ExtractMethod(
            PhpMethodDeclAst method,
            bool isInterface,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            var name = method.Identifier ?? "";
            if (name.Length == 0 || name == "<error>")
            {
                return null;
            }

            var modifiers = method.Modifiers?.Modifiers.ToList() ?? [];
            var doc = PhpDocParser.Parse(method.DocComment);
            if (!extractor.ShouldIncludeMember(doc, PhpAstTypeExtractor.IsPrivate(modifiers) && !isInterface))
            {
                return null;
            }

            var phpReturn = extractor.SpellType(method.ReturnType, resolver);
            var returnType = RewriteLocalTypes(
                TyhpdefRequiredTypes.IsConstructorOrDestructor(name)
                    && string.IsNullOrWhiteSpace(phpReturn)
                    && doc.ReturnTag is null
                    ? "void"
                    : TyhpdefRequiredTypes.ReturnType(
                        name,
                        extractor.MergeReturnType(phpReturn, doc, resolver)),
                fileLocalTypes,
                localTypes);

            var parameters = ExtractParameters(method.Parameters, doc, resolver, extractor, fileLocalTypes, localTypes);
            var mergedParams = parameters.ToDictionary(p => p.Name, p => p.Type, StringComparer.OrdinalIgnoreCase);
            return new TyhpdefMethod
            {
                Name = name,
                Modifiers = extractor.MethodModifiers(method.Modifiers, isInterface),
                Parameters = parameters,
                ReturnType = returnType,
                GenericParameters = extractor.ExtractTemplates(doc, resolver),
                DocComment = extractor.CopyDocComment(method.DocComment, doc, mergedParams, returnType, null),
                IsDeprecated = doc.IsDeprecated,
                ReturnsReference = method.ReturnsRef,
            };
        }

        private static List<TyhpdefParameter> ExtractParameters(
            PhpParameterListAst? list,
            PhpDocBlock doc,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            var result = new List<TyhpdefParameter>();
            foreach (var parameter in list?.GetAllNotNull() ?? [])
            {
                var name = PhpAstTypeExtractor.StripDollar(parameter.Name);
                if (name.Length == 0 || name == "<error>" || !PhpAstTypeExtractor.IsHarvestableMemberName(name))
                {
                    continue;
                }

                var phpHint = extractor.SpellType(parameter.Type, resolver);
                var type = RewriteLocalTypes(extractor.MergeParameterType(phpHint, doc, name, resolver), fileLocalTypes, localTypes);
                result.Add(new TyhpdefParameter
                {
                    Name = name,
                    Type = type,
                    DefaultValue = SpellOptionalParameterDefault(parameter.DefaultValue, resolver),
                    IsVariadic = parameter.IsVariadic,
                    IsByReference = parameter.IsRef,
                    IsPromoted = parameter.Modifiers is not null
                        && PhpAstTypeExtractor.HasVisibility(parameter.Modifiers.Modifiers),
                });
            }

            return result;
        }

        /// <summary>
        /// Parameter defaults that are not a tyhpdef literal still need <c>= null</c> so the
        /// checker treats the argument as optional. Consts and properties keep omitting
        /// <c>??</c> when <see cref="PhpAstTypeExtractor.SpellLiteral"/> returns null.
        /// </summary>
        private static string? SpellOptionalParameterDefault(
            IExpression? value,
            PhpTypeNameResolver resolver)
        {
            var spelled = PhpAstTypeExtractor.SpellLiteral(value, resolver);
            if (spelled is not null)
            {
                return spelled;
            }

            return value is null ? null : "null";
        }

        private static IEnumerable<TyhpdefProperty> PromotedProperties(
            PhpMethodDeclAst ctor,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            var ctorDoc = PhpDocParser.Parse(ctor.DocComment);
            foreach (var parameter in ctor.Parameters?.GetAllNotNull() ?? [])
            {
                var vis = parameter.Modifiers?.Modifiers.ToList() ?? [];
                if (!PhpAstTypeExtractor.HasVisibility(vis))
                {
                    continue;
                }

                var name = PhpAstTypeExtractor.StripDollar(parameter.Name);
                if (!PhpAstTypeExtractor.IsHarvestableMemberName(name))
                {
                    continue;
                }

                var phpHint = extractor.SpellType(parameter.Type, resolver);
                var type = RewriteLocalTypes(extractor.MergeParameterType(phpHint, ctorDoc, name, resolver), fileLocalTypes, localTypes);
                var fakeDoc = PhpDocBlock.Empty;
                if (!extractor.ShouldIncludeMember(fakeDoc, PhpAstTypeExtractor.IsPrivate(vis)))
                {
                    continue;
                }

                yield return new TyhpdefProperty
                {
                    Name = name,
                    Type = string.IsNullOrWhiteSpace(type) ? "mixed" : type,
                    Modifiers = extractor.PropertyModifiers(parameter.Modifiers),
                    CoalesceValue = PhpAstTypeExtractor.SpellLiteral(parameter.DefaultValue, resolver),
                };
            }
        }

        private static IEnumerable<TyhpdefProperty> ExtractProperties(
            PhpPropertyDeclAst decl,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            var modifiers = decl.Modifiers?.Modifiers.ToList() ?? [];
            var phpHint = extractor.SpellType(decl.Type, resolver);
            foreach (var property in decl.Properties?.GetAllNotNull() ?? [])
            {
                var name = PhpAstTypeExtractor.StripDollar(property.Identifier ?? property.ValueString);
                if (name.Length == 0 || name == "<error>" || !PhpAstTypeExtractor.IsHarvestableMemberName(name))
                {
                    continue;
                }

                var doc = PhpDocParser.Parse(property.DocComment);
                if (!extractor.ShouldIncludeMember(doc, PhpAstTypeExtractor.IsPrivate(modifiers)))
                {
                    continue;
                }

                var type = RewriteLocalTypes(extractor.MergeVarType(phpHint, doc, resolver), fileLocalTypes, localTypes);
                yield return new TyhpdefProperty
                {
                    Name = name,
                    Type = string.IsNullOrWhiteSpace(type) ? "mixed" : type,
                    Modifiers = extractor.PropertyModifiers(decl.Modifiers),
                    CoalesceValue = PhpAstTypeExtractor.SpellLiteral(property.DefaultValue, resolver),
                    DocComment = extractor.CopyDocComment(property.DocComment, doc, null, null, type),
                    IsDeprecated = doc.IsDeprecated,
                    Hooks = ExtractHooks(property.Hooks),
                };
            }
        }

        /// <summary>
        /// Copies bodyless hook shape from PHP <c>#classPropertyAccessors</c> /
        /// <see cref="PhpPropertyAst.Hooks"/>. Hook bodies are never copied.
        /// </summary>
        private static List<TyhpdefPropertyHook> ExtractHooks(PhpPropertyHookListAst? hooks)
        {
            var result = new List<TyhpdefPropertyHook>();
            foreach (var hook in hooks?.GetAllNotNull() ?? [])
            {
                var name = hook.Identifier ?? "";
                if (name.Length == 0 || name == "<error>")
                {
                    continue;
                }

                result.Add(new TyhpdefPropertyHook
                {
                    Name = name,
                    ReturnsRef = hook.ReturnsRef,
                    Modifiers = ExtractHookModifiers(hook.Modifiers),
                });
            }

            return result;
        }

        /// <summary>
        /// Hook visibility / <c>final</c> only. Never emits an explicit <c>public</c>
        /// hook modifier (matching Reflection harvest's <c>hook_modifiers()</c>), even when the PHP
        /// source wrote <c>public get;</c> explicitly, so <c>{ get; }</c> stays compact.
        /// </summary>
        private static List<string> ExtractHookModifiers(PhpModifierListAst? list)
        {
            var modifiers = list?.Modifiers.ToList() ?? [];
            var result = new List<string>();
            if (modifiers.Contains(PhpModifier.Final))
            {
                result.Add("final");
            }

            if (modifiers.Contains(PhpModifier.Private))
            {
                result.Add("private");
            }
            else if (modifiers.Contains(PhpModifier.Protected))
            {
                result.Add("protected");
            }

            return result;
        }

        private static void AddFunction(
            PhpFunctionDeclAst function,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            AnonShapeState shapes,
            TyhpdefHarvestGates.State gate)
        {
            var name = function.Identifier ?? "";
            if (name.Length == 0)
            {
                return;
            }

            var doc = PhpDocParser.Parse(function.DocComment);
            if (!extractor.ShouldIncludeMember(doc, isPrivate: false))
            {
                return;
            }

            var localTypes = PhpDocLocalTypeMaterializer.Build(doc, name, resolver);
            AddGeneratedLocalTypes(localTypes, resolver.Namespace, file, namespaces, shapes);
            var phpReturn = extractor.SpellType(function.ReturnType, resolver);
            var returnType = ApplyAnonymousClassReturn(
                RewriteLocalTypes(
                    TyhpdefRequiredTypes.ReturnType(
                        name,
                        extractor.MergeReturnType(phpReturn, doc, resolver)),
                    fileLocalTypes,
                    localTypes),
                function.Body,
                ownerTypeName: null,
                name,
                resolver,
                extractor,
                options,
                fileLocalTypes,
                shapes);
            var parameters = ExtractParameters(function.Parameters, doc, resolver, extractor, fileLocalTypes, localTypes);
            var mergedParams = parameters.ToDictionary(p => p.Name, p => p.Type, StringComparer.OrdinalIgnoreCase);
            var mapped = new TyhpdefFunction
            {
                Name = name,
                Parameters = parameters,
                ReturnType = returnType,
                GenericParameters = extractor.ExtractTemplates(doc, resolver),
                DocComment = extractor.CopyDocComment(function.DocComment, doc, mergedParams, returnType, null),
                IsDeprecated = doc.IsDeprecated,
                ReturnsReference = function.ReturnsRef,
                IsFallback = gate.Fallback,
            };

            CommitFunction(file, namespaces, resolver.Namespace, mapped, gate);
        }

        private static void CommitFunction(
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            string ns,
            TyhpdefFunction mapped,
            TyhpdefHarvestGates.State gate)
        {
            if (!gate.HasDeclare)
            {
                if (ns.Length == 0)
                {
                    file.GlobalFunctions.Add(mapped);
                    return;
                }

                GetNamespace(namespaces, ns).Functions.Add(mapped);
                return;
            }

            if (ns.Length == 0)
            {
                EnsureDeclareBlock(file, gate).Functions.Add(mapped);
                return;
            }

            DeclareNamespace(file, gate, ns).Functions.Add(mapped);
        }

        private static void CommitConstant(
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            string ns,
            TyhpdefConstant mapped,
            TyhpdefHarvestGates.State gate,
            bool isClassMember)
        {
            if (isClassMember || !gate.HasDeclare)
            {
                if (ns.Length == 0)
                {
                    file.GlobalConstants.Add(mapped);
                    return;
                }

                GetNamespace(namespaces, ns).Constants.Add(mapped);
                return;
            }

            if (ns.Length == 0)
            {
                EnsureDeclareBlock(file, gate).Constants.Add(mapped);
                return;
            }

            DeclareNamespace(file, gate, ns).Constants.Add(mapped);
        }

        private static void AddDefinedConstant(
            string name,
            IExpression? value,
            PhpTypeNameResolver resolver,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            TyhpdefHarvestGates.State gate)
        {
            var spelled = PhpAstTypeExtractor.SpellLiteral(value, resolver);
            var type = PhpAstTypeExtractor.InferConstType(value);
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "mixed";
            }

            CommitConstant(
                file,
                namespaces,
                resolver.Namespace,
                new TyhpdefConstant
                {
                    Name = name,
                    Type = type,
                    Value = spelled,
                    UsesCoalesce = !string.IsNullOrWhiteSpace(spelled),
                    IsFallback = true,
                },
                gate.Apply(new TyhpdefHarvestGates.State(Fallback: true)),
                isClassMember: false);
        }

        private static TyhpdefDeclareBlock EnsureDeclareBlock(TyhpdefFile file, TyhpdefHarvestGates.State gate)
        {
            var list = file.DeclareBlocks;
            TyhpdefDeclareBlock? block = null;
            if (!string.IsNullOrWhiteSpace(gate.PhpConstraint))
            {
                block = FindDeclare(list, gate.PhpConstraint!);
                list = block.Children;
            }

            if (!string.IsNullOrWhiteSpace(gate.ExtConstraint))
            {
                block = FindDeclare(list, "ext=\"" + gate.ExtConstraint + "\"");
            }

            return block ?? FindDeclare(file.DeclareBlocks, gate.PhpConstraint ?? "");
        }

        private static TyhpdefNamespace DeclareNamespace(TyhpdefFile file, TyhpdefHarvestGates.State gate, string ns)
        {
            var block = EnsureDeclareBlock(file, gate);
            var found = block.Namespaces.FirstOrDefault(item => string.Equals(item.Name, ns, StringComparison.Ordinal));
            if (found != null)
            {
                return found;
            }

            found = new TyhpdefNamespace { Name = ns };
            block.Namespaces.Add(found);
            return found;
        }

        private static TyhpdefDeclareBlock FindDeclare(List<TyhpdefDeclareBlock> list, string constraint)
        {
            var found = list.FirstOrDefault(item => string.Equals(item.Constraint, constraint, StringComparison.Ordinal));
            if (found != null)
            {
                return found;
            }

            found = new TyhpdefDeclareBlock { Constraint = constraint };
            list.Add(found);
            return found;
        }

        private static Dictionary<string, TyhpdefHarvestGates.State> CollectRequiredFileGates(IReadOnlyList<SrcFileAst> parsedFiles)
        {
            var map = new Dictionary<string, TyhpdefHarvestGates.State>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in parsedFiles)
            {
                foreach (var child in src.AstChildren)
                {
                    if (child is PhpTopStatementListAst list)
                    {
                        CollectRequiredFileGatesFrom(list.GetAllNotNull(), src.FileName, map);
                    }
                    else if (child is ITopStatement statement)
                    {
                        CollectRequiredFileGatesFrom([statement], src.FileName, map);
                    }
                }
            }

            return map;
        }

        private static void CollectRequiredFileGatesFrom(
            IEnumerable<ITopStatement?> statements,
            string? sourceFile,
            Dictionary<string, TyhpdefHarvestGates.State> map)
        {
            foreach (var statement in statements)
            {
                if (statement is null)
                {
                    continue;
                }

                if (statement is PhpNamespaceDeclAst or PhpBlockNamespaceDeclAst)
                {
                    var inner = statement is PhpNamespaceDeclAst brace
                        ? brace.TopStatements
                        : ((PhpBlockNamespaceDeclAst)statement).TopStatements;
                    if (inner != null)
                    {
                        CollectRequiredFileGatesFrom(inner.GetAllNotNull(), sourceFile, map);
                    }

                    continue;
                }

                if (!TyhpdefHarvestGates.TryGetEarlyExit(statement, sourceFile, out var exit)
                    || exit.RequiredPath == null
                    || exit.RequiredState == null
                    || exit.RequiredState.Drop)
                {
                    continue;
                }

                if (!map.TryGetValue(exit.RequiredPath, out var existing))
                {
                    map[exit.RequiredPath] = exit.RequiredState;
                    continue;
                }

                if (existing.PhpConstraint != exit.RequiredState.PhpConstraint
                    || existing.ExtConstraint != exit.RequiredState.ExtConstraint)
                {
                    map[exit.RequiredPath] = TyhpdefHarvestGates.State.None;
                }
            }
        }

        private static void AddConstant(
            PhpConstDeclAst constant,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            bool isClassMember,
            TyhpdefHarvestGates.State gate)
        {
            var mapped = ExtractConstant(constant, isClassMember, resolver, extractor, options);
            if (mapped is null)
            {
                return;
            }

            if (gate.Fallback)
            {
                mapped = mapped with { IsFallback = true };
            }

            CommitConstant(file, namespaces, resolver.Namespace, mapped, gate, isClassMember);
        }

        private static TyhpdefConstant? ExtractConstant(
            PhpConstDeclAst constant,
            bool isClassMember,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options)
        {
            var name = constant.Identifier ?? "";
            if (name.Length == 0)
            {
                return null;
            }

            var vis = constant.Modifiers?.Modifiers.ToList() ?? [];
            var doc = PhpDocParser.Parse(constant.DocComment);
            if (!extractor.ShouldIncludeMember(doc, isClassMember && PhpAstTypeExtractor.IsPrivate(vis)))
            {
                return null;
            }

            var value = PhpAstTypeExtractor.SpellLiteral(constant.Value, resolver);
            var inferred = PhpAstTypeExtractor.InferConstType(constant.Value);
            var phpHint = constant.Type is not null
                ? extractor.SpellType(constant.Type, resolver)
                : "";
            var type = extractor.MergeVarType(
                string.IsNullOrWhiteSpace(phpHint) ? inferred : phpHint,
                doc,
                resolver);

            var modifiers = new List<string>();
            if (isClassMember)
            {
                modifiers = extractor.MethodModifiers(constant.Modifiers, isInterface: false);
                modifiers.RemoveAll(m => m is "static" or "abstract" or "final");
            }

            return new TyhpdefConstant
            {
                Name = name,
                Type = string.IsNullOrWhiteSpace(type) ? inferred : type,
                Value = value,
                UsesCoalesce = value is not null,
                Modifiers = modifiers,
                DocComment = extractor.CopyDocComment(constant.DocComment, doc, null, null, type),
                IsDeprecated = doc.IsDeprecated,
            };
        }

        private static IEnumerable<TyhpdefMethod> ExtractMagicMethods(
            PhpDocBlock classDoc,
            PhpTypeNameResolver resolver,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            foreach (var tag in classDoc.MethodTags)
            {
                var name = tag.ParameterName ?? "";
                if (name.Length == 0 || !PhpAstTypeExtractor.IsHarvestableMemberName(name))
                {
                    continue;
                }

                var returnType = string.IsNullOrWhiteSpace(tag.TypeExpression)
                    ? "mixed"
                    : RewriteLocalTypes(
                        resolver.ResolveImportedNamesOnly(PhpDocTypeParser.Normalize(tag.TypeExpression)),
                        fileLocalTypes,
                        localTypes);
                var modifiers = new List<string> { "public" };
                if (StartsWithWord(tag.RawContent, "static"))
                {
                    modifiers.Add("static");
                }

                yield return new TyhpdefMethod
                {
                    Name = name,
                    Modifiers = modifiers,
                    Parameters = ParseMagicParameters(tag.RawContent, resolver, fileLocalTypes, localTypes),
                    ReturnType = returnType,
                    IsDeprecated = false,
                };
            }
        }

        private static IEnumerable<TyhpdefProperty> ExtractMagicProperties(
            PhpDocBlock classDoc,
            PhpAstTypeExtractor extractor,
            TyhpdefGenerationOptions options,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            foreach (var tag in classDoc.PropertyTags)
            {
                var name = PhpAstTypeExtractor.StripDollar(tag.ParameterName);
                if (name.Length == 0 || !PhpAstTypeExtractor.IsHarvestableMemberName(name))
                {
                    continue;
                }

                var type = string.IsNullOrWhiteSpace(tag.TypeExpression)
                    ? "mixed"
                    : PhpAstTypeExtractor.RewriteStaticTypeInValuePosition(
                        RewriteLocalTypes(
                            PhpAstTypeExtractor.ToTyhpdefType(PhpDocTypeParser.Normalize(tag.TypeExpression)),
                            fileLocalTypes,
                            localTypes));
                yield return new TyhpdefProperty
                {
                    Name = name,
                    Type = type,
                    Modifiers = ["public"],
                };
            }
        }

        private static List<TyhpdefParameter> ParseMagicParameters(
            string rawContent,
            PhpTypeNameResolver resolver,
            PhpDocLocalTypeMaterializer? fileLocalTypes,
            PhpDocLocalTypeMaterializer? localTypes)
        {
            var result = new List<TyhpdefParameter>();
            var open = rawContent.LastIndexOf('(');
            var close = rawContent.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return result;
            }

            var inner = rawContent[(open + 1)..close].Trim();
            if (inner.Length == 0)
            {
                return result;
            }

            foreach (var piece in SplitArgs(inner))
            {
                var part = piece.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                var dollar = part.LastIndexOf('$');
                string name;
                string phpHint;
                if (dollar >= 0)
                {
                    name = PhpAstTypeExtractor.StripDollar(part[dollar..].Split('=', 2)[0]);
                    phpHint = part[..dollar].Trim();
                }
                else
                {
                    continue;
                }

                if (!PhpAstTypeExtractor.IsHarvestableMemberName(name))
                {
                    continue;
                }

                var type = phpHint.Length == 0
                    ? "mixed"
                    : PhpAstTypeExtractor.RewriteStaticTypeInValuePosition(
                        RewriteLocalTypes(
                            resolver.ResolveImportedNamesOnly(PhpDocTypeParser.Normalize(phpHint.TrimEnd())),
                            fileLocalTypes,
                            localTypes));
                result.Add(new TyhpdefParameter { Name = name, Type = type });
            }

            return result;
        }

        private static List<string> SplitArgs(string inner)
        {
            var parts = new List<string>();
            var depthAngle = 0;
            var depthParen = 0;
            var start = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                switch (c)
                {
                    case '<':
                        depthAngle++;
                        break;
                    case '>':
                        if (depthAngle > 0)
                        {
                            depthAngle--;
                        }

                        break;
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        if (depthParen > 0)
                        {
                            depthParen--;
                        }

                        break;
                    case ',' when depthAngle == 0 && depthParen == 0:
                        parts.Add(inner[start..i]);
                        start = i + 1;
                        break;
                }
            }

            parts.Add(inner[start..]);
            return parts;
        }

        private static bool StartsWithWord(string text, string word)
        {
            var trimmed = (text ?? "").TrimStart();
            return trimmed.StartsWith(word, StringComparison.OrdinalIgnoreCase)
                && (trimmed.Length == word.Length || !char.IsLetterOrDigit(trimmed[word.Length]));
        }

        private static TyhpdefNamespace GetNamespace(
            Dictionary<string, TyhpdefNamespace> namespaces,
            string name)
        {
            if (!namespaces.TryGetValue(name, out var ns))
            {
                ns = new TyhpdefNamespace { Name = name };
                namespaces[name] = ns;
            }

            return ns;
        }

        private void TryHoleFill(
            TyhpdefFile file,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken)
        {
            string? cacheDir;
            if (options.RequireStubs)
            {
                cacheDir = this._stubCache.Resolve(
                    options with { FetchStubCache = false },
                    result.Diagnostics,
                    cancellationToken);
            }
            else
            {
                var dir = StubCorpusCache.ResolveDirectory(options);
                cacheDir = StubCorpusCache.IsPopulated(dir) ? dir : null;
            }

            if (cacheDir is null)
            {
                return;
            }

            var corpusDirs = StubCorpusCache.CorpusDirectories(cacheDir);
            if (corpusDirs.Count == 0)
            {
                return;
            }

            var index = IndexStubCorpora(corpusDirs);
            HoleFillFunctions(file.GlobalFunctions, "", index, result.Diagnostics, options.IncludeDocComments);
            foreach (var type in file.GlobalTypes ?? [])
            {
                HoleFillType(type, "", index, result.Diagnostics, options.IncludeDocComments);
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                HoleFillFunctions(ns.Functions, ns.Name, index, result.Diagnostics, options.IncludeDocComments);
                foreach (var type in ns.Classes ?? [])
                {
                    HoleFillType(type, ns.Name, index, result.Diagnostics, options.IncludeDocComments);
                }
            }
        }

        private static Dictionary<string, Dictionary<string, StubSymbol>> IndexStubCorpora(
            IReadOnlyDictionary<string, string> corpusDirs)
        {
            var index = new Dictionary<string, Dictionary<string, StubSymbol>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (corpusId, dir) in corpusDirs)
            {
                var byFqn = new Dictionary<string, StubSymbol>(StringComparer.OrdinalIgnoreCase);
                foreach (var stubFile in StubCorpusCache.EnumerateStubFiles(dir))
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(stubFile);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    foreach (var symbol in StubPhpSymbolExtractor.ExtractFile(stubFile, text))
                    {
                        byFqn[symbol.Fqn] = symbol;
                        foreach (var member in symbol.Members)
                        {
                            byFqn[member.Fqn] = member;
                        }
                    }
                }

                index[corpusId] = byFqn;
            }

            return index;
        }

        private static void HoleFillFunctions(
            List<TyhpdefFunction>? functions,
            string ns,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            DiagnosticBag diagnostics,
            bool includeDocs)
        {
            if (functions is null)
            {
                return;
            }

            for (var i = 0; i < functions.Count; i++)
            {
                var function = functions[i];
                var fqn = Qualify(ns, function.Name);
                if (TryHoleFillCallable(function, fqn, index, diagnostics, includeDocs, out var enriched)
                    && enriched is TyhpdefFunction asFunction)
                {
                    functions[i] = asFunction;
                }
            }
        }

        private static void HoleFillType(
            TyhpdefClassDeclaration type,
            string ns,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            DiagnosticBag diagnostics,
            bool includeDocs)
        {
            var typeFqn = Qualify(ns, type.Name);
            if (string.IsNullOrWhiteSpace(type.DocComment))
            {
                var matches = MatchesFor(typeFqn, index);
                var stubDoc = FirstStubDoc(matches);
                if (stubDoc is not null)
                {
                    type.DocComment = stubDoc;
                }
            }

            for (var i = 0; i < type.Methods.Count; i++)
            {
                var method = type.Methods[i];
                var fqn = typeFqn + "::" + method.Name;
                if (TryHoleFillCallable(method, fqn, index, diagnostics, includeDocs, out var enriched))
                {
                    type.Methods[i] = enriched;
                }
            }

            for (var i = 0; i < type.Properties.Count; i++)
            {
                var property = type.Properties[i];
                if (!StubHarvestTyhpdefEnricher.IsWeakType(property.Type)
                    && !string.IsNullOrWhiteSpace(property.DocComment))
                {
                    continue;
                }

                var fqn = typeFqn + "::$" + PhpAstTypeExtractor.StripDollar(property.Name);
                var matches = MatchesFor(fqn, index);
                if (matches.Count == 0)
                {
                    continue;
                }

                var byCorpus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (corpus, symbol) in matches)
                {
                    byCorpus[corpus] = FirstPresent(symbol.PhpDocType, symbol.NativeReturnType);
                }

                var consensus = StubHarvestTyhpdefEnricher.ConsensusType(byCorpus, fqn, diagnostics);
                var doc = property.DocComment;
                if (includeDocs && string.IsNullOrWhiteSpace(doc))
                {
                    doc = FirstStubDoc(matches);
                }

                if (consensus is not null && StubHarvestTyhpdefEnricher.IsWeakType(property.Type))
                {
                    type.Properties[i] = property with { Type = consensus, DocComment = doc };
                }
                else if (doc is not null && doc != property.DocComment)
                {
                    type.Properties[i] = property with { DocComment = doc };
                }
            }
        }

        private static bool TryHoleFillCallable(
            TyhpdefMethod method,
            string fqn,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            DiagnosticBag diagnostics,
            bool includeDocs,
            out TyhpdefMethod enriched)
        {
            enriched = method;
            var matches = MatchesFor(fqn, index);
            if (matches.Count == 0)
            {
                return false;
            }

            var changed = false;
            var returnType = method.ReturnType;
            if (StubHarvestTyhpdefEnricher.IsWeakType(returnType))
            {
                var byCorpus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (corpus, symbol) in matches)
                {
                    byCorpus[corpus] = FirstPresent(symbol.PhpDocType, symbol.NativeReturnType);
                }

                var consensus = StubHarvestTyhpdefEnricher.ConsensusType(byCorpus, fqn, diagnostics);
                if (consensus is not null)
                {
                    returnType = consensus;
                    changed = true;
                }
            }

            var parameters = method.Parameters.Select(p => p with { }).ToList();
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (!StubHarvestTyhpdefEnricher.IsWeakType(parameter.Type))
                {
                    continue;
                }

                var byCorpus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (corpus, symbol) in matches)
                {
                    var stubParam = symbol.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    var phpDoc = symbol.PhpDoc.ParamTags.FirstOrDefault(t =>
                        string.Equals(t.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    byCorpus[corpus] = FirstPresent(phpDoc?.TypeExpression, stubParam?.NativeType);
                }

                var consensus = StubHarvestTyhpdefEnricher.ConsensusType(
                    byCorpus,
                    fqn + "($" + parameter.Name + ")",
                    diagnostics);
                if (consensus is not null)
                {
                    parameters[i] = parameter with { Type = consensus };
                    changed = true;
                }
            }

            var doc = method.DocComment;
            if (includeDocs && string.IsNullOrWhiteSpace(doc))
            {
                doc = FirstStubDoc(matches);
                changed |= doc is not null;
            }

            if (!changed)
            {
                return false;
            }

            if (method is TyhpdefFunction function)
            {
                enriched = function with
                {
                    ReturnType = returnType,
                    Parameters = parameters,
                    DocComment = doc,
                };
            }
            else
            {
                enriched = method with
                {
                    ReturnType = returnType,
                    Parameters = parameters,
                    DocComment = doc,
                };
            }

            return true;
        }

        private static List<(string Corpus, StubSymbol Symbol)> MatchesFor(
            string fqn,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index)
        {
            var matches = new List<(string, StubSymbol)>();
            var key = fqn.Trim().TrimStart('\\');
            foreach (var (corpus, byFqn) in index)
            {
                if (byFqn.TryGetValue(fqn, out var symbol)
                    || byFqn.TryGetValue("\\" + key, out symbol)
                    || byFqn.TryGetValue(key, out symbol))
                {
                    matches.Add((corpus, symbol));
                }
            }

            return matches;
        }

        private static string Qualify(string ns, string name)
        {
            var trimmed = (name ?? "").Trim().TrimStart('\\');
            if (string.IsNullOrWhiteSpace(ns))
            {
                return "\\" + trimmed;
            }

            return "\\" + ns.Trim().Trim('\\') + "\\" + trimmed;
        }

        private static string? FirstPresent(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string? FirstStubDoc(List<(string Corpus, StubSymbol Symbol)> matches)
        {
            foreach (var preferred in new[] { StubCorpusCache.PsalmId, StubCorpusCache.PhpStanId, StubCorpusCache.PhpStormId, StubCorpusCache.PhanId })
            {
                foreach (var (corpus, symbol) in matches)
                {
                    if (corpus == preferred && !string.IsNullOrWhiteSpace(symbol.DocComment))
                    {
                        return symbol.DocComment;
                    }
                }
            }

            return matches.Select(m => m.Symbol.DocComment).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
        }

        private static void WriteFiles(
            TyhpdefFile file,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            string primaryFileName)
        {
            var outputs = TyhpdefOutputLayout.SplitWithPrimaryFile(file, options, primaryFileName);
            var writerOptions = new TyhpdefOutputOptions { IncludeDocComments = options.IncludeDocComments };
            foreach (var (path, part) in outputs)
            {
                if (File.Exists(path) && !options.Overwrite)
                {
                    Message.Warn("CLI_TyhpdefSkippedExistingFile", path);
                    continue;
                }

                string text;
                try
                {
                    text = TyhpdefOutputWriter.Write(part, writerOptions);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        path,
                        0,
                        0,
                        path,
                        ex.Message);
                    continue;
                }

                var parseDiagnostics = new DiagnosticBag();
                try
                {
                    var ast = Tyhpdef.ParseContent(text, path, ParseMode.Tyhpdef, parseDiagnostics);
                    if (ast is null || parseDiagnostics.HasErrors)
                    {
                        var detail = parseDiagnostics.Errors.Count > 0
                            ? parseDiagnostics.Errors[0].Message
                            : "parse returned null";
                        FailParse(result, path, detail);
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    FailParse(result, path, ex.Message);
                    continue;
                }

                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(path, text);
                    result.GeneratedFiles.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        path,
                        0,
                        0,
                        path,
                        ex.Message);
                }
            }

            result.ClassCount = CountTypes(file);
            result.FunctionCount = CountFunctions(file);
            result.ConstantCount = CountConstants(file);
            result.TotalDeclarations = result.ClassCount + result.FunctionCount + result.ConstantCount;
        }

        private static int CountTypes(TyhpdefFile file)
            => (file.GlobalTypes?.Count ?? 0) + (file.Namespaces ?? []).Sum(n => n.Classes?.Count ?? 0);

        private static int CountFunctions(TyhpdefFile file)
            => (file.GlobalFunctions?.Count ?? 0) + (file.Namespaces ?? []).Sum(n => n.Functions?.Count ?? 0);

        private static int CountConstants(TyhpdefFile file)
            => (file.GlobalConstants?.Count ?? 0) + (file.Namespaces ?? []).Sum(n => n.Constants?.Count ?? 0);

        private static void FailUsage(TyhpdefGenerationResult result, string messageKey, params object[] args)
        {
            Message.Error(messageKey, args);
            result.Diagnostics.AddError(
                MessageCode.TyhpdefGenerationError,
                "generate_tyhpdef",
                0,
                0,
                Message.Localize(messageKey, args));
        }

        /// <summary>
        /// Reports a failed round-trip parse of freshly generated tyhpdef text. Must call
        /// <see cref="Message.Error"/> directly (like <see cref="FailUsage"/>). The CLI
        /// formatter skips <see cref="MessageCode.TyhpdefGenerationError"/> (usage errors
        /// already printed), so parse-check uses <see cref="MessageCode.TyhpdefParseError"/>
        /// — otherwise the run exits non-zero with no TYHP####.
        /// </summary>
        private static void FailParse(TyhpdefGenerationResult result, string path, string detail)
        {
            Message.Error("CLI_TyhpdefParseFailed", path, detail);
            result.Diagnostics.AddError(
                MessageCode.TyhpdefParseError,
                path,
                0,
                0,
                Message.Localize("CLI_TyhpdefParseFailed", path, detail));
        }

        private static string? ResolveExistingFile(string pattern, string root)
        {
            try
            {
                if (File.Exists(pattern))
                {
                    return Path.GetFullPath(pattern);
                }

                var combined = Path.Combine(root, pattern);
                if (File.Exists(combined))
                {
                    return Path.GetFullPath(combined);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        private static string? ResolveExistingDirectory(string pattern, string root)
        {
            try
            {
                if (Directory.Exists(pattern))
                {
                    return Path.GetFullPath(pattern);
                }

                var combined = Path.Combine(root, pattern);
                if (Directory.Exists(combined))
                {
                    return Path.GetFullPath(combined);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        private static (string Root, string Pattern) SplitGlob(string pattern, string fallbackRoot)
        {
            var normalized = pattern.Replace('\\', '/');
            var globAt = normalized.IndexOfAny(['*', '?', '[']);
            if (globAt < 0)
            {
                return (fallbackRoot, NormalizeGlobPattern(pattern));
            }

            var slash = normalized.LastIndexOf('/', globAt);
            if (slash <= 0)
            {
                return (fallbackRoot, NormalizeGlobPattern(pattern));
            }

            var globRoot = normalized[..slash];
            var globPattern = normalized[(slash + 1)..];
            if (Path.IsPathRooted(pattern) || Path.IsPathRooted(globRoot))
            {
                return (Path.GetFullPath(globRoot.Replace('/', Path.DirectorySeparatorChar)), globPattern);
            }

            return (fallbackRoot, NormalizeGlobPattern(pattern));
        }

        private static string NormalizeGlobPattern(string pattern)
        {
            var normalized = pattern.Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            return normalized;
        }

        private static string? SpellTraitAdaptations(PhpTraitUseAst traitUse, PhpTypeNameResolver resolver)
        {
            var parts = new List<string>();
            foreach (var item in traitUse.Adaptations?.GetAllNotNull() ?? [])
            {
                switch (item)
                {
                    case PhpTraitAliasAst alias when alias.IsHide:
                    {
                        var member = SpellTraitMemberRef(alias.MethodReference, resolver);
                        if (member.Length > 0)
                        {
                            parts.Add(member + " hide");
                        }

                        break;
                    }
                    case PhpTraitAliasAst alias:
                    {
                        var member = SpellTraitMemberRef(alias.MethodReference, resolver);
                        if (member.Length == 0)
                        {
                            break;
                        }

                        var text = member + " as";
                        if (alias.NewModifier is { } modifier && modifier != PhpModifier.None)
                        {
                            text += " " + modifier.ToString().ToLowerInvariant();
                        }

                        if (!string.IsNullOrEmpty(alias.Identifier))
                        {
                            text += " " + alias.Identifier;
                        }

                        parts.Add(text);
                        break;
                    }
                    case PhpTraitPrecedenceAst precedence:
                    {
                        var member = SpellTraitMemberRef(precedence.MethodReference, resolver);
                        var instead = new List<string>();
                        foreach (var trait in precedence.InsteadOfTraits?.GetAllNotNull() ?? [])
                        {
                            var spelled = resolver.ResolveName(PhpAstTypeExtractor.SpellName(trait));
                            if (spelled.Length > 0)
                            {
                                instead.Add(spelled);
                            }
                        }

                        if (member.Length > 0 && instead.Count > 0)
                        {
                            parts.Add(member + " insteadof " + string.Join(", ", instead));
                        }

                        break;
                    }
                }
            }

            if (parts.Count == 0)
            {
                return null;
            }

            return "{ " + string.Join("; ", parts) + "; }";
        }

        private static string SpellTraitMemberRef(PhpTraitMemberRefAst? member, PhpTypeNameResolver resolver)
        {
            if (member is null)
            {
                return "";
            }

            var method = member.MemberName?.Identifier;
            if (string.IsNullOrEmpty(method))
            {
                method = member.MemberName?.ValueString;
            }

            if (string.IsNullOrEmpty(method))
            {
                method = member.Identifier;
            }

            method = (method ?? "").Trim();
            if (method.Length == 0)
            {
                return "";
            }

            if (member.TraitName is not null)
            {
                var trait = resolver.ResolveName(PhpAstTypeExtractor.SpellName(member.TraitName));
                if (trait.Length > 0)
                {
                    return trait + "::" + method;
                }
            }

            return method;
        }
    }
}
