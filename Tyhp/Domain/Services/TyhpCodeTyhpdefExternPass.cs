using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Compiled-library post-pass: classify foreign names in the generated <c>package.tyhpdef</c>
    /// against this library's <c>composer.json</c>. Author-only
    /// (<c>require-dev</c> minus <c>require</c> minus <c>extra.tyhp.require</c>)
    /// owners become name-only <c>extern</c> with <c>@provided-by</c>. Ambient extras
    /// and runtime <c>require</c> stay FQNs. Unknown origin is left as written.
    /// </summary>
    internal static class TyhpCodeTyhpdefExternPass
    {
        private static readonly HashSet<string> BuiltinNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "int", "integer", "string", "bool", "boolean", "float", "double",
            "void", "mixed", "null", "true", "false", "never", "resource",
            "array", "iterable", "object", "callable", "self", "static", "parent",
            "this",
        };

        public static void Apply(
            TyhpdefFile file,
            GlobalScope globalScope,
            TyhpdefGenerationOptions options,
            IReadOnlyList<SrcFileAst> parsedFiles)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(globalScope);
            ArgumentNullException.ThrowIfNull(options);

            var composerPath = FindLibraryComposerJson(options, parsedFiles);
            if (string.IsNullOrWhiteSpace(composerPath))
            {
                return;
            }

            var dependencies = ComposerJsonService.TryReadDependencySet(composerPath);
            if (dependencies is null)
            {
                return;
            }

            var catalog = TyhpdefExternCatalog.Build(options.CatalogRoots);
            var declared = CollectDeclaredNames(file);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var externTypes = new List<TyhpdefClassDeclaration>();
            var externFunctions = new List<TyhpdefFunction>();
            var externConstants = new List<TyhpdefConstant>();

            foreach (var (name, hint) in CollectForeignNames(file))
            {
                if (!name.Contains('\\', StringComparison.Ordinal))
                {
                    continue;
                }

                var fqn = TyhpdefApiIndex.NormalizeFqn(name);
                if (fqn.Length <= 1 || declared.Contains(fqn) || !seen.Add(fqn))
                {
                    continue;
                }

                if (!TryResolveOwner(
                    fqn,
                    hint,
                    globalScope,
                    catalog,
                    options.OutputDirectory,
                    out var owner,
                    out var kind))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(owner)
                    || string.Equals(owner, dependencies.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (dependencies.Require.Contains(owner)
                    || dependencies.ExtraTyhpRequire.Contains(owner)
                    || !dependencies.RequireDev.Contains(owner))
                {
                    continue;
                }

                switch (kind)
                {
                    case "function":
                        externFunctions.Add(new TyhpdefFunction
                        {
                            Name = fqn,
                            IsExtern = true,
                            ProvidedBy = owner,
                        });
                        break;
                    case "const":
                        externConstants.Add(new TyhpdefConstant
                        {
                            Name = fqn,
                            IsExtern = true,
                            ProvidedBy = owner,
                        });
                        break;
                    case "trait":
                        break;
                    default:
                        externTypes.Add(new TyhpdefClassDeclaration
                        {
                            Kind = string.IsNullOrWhiteSpace(kind) ? "class" : kind,
                            Name = fqn,
                            IsExtern = true,
                            ProvidedBy = owner,
                        });
                        break;
                }
            }

            if (externTypes.Count == 0 && externFunctions.Count == 0 && externConstants.Count == 0)
            {
                return;
            }

            file.GlobalTypes.InsertRange(
                0,
                externTypes
                    .OrderBy(e => e.ProvidedBy ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
            file.GlobalFunctions.InsertRange(
                0,
                externFunctions
                    .OrderBy(e => e.ProvidedBy ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
            file.GlobalConstants.InsertRange(
                0,
                externConstants
                    .OrderBy(e => e.ProvidedBy ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
        }

        internal static string? FindLibraryComposerJson(
            TyhpdefGenerationOptions options,
            IReadOnlyList<SrcFileAst> parsedFiles)
        {
            foreach (var candidate in EnumerateComposerCandidates(options, parsedFiles))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateComposerCandidates(
            TyhpdefGenerationOptions options,
            IReadOnlyList<SrcFileAst> parsedFiles)
        {
            _ = parsedFiles;
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                yield break;
            }

            string dir;
            try
            {
                dir = Path.GetFullPath(options.OutputDirectory);
            }
            catch (Exception)
            {
                yield break;
            }

            yield return Path.Combine(dir, "composer.json");
            var parent = Directory.GetParent(dir)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                yield return Path.Combine(parent, "composer.json");
            }
        }

        private static HashSet<string> CollectDeclaredNames(TyhpdefFile file)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string ns, string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                names.Add(TyhpdefApiIndex.Qualify(ns, name));
            }

            foreach (var type in file.GlobalTypes ?? [])
            {
                Add("", type.Name);
            }

            foreach (var function in file.GlobalFunctions ?? [])
            {
                Add("", function.Name);
            }

            foreach (var constant in file.GlobalConstants ?? [])
            {
                Add("", constant.Name);
            }

            foreach (var alias in file.TypeAliases ?? [])
            {
                Add("", alias.Name);
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var type in ns.Classes ?? [])
                {
                    Add(ns.Name, type.Name);
                }

                foreach (var function in ns.Functions ?? [])
                {
                    Add(ns.Name, function.Name);
                }

                foreach (var constant in ns.Constants ?? [])
                {
                    Add(ns.Name, constant.Name);
                }

                foreach (var alias in ns.TypeAliases ?? [])
                {
                    Add(ns.Name, alias.Name);
                }
            }

            return names;
        }

        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectForeignNames(TyhpdefFile file)
        {
            foreach (var name in CollectFromScope(
                file.GlobalTypes,
                file.GlobalFunctions,
                file.GlobalConstants,
                file.TypeAliases))
            {
                yield return name;
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var name in CollectFromScope(ns.Classes, ns.Functions, ns.Constants, ns.TypeAliases))
                {
                    yield return name;
                }
            }

            foreach (var globalUse in file.GlobalUses ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(globalUse, skip: []))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }
            }
        }

        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectFromScope(
            IEnumerable<TyhpdefClassDeclaration>? types,
            IEnumerable<TyhpdefFunction>? functions,
            IEnumerable<TyhpdefConstant>? constants,
            IEnumerable<TyhpdefTypeAlias>? aliases)
        {
            foreach (var type in types ?? [])
            {
                if (type.IsExtern)
                {
                    continue;
                }

                foreach (var name in CollectFromType(type))
                {
                    yield return name;
                }
            }

            foreach (var function in functions ?? [])
            {
                if (function.IsExtern)
                {
                    continue;
                }

                foreach (var name in CollectFromCallable(function, extraSkip: null, includeBody: false))
                {
                    yield return name;
                }
            }

            foreach (var constant in constants ?? [])
            {
                if (constant.IsExtern)
                {
                    continue;
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(constant.Type, skip: []))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(constant.Value, skip: []))
                {
                    yield return (extracted.Name, OwnerKindHint.Either);
                }
            }

            foreach (var alias in aliases ?? [])
            {
                var skip = GenericNames(alias.GenericParameters);
                foreach (var name in CollectFromGenericParameters(alias.GenericParameters, skip))
                {
                    yield return name;
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(alias.AliasedType, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var parent in alias.IntersectionTypes ?? [])
                {
                    foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(parent, skip))
                    {
                        yield return (extracted.Name, OwnerKindHint.Type);
                    }
                }

                if (alias.ObjectShape is { } shape)
                {
                    foreach (var method in shape.Methods ?? [])
                    {
                        foreach (var name in CollectFromCallable(method, skip, includeBody: false))
                        {
                            yield return name;
                        }
                    }

                    foreach (var property in shape.Properties ?? [])
                    {
                        foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(property.Type, skip))
                        {
                            yield return (extracted.Name, OwnerKindHint.Type);
                        }
                    }

                    foreach (var constant in shape.Constants ?? [])
                    {
                        foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(constant.Type, skip))
                        {
                            yield return (extracted.Name, OwnerKindHint.Type);
                        }
                    }
                }
            }
        }

        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectFromType(TyhpdefClassDeclaration type)
        {
            var skip = GenericNames(type.GenericParameters);
            foreach (var name in CollectFromGenericParameters(type.GenericParameters, skip))
            {
                yield return name;
            }

            foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(type.Extends, skip))
            {
                yield return (extracted.Name, OwnerKindHint.Type);
            }

            foreach (var implemented in type.Implements ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(implemented, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }
            }

            foreach (var used in type.Uses ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(used, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }
            }

            foreach (var name in CollectFromAttributes(type.Attributes, skip))
            {
                yield return name;
            }

            foreach (var property in type.Properties ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(property.Type, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(property.CoalesceValue, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Either);
                }

                foreach (var name in CollectFromAttributes(property.Attributes, skip))
                {
                    yield return name;
                }
            }

            foreach (var method in type.Methods ?? [])
            {
                foreach (var name in CollectFromCallable(method, skip, includeBody: false))
                {
                    yield return name;
                }
            }

            foreach (var op in type.Operators ?? [])
            {
                foreach (var name in CollectFromCallable(op, skip, includeBody: false))
                {
                    yield return name;
                }
            }

            foreach (var enumCase in type.EnumCases ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(enumCase.BackingValue, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Either);
                }

                foreach (var name in CollectFromAttributes(enumCase.Attributes, skip))
                {
                    yield return name;
                }
            }

            foreach (var member in type.ExtensionMembers ?? [])
            {
                foreach (var name in CollectFromExtensionMember(member, skip))
                {
                    yield return name;
                }
            }

            foreach (var group in type.ExtensionGroups ?? [])
            {
                var groupSkip = GenericNames(group.GenericParameters);
                groupSkip.UnionWith(skip);
                foreach (var name in CollectFromGenericParameters(group.GenericParameters, groupSkip))
                {
                    yield return name;
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(group.TargetType, groupSkip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var member in group.Members ?? [])
                {
                    foreach (var name in CollectFromExtensionMember(member, groupSkip))
                    {
                        yield return name;
                    }
                }
            }
        }

        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectFromExtensionMember(
            TyhpdefExtensionMember member,
            HashSet<string> skip)
        {
            var memberSkip = GenericNames(member.GenericParameters);
            memberSkip.UnionWith(skip);
            foreach (var name in CollectFromGenericParameters(member.GenericParameters, memberSkip))
            {
                yield return name;
            }

            foreach (var parameter in member.Parameters ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(parameter.Type, memberSkip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(parameter.DefaultValue, memberSkip))
                {
                    yield return (extracted.Name, OwnerKindHint.Either);
                }

                foreach (var name in CollectFromAttributes(parameter.Attributes, memberSkip))
                {
                    yield return name;
                }
            }

            foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(member.ReturnType, memberSkip))
            {
                yield return (extracted.Name, OwnerKindHint.Type);
            }

            foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(member.OperatorTarget, memberSkip))
            {
                yield return (extracted.Name, OwnerKindHint.Type);
            }

            foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(member.Body, memberSkip))
            {
                yield return (extracted.Name, OwnerKindHint.Either);
            }

            foreach (var name in CollectFromAttributes(member.Attributes, memberSkip))
            {
                yield return name;
            }
        }

        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectFromCallable(
            TyhpdefMethod method,
            HashSet<string>? extraSkip,
            bool includeBody)
        {
            var skip = GenericNames(method.GenericParameters);
            if (extraSkip is not null)
            {
                skip.UnionWith(extraSkip);
            }

            foreach (var name in CollectFromGenericParameters(method.GenericParameters, skip))
            {
                yield return name;
            }

            foreach (var parameter in method.Parameters ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(parameter.Type, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(parameter.DefaultValue, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Either);
                }

                foreach (var name in CollectFromAttributes(parameter.Attributes, skip))
                {
                    yield return name;
                }
            }

            foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(method.ReturnType, skip))
            {
                yield return (extracted.Name, OwnerKindHint.Type);
            }

            foreach (var name in CollectFromAttributes(method.Attributes, skip))
            {
                yield return name;
            }

            _ = includeBody;
        }

        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectFromAttributes(
            IEnumerable<TyhpdefAttribute>? attributes,
            HashSet<string> skip)
        {
            foreach (var attribute in attributes ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(attribute.Name, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }

                foreach (var argument in attribute.Arguments ?? [])
                {
                    foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(argument, skip))
                    {
                        yield return (extracted.Name, OwnerKindHint.Either);
                    }
                }
            }
        }

        /// <summary>
        /// A generic parameter's <c>extends</c> bound (<c>T extends \Foreign\Base</c>) is emitted
        /// text just like a parameter or return type, so it must be classified the same way.
        /// </summary>
        private static IEnumerable<(string Name, OwnerKindHint Hint)> CollectFromGenericParameters(
            IEnumerable<TyhpdefGenericParameter>? parameters,
            HashSet<string> skip)
        {
            foreach (var parameter in parameters ?? [])
            {
                foreach (var extracted in TyhpdefTrackBExternPass.ExtractNames(parameter.Constraint, skip))
                {
                    yield return (extracted.Name, OwnerKindHint.Type);
                }
            }
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

            skip.UnionWith(BuiltinNames);
            return skip;
        }

        private static bool TryResolveOwner(
            string fqn,
            OwnerKindHint hint,
            GlobalScope globalScope,
            TyhpdefExternCatalog catalog,
            string? outputDirectory,
            out string owner,
            out string kind)
        {
            owner = "";
            kind = "class";

            if (TryResolveBoundOwner(
                fqn,
                hint,
                globalScope,
                outputDirectory,
                out owner,
                out kind))
            {
                return true;
            }

            if ((hint == OwnerKindHint.Type || hint == OwnerKindHint.Either)
                && catalog.TryGet(fqn, out var typeEntry))
            {
                owner = typeEntry.TyhpPackage;
                kind = typeEntry.Kind;
                return !string.IsNullOrWhiteSpace(owner);
            }

            if ((hint == OwnerKindHint.Function || hint == OwnerKindHint.Either)
                && catalog.TryGetFunction(fqn, out var functionEntry))
            {
                owner = functionEntry.TyhpPackage;
                kind = "function";
                return !string.IsNullOrWhiteSpace(owner);
            }

            if ((hint == OwnerKindHint.Const || hint == OwnerKindHint.Either)
                && catalog.TryGetConst(fqn, out var constEntry))
            {
                owner = constEntry.TyhpPackage;
                kind = "const";
                return !string.IsNullOrWhiteSpace(owner);
            }

            if (hint == OwnerKindHint.Either && catalog.TryGetFunction(fqn, out functionEntry))
            {
                owner = functionEntry.TyhpPackage;
                kind = "function";
                return !string.IsNullOrWhiteSpace(owner);
            }

            if (hint == OwnerKindHint.Either && catalog.TryGetConst(fqn, out constEntry))
            {
                owner = constEntry.TyhpPackage;
                kind = "const";
                return !string.IsNullOrWhiteSpace(owner);
            }

            return false;
        }

        private static bool TryResolveBoundOwner(
            string fqn,
            OwnerKindHint hint,
            GlobalScope globalScope,
            string? outputDirectory,
            out string owner,
            out string kind)
        {
            owner = "";
            kind = "class";
            IBaseSymbol? best = null;
            foreach (var symbol in EnumerateTopLevelSymbols(globalScope))
            {
                if (symbol is BaseSymbol { IsExtern: true })
                {
                    continue;
                }

                if (!FqnEquals(symbol.FullyQualifiedName, fqn))
                {
                    continue;
                }

                if (!MatchesHint(symbol, hint))
                {
                    continue;
                }

                best = PreferDeclaration(best, symbol, hint);
            }

            if (best is null)
            {
                return false;
            }

            if (best is BuiltInUtilityTypeSymbol
                || best is ObjectDeclarationSymbol { IsCompilerGenerated: true })
            {
                return false;
            }

            kind = KindOf(best);
            if (kind == "alias" || kind == "trait")
            {
                return false;
            }

            owner = FindOwningPackage(best.SourceFile, outputDirectory);
            return !string.IsNullOrWhiteSpace(owner);
        }

        private static IEnumerable<IBaseSymbol> EnumerateTopLevelSymbols(GlobalScope globalScope)
        {
            var stack = new Stack<IBaseScope>();
            stack.Push(globalScope);
            while (stack.Count > 0)
            {
                var scope = stack.Pop();
                foreach (var symbol in scope.GetAllChildSymbols())
                {
                    if (symbol is ObjectDeclarationSymbol
                        or FunctionDeclarationSymbol
                        or ConstantSymbol
                        or TypeAliasSymbol
                        or BuiltInUtilityTypeSymbol)
                    {
                        yield return symbol;
                    }
                }

                foreach (var child in scope.GetAllChildScopes())
                {
                    stack.Push(child);
                }
            }
        }

        private static bool MatchesHint(IBaseSymbol symbol, OwnerKindHint hint)
            => hint switch
            {
                OwnerKindHint.Type => symbol is ObjectDeclarationSymbol or TypeAliasSymbol or BuiltInUtilityTypeSymbol,
                OwnerKindHint.Function => symbol is FunctionDeclarationSymbol,
                OwnerKindHint.Const => symbol is ConstantSymbol,
                _ => true,
            };

        private static IBaseSymbol PreferDeclaration(IBaseSymbol? current, IBaseSymbol candidate, OwnerKindHint hint)
        {
            if (current is null)
            {
                return candidate;
            }

            if (hint == OwnerKindHint.Either)
            {
                var currentRank = Rank(current);
                var candidateRank = Rank(candidate);
                return candidateRank < currentRank ? candidate : current;
            }

            return current;
        }

        private static int Rank(IBaseSymbol symbol)
            => symbol switch
            {
                ObjectDeclarationSymbol => 0,
                FunctionDeclarationSymbol => 1,
                ConstantSymbol => 2,
                _ => 3,
            };

        private static string KindOf(IBaseSymbol symbol)
            => symbol switch
            {
                FunctionDeclarationSymbol => "function",
                ConstantSymbol => "const",
                TypeAliasSymbol => "alias",
                ObjectDeclarationSymbol obj => obj.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => "interface",
                    PhpTypeDeclType.Enum => "enum",
                    PhpTypeDeclType.Trait => "trait",
                    _ => "class",
                },
                _ => "class",
            };

        private static bool FqnEquals(string? left, string right)
        {
            if (string.IsNullOrWhiteSpace(left))
            {
                return false;
            }

            return string.Equals(
                TyhpdefApiIndex.NormalizeFqn(left),
                TyhpdefApiIndex.NormalizeFqn(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string FindOwningPackage(string? sourceFile, string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return "";
            }

            foreach (var resolved in EnumerateSourceFilePaths(sourceFile, outputDirectory))
            {
                string dir;
                try
                {
                    dir = Path.GetDirectoryName(resolved) ?? "";
                }
                catch (Exception)
                {
                    continue;
                }

                for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(dir); i++)
                {
                    var composerPath = Path.Combine(dir, "composer.json");
                    var name = ReadComposerName(composerPath);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name!;
                    }

                    dir = Directory.GetParent(dir)?.FullName ?? "";
                }
            }

            return "";
        }

        private static IEnumerable<string> EnumerateSourceFilePaths(string sourceFile, string? outputDirectory)
        {
            if (Path.IsPathRooted(sourceFile))
            {
                yield return Path.GetFullPath(sourceFile);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                string combined;
                try
                {
                    combined = Path.GetFullPath(Path.Combine(outputDirectory, sourceFile));
                }
                catch (Exception)
                {
                    combined = "";
                }

                if (!string.IsNullOrWhiteSpace(combined))
                {
                    yield return combined;
                }
            }

            string cwd;
            try
            {
                cwd = Path.GetFullPath(sourceFile);
            }
            catch (Exception)
            {
                yield break;
            }

            yield return cwd;
        }

        private static string? ReadComposerName(string composerPath)
        {
            try
            {
                if (!File.Exists(composerPath))
                {
                    return null;
                }

                var set = ComposerJsonService.TryReadDependencySet(composerPath);
                return string.IsNullOrWhiteSpace(set?.Name) ? null : set!.Name;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private enum OwnerKindHint
        {
            Type,
            Function,
            Const,
            Either,
        }
    }
}
