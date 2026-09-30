using System.Text.RegularExpressions;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// PHP-source harvest post-pass: qualify intra-package names, classify foreign signature
    /// types against <see cref="TyhpdefExternCatalog"/>, omit types that extend
    /// or implement an <c>extern</c>, and emit <c>_tyhpdef/externs.tyhpdef</c>.
    /// Library <c>package.tyhpdef</c> generation never calls this.
    /// </summary>
    public static class TyhpdefTrackBExternPass
    {
        private static readonly HashSet<string> BuiltinNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "int", "integer", "string", "bool", "boolean", "float", "double",
            "void", "mixed", "null", "true", "false", "never", "resource",
            "array", "iterable", "object", "callable", "self", "static", "parent",
            "this",
        };

        private static readonly Regex TypeName = new(
            @"(?<![\w\\$])(\\)?([A-Za-z_][A-Za-z0-9_]*(?:\\[A-Za-z_][A-Za-z0-9_]*)*)",
            RegexOptions.Compiled);

        public sealed class Plan
        {
            public List<TyhpdefClassDeclaration> Externs { get; } = [];

            public Dictionary<string, string> WrapperRequires { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public bool Classified { get; set; }
        }

        /// <summary>
        /// Omits types that extend or implement an <c>extern</c> (and members that
        /// name omitted types), qualifies surviving intra-package names, then
        /// classifies remaining foreign types when the target is a Composer package
        /// (or package-shaped <c>--source</c> tree).
        /// Mutates <paramref name="file"/> (omits and qualification).
        /// </summary>
        public static Plan Apply(
            TyhpdefFile file,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            string searchRoot,
            IEnumerable<string>? excludedInternalTypes = null)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in excludedInternalTypes ?? [])
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    omitted.Add(TyhpdefApiIndex.NormalizeFqn(name));
                }
            }

            var plan = new Plan();
            var targetComposer = FindTargetComposerJson(options, searchRoot);
            ComposerJsonService.ComposerDependencySet? dependencies = null;
            TyhpdefExternCatalog? catalog = null;
            if (!string.IsNullOrWhiteSpace(targetComposer) && File.Exists(targetComposer))
            {
                dependencies = ComposerJsonService.TryReadDependencySet(targetComposer);
            }

            if (dependencies is not null)
            {
                catalog = TyhpdefExternCatalog.Build(options.CatalogRoots);
                WarnMissingRequiredWrappers(catalog, dependencies, result);
                plan.Classified = true;
                SeedExternBaseOmits(
                    file,
                    CollectDeclaredTypes(file),
                    catalog,
                    dependencies,
                    omitted,
                    result);
            }

            CascadeOmitAndStrip(file, omitted, result);

            // After omit: unique short-name qualify must not rewrite PHPDoc-only
            // shorts onto omitted FQCNs (those names are gone from the declared set).
            QualifyIntraPackage(file);

            if (plan.Classified && catalog is not null && dependencies is not null)
            {
                // Recompute: omission just removed declarations, so a reference to one of
                // those FQCNs from a surviving type must no longer classify as intra-package.
                CollectExternsAndRequires(
                    file,
                    CollectDeclaredTypes(file),
                    catalog,
                    dependencies,
                    plan);
            }

            return plan;
        }

        /// <summary>
        /// Rewrites unqualified names that this package itself generated
        /// (<c>array&lt;LogRecord&gt;</c> → <c>array&lt;\Ns\LogRecord&gt;</c>).
        /// Unqualified names that are not in the generated set stay unqualified
        /// and are never auto-<c>extern</c>.
        /// </summary>
        public static void QualifyIntraPackage(TyhpdefFile file)
        {
            var declared = CollectDeclaredTypes(file);
            if (declared.Fqcn.Count == 0)
            {
                return;
            }

            RewriteFileTypes(file, type => RewriteIntraPackage(type, currentNs: "", declared));
            foreach (var ns in file.Namespaces ?? [])
            {
                RewriteNamespaceTypes(ns, type => RewriteIntraPackage(type, ns.Name, declared));
            }
        }

        public static void WriteExternsFile(
            Plan plan,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            if (!plan.Classified)
            {
                return;
            }

            var directory = ResolveExternsDirectory(options);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var path = Path.Combine(directory, TyhpdefOutputLayout.ExternsFileName);
            if (plan.Externs.Count == 0)
            {
                TryDelete(path);
                return;
            }

            var externFile = new TyhpdefFile
            {
                Header = "Extern placeholders for optional Composer / extension peers.",
                GlobalTypes = plan.Externs
                    .OrderBy(e => e.ProvidedBy ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };

            string text;
            try
            {
                text = TyhpdefOutputWriter.Write(
                    externFile,
                    new TyhpdefOutputOptions { IncludeDocComments = false });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                result.Diagnostics.AddError(
                    Domain.Exceptions.MessageCode.TyhpdefOutputWriteError,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
                return;
            }

            try
            {
                Directory.CreateDirectory(directory);
                var parseDiagnostics = new DiagnosticBag();
                var ast = Tyhpdef.ParseContent(text, path, ParseMode.Tyhpdef, parseDiagnostics);
                if (ast is null || parseDiagnostics.HasErrors)
                {
                    var detail = parseDiagnostics.Errors.Count > 0
                        ? parseDiagnostics.Errors[0].Message
                        : "parse returned null";
                    Message.Error("CLI_TyhpdefParseFailed", path, detail);
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefParseError,
                        path,
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefParseFailed", path, detail));
                    return;
                }

                File.WriteAllText(path, text);
                result.GeneratedFiles.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.AddError(
                    Domain.Exceptions.MessageCode.TyhpdefOutputWriteError,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
            }
        }

        public static void ApplyWrapperRequires(Plan plan, TyhpdefGenerationOptions options)
        {
            if (!plan.Classified || plan.WrapperRequires.Count == 0)
            {
                return;
            }

            var wrapperComposer = FindWrapperComposerJson(options);
            if (string.IsNullOrWhiteSpace(wrapperComposer))
            {
                return;
            }

            var tyhpdef = new List<string>();
            var other = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, constraint) in plan.WrapperRequires)
            {
                if (VendorTyhpdefLayout.IsTyhpdefPackage(name))
                {
                    tyhpdef.Add(name);
                }
                else
                {
                    other[name] = constraint;
                }
            }

            if (tyhpdef.Count > 0)
            {
                ComposerJsonService.EnsureTyhpdefRequireDevEntries(wrapperComposer, tyhpdef);
            }

            if (other.Count > 0)
            {
                ComposerJsonService.EnsureRequireEntries(wrapperComposer, other);
            }
        }

        internal static string? FindTargetComposerJson(TyhpdefGenerationOptions options, string searchRoot)
        {
            if (options.Mode == TyhpdefGenerationMode.ComposerPackage
                && !string.IsNullOrWhiteSpace(options.PackagePath))
            {
                var fromPackage = Path.Combine(options.PackagePath, "composer.json");
                return File.Exists(fromPackage) ? fromPackage : null;
            }

            if (options.Mode != TyhpdefGenerationMode.PhpSourceFiles)
            {
                return null;
            }

            foreach (var candidate in EnumerateSourceComposerCandidates(options, searchRoot))
            {
                if (IsPackageShapedComposer(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static string? FindWrapperComposerJson(TyhpdefGenerationOptions options)
        {
            var start = ResolveExternsDirectory(options);
            if (string.IsNullOrWhiteSpace(start))
            {
                return null;
            }

            string dir;
            try
            {
                dir = Path.GetFullPath(start);
            }
            catch (Exception)
            {
                return null;
            }

            for (var i = 0; i < 4; i++)
            {
                var composerPath = Path.Combine(dir, "composer.json");
                var name = ReadComposerName(composerPath);
                if (!string.IsNullOrWhiteSpace(name)
                    && VendorTyhpdefLayout.IsTyhpdefPackage(name))
                {
                    return composerPath;
                }

                var parent = Directory.GetParent(dir);
                if (parent is null)
                {
                    break;
                }

                dir = parent.FullName;
            }

            return null;
        }

        internal static Classification ClassifyName(
            string rawName,
            DeclaredSet declared,
            TyhpdefExternCatalog catalog,
            ComposerJsonService.ComposerDependencySet dependencies)
        {
            var trimmed = (rawName ?? "").Trim();
            if (trimmed.Length == 0 || BuiltinNames.Contains(trimmed.TrimStart('\\')))
            {
                return Classification.Skip;
            }

            if (!trimmed.Contains('\\', StringComparison.Ordinal))
            {
                return Classification.Unqualified;
            }

            var fqcn = TyhpdefApiIndex.NormalizeFqn(trimmed);
            if (declared.Fqcn.Contains(fqcn))
            {
                return Classification.IntraPackage;
            }

            if (!catalog.TryGet(fqcn, out var entry))
            {
                return Classification.CatalogMiss;
            }

            var relation = RelationToTarget(entry, dependencies);
            return relation switch
            {
                PhpRelation.Require => Classification.RequireWrapper,
                PhpRelation.SuggestOrDev => Classification.EmitExtern,
                _ => Classification.UndeclaredRelationship,
            };
        }

        private static void CollectExternsAndRequires(
            TyhpdefFile file,
            DeclaredSet declared,
            TyhpdefExternCatalog catalog,
            ComposerJsonService.ComposerDependencySet dependencies,
            Plan plan)
        {
            var seenExtern = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, _) in WalkSignatureTypeNames(file))
            {
                if (!name.Contains('\\', StringComparison.Ordinal))
                {
                    continue;
                }

                var classification = ClassifyName(name, declared, catalog, dependencies);
                if (classification == Classification.RequireWrapper
                    && catalog.TryGet(TyhpdefApiIndex.NormalizeFqn(name), out var required))
                {
                    plan.WrapperRequires.TryAdd(
                        required.TyhpPackage,
                        ComposerJsonService.TyhpdefDevConstraint);
                    continue;
                }

                if (classification != Classification.EmitExtern
                    || !catalog.TryGet(TyhpdefApiIndex.NormalizeFqn(name), out var entry))
                {
                    continue;
                }

                if (!seenExtern.Add(entry.Fqcn))
                {
                    continue;
                }

                plan.Externs.Add(new TyhpdefClassDeclaration
                {
                    Kind = entry.Kind,
                    Name = entry.Fqcn,
                    IsExtern = true,
                    ProvidedBy = entry.TyhpPackage,
                });
            }
        }

        private static void SeedExternBaseOmits(
            TyhpdefFile file,
            DeclaredSet declared,
            TyhpdefExternCatalog catalog,
            ComposerJsonService.ComposerDependencySet dependencies,
            HashSet<string> omitted,
            TyhpdefGenerationResult result)
        {
            foreach (var site in EnumerateTypes(file))
            {
                foreach (var name in ExtendsAndImplements(site.Type))
                {
                    if (ClassifyName(name, declared, catalog, dependencies) != Classification.EmitExtern)
                    {
                        continue;
                    }

                    if (omitted.Add(site.Fqcn))
                    {
                        Warn(result, "CLI_TyhpdefOmittedExternBase", site.Fqcn, name);
                    }
                }
            }
        }

        private static void CascadeOmitAndStrip(
            TyhpdefFile file,
            HashSet<string> omitted,
            TyhpdefGenerationResult result)
        {
            if (omitted.Count == 0)
            {
                return;
            }

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var site in EnumerateTypes(file))
                {
                    if (omitted.Contains(site.Fqcn))
                    {
                        continue;
                    }

                    foreach (var name in ExtendsAndImplements(site.Type))
                    {
                        if (!TryResolveOmittedName(name, site.Namespace, omitted, out var parent))
                        {
                            continue;
                        }

                        omitted.Add(site.Fqcn);
                        Warn(result, "CLI_TyhpdefOmittedMissingBase", site.Fqcn, parent);
                        changed = true;
                        break;
                    }
                }
            }

            file.GlobalTypes.RemoveAll(type => omitted.Contains(TypeFqcn("", type.Name)));
            foreach (var ns in file.Namespaces ?? [])
            {
                ns.Classes.RemoveAll(type => omitted.Contains(TypeFqcn(ns.Name, type.Name)));
            }

            StripOmittedReferences(file, omitted, result);
        }

        private static void StripOmittedReferences(
            TyhpdefFile file,
            HashSet<string> omitted,
            TyhpdefGenerationResult result)
        {
            StripScope(
                currentNs: "",
                types: file.GlobalTypes,
                functions: file.GlobalFunctions,
                constants: file.GlobalConstants,
                aliases: file.TypeAliases,
                omitted,
                result);

            foreach (var ns in file.Namespaces ?? [])
            {
                StripScope(
                    ns.Name,
                    ns.Classes,
                    ns.Functions,
                    ns.Constants,
                    ns.TypeAliases,
                    omitted,
                    result);
            }
        }

        private static void StripScope(
            string currentNs,
            List<TyhpdefClassDeclaration>? types,
            List<TyhpdefFunction>? functions,
            List<TyhpdefConstant>? constants,
            List<TyhpdefTypeAlias>? aliases,
            HashSet<string> omitted,
            TyhpdefGenerationResult result)
        {
            foreach (var type in types ?? [])
            {
                var typeFqcn = TypeFqcn(currentNs, type.Name);
                if (type.Uses is { Count: > 0 })
                {
                    type.Uses.RemoveAll(used =>
                    {
                        if (!TryResolveOmittedName(used, currentNs, omitted, out var missing))
                        {
                            return false;
                        }

                        Warn(result, "CLI_TyhpdefOmittedMissingReference", typeFqcn + " use " + used, missing);
                        return true;
                    });
                }

                type.Methods?.RemoveAll(method =>
                    ShouldOmitCallable(method, currentNs, omitted, result, typeFqcn + "::" + method.Name));
                type.Operators?.RemoveAll(method =>
                    ShouldOmitCallable(method, currentNs, omitted, result, typeFqcn + "::" + method.Name));
                type.Properties?.RemoveAll(property =>
                {
                    if (!TryResolveOmittedName(property.Type, currentNs, omitted, out var missing))
                    {
                        return false;
                    }

                    Warn(result, "CLI_TyhpdefOmittedMissingReference", typeFqcn + "::$" + property.Name, missing);
                    return true;
                });
                type.Constants?.RemoveAll(constant =>
                {
                    if (!TryResolveOmittedName(constant.Type, currentNs, omitted, out var missing))
                    {
                        return false;
                    }

                    Warn(result, "CLI_TyhpdefOmittedMissingReference", typeFqcn + "::" + constant.Name, missing);
                    return true;
                });
                type.TypeAliases?.RemoveAll(alias =>
                {
                    if (!AliasReferencesOmitted(alias, currentNs, omitted, out var missing))
                    {
                        return false;
                    }

                    Warn(result, "CLI_TyhpdefOmittedMissingReference", typeFqcn + "::" + alias.Name, missing);
                    return true;
                });
            }

            functions?.RemoveAll(function =>
                ShouldOmitCallable(function, currentNs, omitted, result, TypeFqcn(currentNs, function.Name)));
            constants?.RemoveAll(constant =>
            {
                if (!TryResolveOmittedName(constant.Type, currentNs, omitted, out var missing))
                {
                    return false;
                }

                Warn(result, "CLI_TyhpdefOmittedMissingReference", TypeFqcn(currentNs, constant.Name), missing);
                return true;
            });
            aliases?.RemoveAll(alias =>
            {
                if (!AliasReferencesOmitted(alias, currentNs, omitted, out var missing))
                {
                    return false;
                }

                Warn(result, "CLI_TyhpdefOmittedMissingReference", TypeFqcn(currentNs, alias.Name), missing);
                return true;
            });
        }

        private static bool ShouldOmitCallable(
            TyhpdefMethod method,
            string currentNs,
            HashSet<string> omitted,
            TyhpdefGenerationResult result,
            string site)
        {
            if (TryFindOmittedInCallable(method, currentNs, omitted, out var missing))
            {
                Warn(result, "CLI_TyhpdefOmittedMissingReference", site, missing);
                return true;
            }

            return false;
        }

        private static bool TryFindOmittedInCallable(
            TyhpdefMethod method,
            string currentNs,
            HashSet<string> omitted,
            out string missing)
        {
            foreach (var parameter in method.GenericParameters ?? [])
            {
                if (TryResolveOmittedName(parameter.Constraint, currentNs, omitted, out missing)
                    || TryResolveOmittedName(parameter.Default, currentNs, omitted, out missing))
                {
                    return true;
                }
            }

            foreach (var parameter in method.Parameters ?? [])
            {
                if (TryResolveOmittedName(parameter.Type, currentNs, omitted, out missing))
                {
                    return true;
                }
            }

            if (TryResolveOmittedName(method.ReturnType, currentNs, omitted, out missing))
            {
                return true;
            }

            foreach (var overload in method.Overloads ?? [])
            {
                if (TryFindOmittedInCallable(overload, currentNs, omitted, out missing))
                {
                    return true;
                }
            }

            missing = "";
            return false;
        }

        private static IEnumerable<(TyhpdefClassDeclaration Type, string Namespace, string Fqcn)> EnumerateTypes(
            TyhpdefFile file)
        {
            foreach (var type in file.GlobalTypes ?? [])
            {
                yield return (type, "", TypeFqcn("", type.Name));
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var type in ns.Classes ?? [])
                {
                    yield return (type, ns.Name, TypeFqcn(ns.Name, type.Name));
                }
            }
        }

        private static string TypeFqcn(string currentNs, string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Contains('\\', StringComparison.Ordinal))
            {
                return TyhpdefApiIndex.NormalizeFqn(trimmed);
            }

            return TyhpdefApiIndex.Qualify(currentNs, trimmed);
        }

        private static bool AliasReferencesOmitted(
            TyhpdefTypeAlias alias,
            string currentNs,
            HashSet<string> omitted,
            out string missing)
        {
            foreach (var type in alias.EnumerateReferencedTypes())
            {
                if (TryResolveOmittedName(type, currentNs, omitted, out missing))
                {
                    return true;
                }
            }

            missing = "";
            return false;
        }

        private static bool TryResolveOmittedName(
            string? raw,
            string currentNs,
            HashSet<string> omitted,
            out string missing)
        {
            missing = "";
            foreach (var (name, qualified) in ExtractNames(raw, skip: []))
            {
                if (qualified)
                {
                    var fqcn = TyhpdefApiIndex.NormalizeFqn(name);
                    if (omitted.Contains(fqcn))
                    {
                        missing = fqcn;
                        return true;
                    }

                    continue;
                }

                // Unqualified: current namespace only. PHPDoc-only shorts with no
                // import stay as written (ResolveImportedNamesOnly); do not
                // last-segment match the whole omitted set (Acme\B\Consumer::get
                // @param Widget is not Acme\A\Widget). Harvest already FQCN'd
                // real use imports, so those hit the qualified branch.
                // Same-namespace omitted types (ModelInspector::inspect(): ModelInfo)
                // still match here.
                var inNs = TyhpdefApiIndex.Qualify(currentNs, name);
                if (omitted.Contains(inNs))
                {
                    missing = inNs;
                    return true;
                }
            }

            return false;
        }

        private static void WarnMissingRequiredWrappers(
            TyhpdefExternCatalog catalog,
            ComposerJsonService.ComposerDependencySet dependencies,
            TyhpdefGenerationResult result)
        {
            foreach (var phpPackage in dependencies.Require.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (phpPackage.Equals("php", StringComparison.OrdinalIgnoreCase)
                    || VendorTyhpdefLayout.IsFirstPartyComposerPackage(phpPackage))
                {
                    continue;
                }

                if (!catalog.TryGetWrapperForPhpPackage(phpPackage, out _))
                {
                    Warn(result, "CLI_TyhpdefRequiredPhpPackageMissingWrapper", phpPackage);
                }
            }
        }

        private static PhpRelation RelationToTarget(
            TyhpdefCatalogEntry entry,
            ComposerJsonService.ComposerDependencySet dependencies)
        {
            var inRequire = false;
            var inSuggestOrDev = false;
            foreach (var phpPackage in entry.WrappedPhpPackages)
            {
                if (dependencies.Require.Contains(phpPackage))
                {
                    inRequire = true;
                }
                else if (dependencies.Suggest.Contains(phpPackage)
                    || dependencies.RequireDev.Contains(phpPackage))
                {
                    inSuggestOrDev = true;
                }
            }

            if (inRequire)
            {
                return PhpRelation.Require;
            }

            if (inSuggestOrDev)
            {
                return PhpRelation.SuggestOrDev;
            }

            return PhpRelation.None;
        }

        private static IEnumerable<(string Name, bool Qualified)> WalkSignatureTypeNames(TyhpdefFile file)
        {
            foreach (var name in WalkTypesInScope(file.GlobalTypes, file.GlobalFunctions, file.GlobalConstants, file.TypeAliases))
            {
                yield return name;
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var name in WalkTypesInScope(ns.Classes, ns.Functions, ns.Constants, ns.TypeAliases))
                {
                    yield return name;
                }
            }
        }

        private static IEnumerable<(string Name, bool Qualified)> WalkTypesInScope(
            IEnumerable<TyhpdefClassDeclaration>? types,
            IEnumerable<TyhpdefFunction>? functions,
            IEnumerable<TyhpdefConstant>? constants,
            IEnumerable<TyhpdefTypeAlias>? aliases)
        {
            foreach (var type in types ?? [])
            {
                foreach (var name in ExtractNamesFromType(type))
                {
                    yield return name;
                }
            }

            foreach (var function in functions ?? [])
            {
                foreach (var name in ExtractNamesFromCallable(function))
                {
                    yield return name;
                }
            }

            foreach (var constant in constants ?? [])
            {
                foreach (var name in ExtractNames(constant.Type, skip: []))
                {
                    yield return name;
                }
            }

            foreach (var alias in aliases ?? [])
            {
                foreach (var type in alias.EnumerateReferencedTypes())
                {
                    foreach (var name in ExtractNames(type, skip: []))
                    {
                        yield return name;
                    }
                }
            }
        }

        private static IEnumerable<(string Name, bool Qualified)> ExtractNamesFromType(TyhpdefClassDeclaration type)
        {
            var skip = GenericNames(type.GenericParameters);
            foreach (var parameter in type.GenericParameters ?? [])
            {
                foreach (var name in ExtractNames(parameter.Constraint, skip))
                {
                    yield return name;
                }
            }

            foreach (var parent in ExtendsAndImplements(type))
            {
                foreach (var name in ExtractNames(parent, skip))
                {
                    yield return name;
                }
            }

            foreach (var used in type.Uses ?? [])
            {
                foreach (var name in ExtractNames(used, skip))
                {
                    yield return name;
                }
            }

            foreach (var property in type.Properties ?? [])
            {
                foreach (var name in ExtractNames(property.Type, skip))
                {
                    yield return name;
                }
            }

            foreach (var method in type.Methods ?? [])
            {
                foreach (var name in ExtractNamesFromCallable(method, skip))
                {
                    yield return name;
                }
            }
        }

        private static IEnumerable<string> ExtendsAndImplements(TyhpdefClassDeclaration type)
        {
            if (!string.IsNullOrWhiteSpace(type.Extends))
            {
                yield return type.Extends!;
            }

            foreach (var implemented in type.Implements ?? [])
            {
                if (!string.IsNullOrWhiteSpace(implemented))
                {
                    yield return implemented;
                }
            }
        }

        private static IEnumerable<(string Name, bool Qualified)> ExtractNamesFromCallable(
            TyhpdefMethod method,
            HashSet<string>? extraSkip = null)
        {
            var skip = GenericNames(method.GenericParameters);
            if (extraSkip is not null)
            {
                skip.UnionWith(extraSkip);
            }

            foreach (var parameter in method.GenericParameters ?? [])
            {
                foreach (var name in ExtractNames(parameter.Constraint, skip))
                {
                    yield return name;
                }
            }

            foreach (var parameter in method.Parameters ?? [])
            {
                foreach (var name in ExtractNames(parameter.Type, skip))
                {
                    yield return name;
                }
            }

            foreach (var name in ExtractNames(method.ReturnType, skip))
            {
                yield return name;
            }
        }

        internal static IEnumerable<(string Name, bool Qualified)> ExtractNames(
            string? type,
            HashSet<string> skip)
        {
            var trimmed = (type ?? "").Trim();
            if (trimmed.Length == 0)
            {
                yield break;
            }

            foreach (Match match in TypeName.Matches(trimmed))
            {
                var leadingSlash = match.Groups[1].Success;
                var ident = match.Groups[2].Value;
                if (BuiltinNames.Contains(ident) || skip.Contains(ident))
                {
                    continue;
                }

                var qualified = leadingSlash || ident.Contains('\\', StringComparison.Ordinal);
                var name = leadingSlash ? "\\" + ident : ident;
                yield return (name, qualified);
            }
        }

        private static string RewriteIntraPackage(string? type, string currentNs, DeclaredSet declared)
        {
            var trimmed = (type ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            return TypeName.Replace(trimmed, match =>
            {
                var leadingSlash = match.Groups[1].Success;
                var ident = match.Groups[2].Value;
                if (leadingSlash || ident.Contains('\\', StringComparison.Ordinal)
                    || BuiltinNames.Contains(ident))
                {
                    return match.Value;
                }

                var inCurrent = TyhpdefApiIndex.Qualify(currentNs, ident);
                if (declared.Fqcn.Contains(inCurrent))
                {
                    return inCurrent;
                }

                if (declared.ByShortName.TryGetValue(ident, out var matches) && matches.Count == 1)
                {
                    return matches[0];
                }

                return match.Value;
            });
        }

        private static void RewriteFileTypes(TyhpdefFile file, Func<string?, string> rewrite)
        {
            RewriteTypeList(file.GlobalTypes, rewrite);
            RewriteFunctionList(file.GlobalFunctions, rewrite);
            RewriteConstantList(file.GlobalConstants, rewrite);
            RewriteAliasList(file.TypeAliases, rewrite);
        }

        private static void RewriteNamespaceTypes(TyhpdefNamespace ns, Func<string?, string> rewrite)
        {
            RewriteTypeList(ns.Classes, rewrite);
            RewriteFunctionList(ns.Functions, rewrite);
            RewriteConstantList(ns.Constants, rewrite);
            RewriteAliasList(ns.TypeAliases, rewrite);
        }

        private static void RewriteAliasList(List<TyhpdefTypeAlias>? aliases, Func<string?, string> rewrite)
        {
            if (aliases is null)
            {
                return;
            }

            for (var i = 0; i < aliases.Count; i++)
            {
                aliases[i] = aliases[i] with
                {
                    AliasedType = rewrite(aliases[i].AliasedType) ?? aliases[i].AliasedType,
                    IntersectionTypes = (aliases[i].IntersectionTypes ?? [])
                        .Select(t => rewrite(t) ?? t)
                        .ToList(),
                    ObjectShape = RewriteObjectShape(aliases[i].ObjectShape, rewrite),
                    GenericParameters = RewriteGenericParameters(aliases[i].GenericParameters, rewrite),
                };
            }
        }

        private static void RewriteTypeList(List<TyhpdefClassDeclaration>? types, Func<string?, string> rewrite)
        {
            if (types is null)
            {
                return;
            }

            for (var i = 0; i < types.Count; i++)
            {
                types[i] = RewriteType(types[i], rewrite);
            }
        }

        private static TyhpdefClassDeclaration RewriteType(
            TyhpdefClassDeclaration type,
            Func<string?, string> rewrite)
        {
            var methods = type.Methods.Select(m => RewriteMethod(m, rewrite)).ToList();
            var properties = type.Properties.Select(p => p with { Type = rewrite(p.Type) }).ToList();
            var implements = type.Implements.Select(rewrite).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            return type with
            {
                Extends = string.IsNullOrWhiteSpace(type.Extends) ? type.Extends : rewrite(type.Extends),
                Implements = implements!,
                Methods = methods,
                Properties = properties,
                GenericParameters = RewriteGenericParameters(type.GenericParameters, rewrite),
            };
        }

        private static List<TyhpdefGenericParameter> RewriteGenericParameters(
            IEnumerable<TyhpdefGenericParameter>? parameters,
            Func<string?, string> rewrite)
        {
            var result = new List<TyhpdefGenericParameter>();
            foreach (var parameter in parameters ?? [])
            {
                result.Add(parameter with
                {
                    Constraint = string.IsNullOrWhiteSpace(parameter.Constraint)
                        ? parameter.Constraint
                        : rewrite(parameter.Constraint),
                    Default = string.IsNullOrWhiteSpace(parameter.Default)
                        ? parameter.Default
                        : rewrite(parameter.Default),
                });
            }

            return result;
        }

        private static TyhpdefMethod RewriteMethod(TyhpdefMethod method, Func<string?, string> rewrite)
        {
            var parameters = method.Parameters.Select(p => p with { Type = rewrite(p.Type) }).ToList();
            var generics = RewriteGenericParameters(method.GenericParameters, rewrite);
            if (method is TyhpdefFunction function)
            {
                return function with
                {
                    ReturnType = rewrite(function.ReturnType),
                    Parameters = parameters,
                    GenericParameters = generics,
                };
            }

            return method with
            {
                ReturnType = rewrite(method.ReturnType),
                Parameters = parameters,
                GenericParameters = generics,
            };
        }

        private static TyhpdefObjectShape? RewriteObjectShape(
            TyhpdefObjectShape? shape,
            Func<string?, string> rewrite)
        {
            if (shape is null)
            {
                return null;
            }

            return shape with
            {
                Methods = shape.Methods.Select(m => RewriteMethod(m, rewrite)).ToList(),
                Properties = shape.Properties.Select(p => p with { Type = rewrite(p.Type) }).ToList(),
                Constants = shape.Constants.Select(c => c with { Type = rewrite(c.Type) }).ToList(),
            };
        }

        private static void RewriteFunctionList(List<TyhpdefFunction>? functions, Func<string?, string> rewrite)
        {
            if (functions is null)
            {
                return;
            }

            for (var i = 0; i < functions.Count; i++)
            {
                functions[i] = (TyhpdefFunction)RewriteMethod(functions[i], rewrite);
            }
        }

        private static void RewriteConstantList(List<TyhpdefConstant>? constants, Func<string?, string> rewrite)
        {
            if (constants is null)
            {
                return;
            }

            for (var i = 0; i < constants.Count; i++)
            {
                constants[i] = constants[i] with { Type = rewrite(constants[i].Type) };
            }
        }

        private static DeclaredSet CollectDeclaredTypes(TyhpdefFile file)
        {
            var set = new DeclaredSet();
            foreach (var type in file.GlobalTypes ?? [])
            {
                AddDeclared(set, "", type.Name);
            }

            foreach (var alias in file.TypeAliases ?? [])
            {
                AddDeclared(set, "", alias.Name);
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var type in ns.Classes ?? [])
                {
                    AddDeclared(set, ns.Name, type.Name);
                }

                foreach (var alias in ns.TypeAliases ?? [])
                {
                    AddDeclared(set, ns.Name, alias.Name);
                }
            }

            return set;
        }

        private static void AddDeclared(DeclaredSet set, string ns, string name)
        {
            var fqcn = TyhpdefApiIndex.Qualify(ns, name);
            if (!set.Fqcn.Add(fqcn))
            {
                return;
            }

            var shortName = fqcn[(fqcn.LastIndexOf('\\') + 1)..];
            if (!set.ByShortName.TryGetValue(shortName, out var list))
            {
                list = [];
                set.ByShortName[shortName] = list;
            }

            list.Add(fqcn);
        }

        private static HashSet<string> GenericNames(IEnumerable<TyhpdefGenericParameter>? parameters)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters ?? [])
            {
                if (!string.IsNullOrWhiteSpace(parameter.Name))
                {
                    skip.Add(parameter.Name.Trim());
                }
            }

            return skip;
        }

        private static IEnumerable<string> EnumerateSourceComposerCandidates(
            TyhpdefGenerationOptions options,
            string searchRoot)
        {
            if (!string.IsNullOrWhiteSpace(searchRoot))
            {
                yield return Path.Combine(searchRoot, "composer.json");
            }

            foreach (var source in options.SourcePaths ?? [])
            {
                string? start = null;
                try
                {
                    if (Directory.Exists(source))
                    {
                        start = Path.GetFullPath(source);
                    }
                    else if (File.Exists(source))
                    {
                        start = Path.GetDirectoryName(Path.GetFullPath(source));
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(start))
                {
                    continue;
                }

                var dir = start;
                for (var i = 0; i < 3 && dir is not null; i++)
                {
                    yield return Path.Combine(dir, "composer.json");
                    dir = Directory.GetParent(dir)?.FullName;
                }
            }
        }

        private static bool IsPackageShapedComposer(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?.AsObject();
                return root?["autoload"] is not null;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                return false;
            }
        }

        private static string? ReadComposerName(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?.AsObject();
                return root?["name"]?.GetValue<string>();
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        private static string ResolveExternsDirectory(TyhpdefGenerationOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.OutputFileName))
            {
                var fromFile = Path.GetDirectoryName(options.OutputFileName);
                if (!string.IsNullOrWhiteSpace(fromFile))
                {
                    return fromFile;
                }
            }

            return options.OutputDirectory;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void Warn(TyhpdefGenerationResult result, string key, params object[] args)
        {
            var text = Message.Localize(key, args);
            result.Warnings.Add(text);
            Message.Warn(key, args);
        }

        internal enum Classification
        {
            Skip,
            Unqualified,
            IntraPackage,
            CatalogMiss,
            RequireWrapper,
            EmitExtern,
            UndeclaredRelationship,
        }

        private enum PhpRelation
        {
            None,
            Require,
            SuggestOrDev,
        }

        internal sealed class DeclaredSet
        {
            public HashSet<string> Fqcn { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, List<string>> ByShortName { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
