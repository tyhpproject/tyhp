using System.Globalization;
using System.Text;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Diffs Reflection IR from multiple managed PHP minors into one gated <see cref="TyhpdefFile"/>.
    /// Identical symbols stay ungated; additions / removals / signature changes become
    /// <c>declare(php=…)</c> blocks or <c>#[\Tyhp\Php]</c> member attributes.
    /// </summary>
    public static class TyhpdefMultiTargetMerger
    {
        private static readonly HashSet<string> DeclareOnlyKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            "struct",
            "extension",
        };

        public static TyhpdefFile Merge(IReadOnlyList<(string Minor, TyhpdefFile File)> versions)
        {
            ArgumentNullException.ThrowIfNull(versions);
            if (versions.Count == 0)
            {
                return new TyhpdefFile();
            }

            var sorted = versions
                .Select(v => (Minor: NormalizeMinor(v.Minor), v.File))
                .OrderBy(v => v.Minor, Comparer<string>.Create(PhpRuntimeVersion.ComparePatch))
                .ToList();
            var allMinors = sorted.Select(v => v.Minor).ToList();
            var header = BuildHeader(sorted);

            if (sorted.Count == 1)
            {
                return sorted[0].File with { Header = header };
            }

            var buckets = new List<VersionedFile>(sorted.Count);
            foreach (var (minor, file) in sorted)
            {
                buckets.Add(Flatten(minor, file));
            }

            var output = new TyhpdefFile { Header = header };
            MergeNamespace(output, output, "", buckets, allMinors, isGlobal: true);
            foreach (var nsName in UnionNamespaceNames(buckets).OrderBy(n => n, StringComparer.Ordinal))
            {
                if (nsName.Length == 0)
                {
                    continue;
                }

                var ns = new TyhpdefNamespace { Name = nsName };
                MergeNamespace(output, ns, nsName, buckets, allMinors, isGlobal: false);
                if (HasNamespaceContent(ns))
                {
                    output.Namespaces.Add(ns);
                }
            }

            return output;
        }

        internal static string? ConstraintFor(IReadOnlyList<string> presentMinors, IReadOnlyList<string> allMinors)
        {
            if (presentMinors.Count == 0 || allMinors.Count == 0)
            {
                return null;
            }

            if (presentMinors.Count == allMinors.Count
                && presentMinors.SequenceEqual(allMinors, StringComparer.Ordinal))
            {
                return null;
            }

            var first = presentMinors[0];
            var last = presentMinors[^1];
            var startsAtMin = string.Equals(first, allMinors[0], StringComparison.Ordinal);
            var endsAtMax = string.Equals(last, allMinors[^1], StringComparison.Ordinal);
            if (startsAtMin && !endsAtMax)
            {
                return "<" + NextTargetAfter(last, allMinors);
            }

            if (!startsAtMin && endsAtMax)
            {
                return ">=" + first;
            }

            if (!startsAtMin && !endsAtMax)
            {
                return ">=" + first + " <" + NextTargetAfter(last, allMinors);
            }

            return null;
        }

        /// <summary>
        /// Next listed target after <paramref name="last"/>, not the next PHP minor.
        /// A 8.2+8.4 matrix treats 8.4 as the successor of 8.2.
        /// </summary>
        internal static string NextTargetAfter(string last, IReadOnlyList<string> allMinors)
        {
            for (var i = 0; i < allMinors.Count; i++)
            {
                if (string.Equals(allMinors[i], last, StringComparison.Ordinal) && i + 1 < allMinors.Count)
                {
                    return allMinors[i + 1];
                }
            }

            return NextMinor(last);
        }

        internal static string NextMinor(string minor)
        {
            var normalized = NormalizeMinor(minor);
            var parts = normalized.Split('.');
            if (parts.Length < 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minorNumber))
            {
                return normalized;
            }

            return major.ToString(CultureInfo.InvariantCulture) + "."
                + (minorNumber + 1).ToString(CultureInfo.InvariantCulture);
        }

        internal static bool ConstraintsOverlap(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return PhpVersionConstraint.AnyOverlap([left], [right]);
        }

        private static void MergeNamespace(
            TyhpdefFile file,
            object ungatedTarget,
            string nsName,
            List<VersionedFile> buckets,
            IReadOnlyList<string> allMinors,
            bool isGlobal)
        {
            MergeNamed(
                buckets.Select(b => (b.Minor, Slice(b, nsName).Functions)).ToList(),
                allMinors,
                fn => fn.Name,
                FunctionKey,
                CloneFunction,
                ApplyFunctionGate,
                (item, constraint) =>
                {
                    if (constraint == null)
                    {
                        AddFunction(ungatedTarget, item, isGlobal);
                    }
                    else
                    {
                        AddGatedFunction(file, item, constraint, nsName, isGlobal);
                    }
                });

            MergeNamed(
                buckets.Select(b => (b.Minor, Slice(b, nsName).Constants)).ToList(),
                allMinors,
                c => c.Name,
                ConstantKey,
                CloneConstant,
                ApplyConstantGate,
                (item, constraint) =>
                {
                    if (constraint == null)
                    {
                        AddConstant(ungatedTarget, item, isGlobal);
                    }
                    else
                    {
                        AddGatedConstant(file, item, constraint, nsName, isGlobal);
                    }
                });

            MergeNamed(
                buckets.Select(b => (b.Minor, Slice(b, nsName).TypeAliases)).ToList(),
                allMinors,
                a => a.Name,
                AliasKey,
                a => a,
                (_, __) => { },
                (item, constraint) =>
                {
                    if (constraint == null)
                    {
                        AddAlias(ungatedTarget, item, isGlobal);
                    }
                    else
                    {
                        AddGatedAlias(file, item, constraint, nsName, isGlobal);
                    }
                });

            MergeTypes(file, ungatedTarget, nsName, buckets, allMinors, isGlobal);
        }

        private static void MergeTypes(
            TyhpdefFile file,
            object ungatedTarget,
            string nsName,
            List<VersionedFile> buckets,
            IReadOnlyList<string> allMinors,
            bool isGlobal)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bucket in buckets)
            {
                foreach (var type in Slice(bucket, nsName).Types)
                {
                    names.Add(type.Name);
                }
            }

            foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                var present = new List<(string Minor, TyhpdefClassDeclaration Type)>();
                foreach (var bucket in buckets)
                {
                    var match = Slice(bucket, nsName).Types.LastOrDefault(t =>
                        string.Equals(t.Name, name, StringComparison.Ordinal));
                    if (match != null)
                    {
                        present.Add((bucket.Minor, match));
                    }
                }

                if (present.Count == 0)
                {
                    continue;
                }

                var declareOnly = DeclareOnlyKinds.Contains(present[0].Type.Kind);
                var groups = ContiguousGroups(present, t => TypeHeaderKey(t), allMinors);
                var canMergeMembers = !declareOnly
                    && groups.Count == 1
                    && groups[0].Select(g => g.Minor).SequenceEqual(allMinors, StringComparer.Ordinal);

                if (canMergeMembers)
                {
                    var merged = MergeClassMembers(groups[0], allMinors);
                    AddType(ungatedTarget, merged, isGlobal);
                    continue;
                }

                foreach (var group in groups)
                {
                    var constraint = ConstraintFor(group.Select(g => g.Minor).ToList(), allMinors);
                    var clone = CloneType(group[^1].Item, includeMembers: true);
                    if (declareOnly || !string.IsNullOrEmpty(constraint))
                    {
                        if (!declareOnly && constraint != null)
                        {
                            clone = clone with { PhpGate = constraint };
                            AddType(ungatedTarget, clone, isGlobal);
                        }
                        else
                        {
                            AddGatedType(file, clone, constraint ?? ">=0", nsName, isGlobal);
                        }
                    }
                    else
                    {
                        AddType(ungatedTarget, clone, isGlobal);
                    }
                }
            }
        }

        private static TyhpdefClassDeclaration MergeClassMembers(
            IReadOnlyList<(string Minor, TyhpdefClassDeclaration Type)> versions,
            IReadOnlyList<string> allMinors)
        {
            var template = CloneType(versions[^1].Type, includeMembers: false);
            template = template with
            {
                Methods = MergeMembers(
                    versions.Select(v => (v.Minor, v.Type.Methods ?? [])).ToList(),
                    allMinors,
                    m => m.Name,
                    MethodKey,
                    CloneMethod,
                    (method, gate) => method with { PhpGate = gate }),
                Properties = MergeMembers(
                    versions.Select(v => (v.Minor, v.Type.Properties ?? [])).ToList(),
                    allMinors,
                    p => p.Name,
                    PropertyKey,
                    CloneProperty,
                    (property, gate) => property with { PhpGate = gate }),
                Constants = MergeMembers(
                    versions.Select(v => (v.Minor, v.Type.Constants ?? [])).ToList(),
                    allMinors,
                    c => c.Name,
                    ConstantKey,
                    CloneConstant,
                    (constant, gate) => constant with { PhpGate = gate }),
                EnumCases = MergeMembers(
                    versions.Select(v => (v.Minor, v.Type.EnumCases ?? [])).ToList(),
                    allMinors,
                    c => c.Name,
                    EnumCaseKey,
                    c => c,
                    (enumCase, gate) => enumCase with { PhpGate = gate }),
                Operators = MergeMembers(
                    versions.Select(v => (v.Minor, v.Type.Operators ?? [])).ToList(),
                    allMinors,
                    o => o.Name,
                    MethodKey,
                    CloneMethod,
                    (op, gate) => op with { PhpGate = gate }),
                ExtensionMembers = versions[^1].Type.ExtensionMembers ?? [],
                ExtensionGroups = versions[^1].Type.ExtensionGroups ?? [],
                DocComment = FirstDoc(versions.Select(v => v.Type.DocComment)),
            };
            return template;
        }

        private static List<T> MergeMembers<T>(
            List<(string Minor, List<T> Items)> perVersion,
            IReadOnlyList<string> allMinors,
            Func<T, string> name,
            Func<T, string> signature,
            Func<T, T> clone,
            Func<T, string?, T> applyGate)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, items) in perVersion)
            {
                foreach (var item in items)
                {
                    names.Add(name(item));
                }
            }

            var result = new List<T>();
            foreach (var memberName in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                var present = new List<(string Minor, T Item)>();
                foreach (var (minor, items) in perVersion)
                {
                    var match = items.LastOrDefault(i => string.Equals(name(i), memberName, StringComparison.Ordinal));
                    if (match is not null)
                    {
                        present.Add((minor, match));
                    }
                }

                foreach (var group in ContiguousGroups(present, signature, allMinors))
                {
                    var constraint = ConstraintFor(group.Select(g => g.Minor).ToList(), allMinors);
                    result.Add(applyGate(clone(group[^1].Item), constraint));
                }
            }

            return result;
        }

        private static void MergeNamed<T>(
            List<(string Minor, List<T> Items)> perVersion,
            IReadOnlyList<string> allMinors,
            Func<T, string> name,
            Func<T, string> signature,
            Func<T, T> clone,
            Action<T, string?> applyGate,
            Action<T, string?> emit)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, items) in perVersion)
            {
                foreach (var item in items)
                {
                    names.Add(name(item));
                }
            }

            foreach (var symbolName in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                var present = new List<(string Minor, T Item)>();
                foreach (var (minor, items) in perVersion)
                {
                    var match = items.LastOrDefault(i => string.Equals(name(i), symbolName, StringComparison.Ordinal));
                    if (match is not null)
                    {
                        present.Add((minor, match));
                    }
                }

                foreach (var group in ContiguousGroups(present, signature, allMinors))
                {
                    var constraint = ConstraintFor(group.Select(g => g.Minor).ToList(), allMinors);
                    var item = clone(group[^1].Item);
                    applyGate(item, constraint);
                    emit(item, constraint);
                }
            }
        }

        private static List<List<(string Minor, T Item)>> ContiguousGroups<T>(
            IReadOnlyList<(string Minor, T Item)> present,
            Func<T, string> signature,
            IReadOnlyList<string> allMinors)
        {
            var groups = new List<List<(string Minor, T Item)>>();
            if (present.Count == 0)
            {
                return groups;
            }

            var current = new List<(string Minor, T Item)> { present[0] };
            var currentKey = signature(present[0].Item);
            for (var i = 1; i < present.Count; i++)
            {
                var key = signature(present[i].Item);
                var previousMinor = present[i - 1].Minor;
                var contiguous = AreAdjacentTargets(previousMinor, present[i].Minor, allMinors);
                if (contiguous && string.Equals(key, currentKey, StringComparison.Ordinal))
                {
                    current.Add(present[i]);
                    continue;
                }

                groups.Add(current);
                current = [present[i]];
                currentKey = key;
            }

            groups.Add(current);
            return groups;
        }

        private static bool AreAdjacentTargets(string previous, string current, IReadOnlyList<string> allMinors)
        {
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                return true;
            }

            var previousIndex = -1;
            var currentIndex = -1;
            for (var i = 0; i < allMinors.Count; i++)
            {
                if (previousIndex < 0 && string.Equals(allMinors[i], previous, StringComparison.Ordinal))
                {
                    previousIndex = i;
                }

                if (currentIndex < 0 && string.Equals(allMinors[i], current, StringComparison.Ordinal))
                {
                    currentIndex = i;
                }
            }

            return previousIndex >= 0 && currentIndex == previousIndex + 1;
        }

        private static VersionedFile Flatten(string minor, TyhpdefFile file)
        {
            var result = new VersionedFile(minor);
            AddSlice(result.Global, file.GlobalFunctions, file.GlobalConstants, file.TypeAliases, file.GlobalTypes);
            foreach (var ns in file.Namespaces ?? [])
            {
                var slice = result.Namespace(ns.Name);
                AddSlice(slice, ns.Functions, ns.Constants, ns.TypeAliases, ns.Classes);
            }

            foreach (var block in file.DeclareBlocks ?? [])
            {
                AddSlice(result.Global, block.Functions, block.Constants, block.TypeAliases, block.Classes);
                foreach (var ns in block.Namespaces ?? [])
                {
                    var slice = result.Namespace(ns.Name);
                    AddSlice(slice, ns.Functions, ns.Constants, ns.TypeAliases, ns.Classes);
                }
            }

            return result;
        }

        private static void AddSlice(
            NamespaceSlice slice,
            IEnumerable<TyhpdefFunction>? functions,
            IEnumerable<TyhpdefConstant>? constants,
            IEnumerable<TyhpdefTypeAlias>? aliases,
            IEnumerable<TyhpdefClassDeclaration>? types)
        {
            slice.Functions.AddRange(functions ?? []);
            slice.Constants.AddRange(constants ?? []);
            slice.TypeAliases.AddRange(aliases ?? []);
            slice.Types.AddRange(types ?? []);
        }

        private static NamespaceSlice Slice(VersionedFile file, string nsName)
            => nsName.Length == 0 ? file.Global : file.Namespace(nsName);

        private static IEnumerable<string> UnionNamespaceNames(List<VersionedFile> buckets)
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { "" };
            foreach (var bucket in buckets)
            {
                foreach (var name in bucket.Namespaces.Keys)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static string BuildHeader(IReadOnlyList<(string Minor, TyhpdefFile File)> versions)
        {
            var minors = string.Join(", ", versions.Select(v => v.Minor));
            var existing = versions[^1].File.Header?.Trim() ?? "";
            var gated = "Gated for PHP " + minors + " (Tyhp-managed runtimes)";
            if (existing.Length == 0)
            {
                return gated;
            }

            return existing + "\n" + gated;
        }

        private static string NormalizeMinor(string? minor)
            => PhpRuntimeVersion.ToMinor(minor) ?? (minor ?? "").Trim();

        private static string FunctionKey(TyhpdefFunction function)
            => MethodKey(function) + "\next:" + function.IsExtension;

        private static string MethodKey(TyhpdefMethod method)
        {
            var sb = new StringBuilder();
            sb.Append(method.Name);
            sb.Append('|');
            sb.Append(string.Join(',', method.Modifiers ?? []));
            sb.Append('|');
            sb.Append(method.ReturnType);
            sb.Append('|');
            sb.Append(method.ReturnsReference);
            sb.Append('|');
            sb.Append(method.IsDeprecated);
            sb.Append('|');
            sb.Append(method.IsObsolete);
            sb.Append('|');
            sb.Append(method is TyhpdefFunction { IsFallback: true });
            foreach (var parameter in method.Parameters ?? [])
            {
                sb.Append(";p:");
                sb.Append(parameter.Name);
                sb.Append(':');
                sb.Append(parameter.Type);
                sb.Append(':');
                sb.Append(parameter.DefaultValue);
                sb.Append(':');
                sb.Append(parameter.IsVariadic);
                sb.Append(':');
                sb.Append(parameter.IsByReference);
            }

            foreach (var overload in method.Overloads ?? [])
            {
                sb.Append(";o:");
                sb.Append(MethodKey(overload));
            }

            return sb.ToString();
        }

        private static string ConstantKey(TyhpdefConstant constant)
            => constant.Name + "|" + constant.Type + "|" + constant.Value + "|" + constant.IsDeprecated
                + "|" + string.Join(',', constant.Modifiers ?? []);

        /// <summary>
        /// Member signature for merge grouping. Hook list is part of the signature:
        /// storage vs hooked (or get-only vs get+set / <c>&amp;get</c> / hook modifiers)
        /// across targets becomes disjoint gated declarations, same as a method that
        /// gained a parameter.
        /// </summary>
        private static string PropertyKey(TyhpdefProperty property)
        {
            var sb = new StringBuilder();
            sb.Append(property.Name);
            sb.Append('|');
            sb.Append(property.Type);
            sb.Append('|');
            sb.Append(property.IsDeprecated);
            sb.Append('|');
            sb.Append(string.Join(',', property.Modifiers ?? []));
            foreach (var hook in (property.Hooks ?? [])
                .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(";h:");
                sb.Append(hook.Name);
                sb.Append(':');
                sb.Append(hook.ReturnsRef);
                sb.Append(':');
                sb.Append(string.Join(',', hook.Modifiers ?? []));
                foreach (var attribute in hook.Attributes ?? [])
                {
                    sb.Append('@');
                    sb.Append(attribute.Name);
                    if (attribute.Arguments is { Count: > 0 })
                    {
                        sb.Append('(');
                        sb.Append(string.Join(',', attribute.Arguments));
                        sb.Append(')');
                    }
                }
            }

            return sb.ToString();
        }

        private static string EnumCaseKey(TyhpdefEnumCase enumCase)
            => enumCase.Name + "|" + enumCase.BackingValue;

        private static string AliasKey(TyhpdefTypeAlias alias)
            => alias.Name + "|" + alias.AliasedType;

        private static string TypeHeaderKey(TyhpdefClassDeclaration type)
            => string.Join('|',
                type.Kind,
                type.Name,
                type.AsAlias,
                type.IsPartial,
                string.Join(',', type.Modifiers ?? []),
                type.Extends,
                string.Join(',', type.Implements ?? []),
                string.Join(',', type.Uses ?? []),
                type.BackingType,
                type.IsDeprecated,
                type.IsObsolete);

        private static TyhpdefFunction CloneFunction(TyhpdefFunction function)
            => function with
            {
                Parameters = [.. function.Parameters ?? []],
                Overloads = [.. (function.Overloads ?? []).Select(CloneMethod)],
                PhpGate = null,
            };

        private static TyhpdefMethod CloneMethod(TyhpdefMethod method)
            => method with
            {
                Parameters = [.. method.Parameters ?? []],
                Overloads = [.. (method.Overloads ?? []).Select(CloneMethod)],
                PhpGate = null,
            };

        private static TyhpdefConstant CloneConstant(TyhpdefConstant constant)
            => constant with { PhpGate = null };

        private static TyhpdefProperty CloneProperty(TyhpdefProperty property)
            => property with
            {
                PhpGate = null,
                Hooks = [.. (property.Hooks ?? []).Select(ClonePropertyHook)],
            };

        private static TyhpdefPropertyHook ClonePropertyHook(TyhpdefPropertyHook hook)
            => hook with
            {
                Modifiers = [.. hook.Modifiers ?? []],
                Attributes = [.. (hook.Attributes ?? []).Select(a => a with
                {
                    Arguments = [.. a.Arguments ?? []],
                })],
            };

        private static TyhpdefClassDeclaration CloneType(TyhpdefClassDeclaration type, bool includeMembers)
            => type with
            {
                PhpGate = null,
                Methods = includeMembers ? [.. (type.Methods ?? []).Select(CloneMethod)] : [],
                Properties = includeMembers ? [.. (type.Properties ?? []).Select(CloneProperty)] : [],
                Constants = includeMembers ? [.. (type.Constants ?? []).Select(CloneConstant)] : [],
                EnumCases = includeMembers ? [.. type.EnumCases ?? []] : [],
                TypeAliases = includeMembers ? [.. type.TypeAliases ?? []] : [],
                Operators = includeMembers ? [.. (type.Operators ?? []).Select(CloneMethod)] : [],
                ExtensionMembers = includeMembers ? [.. type.ExtensionMembers ?? []] : [],
                ExtensionGroups = includeMembers
                    ? (type.ExtensionGroups ?? []).Select(group => group with
                    {
                        GenericParameters = [.. group.GenericParameters ?? []],
                        Members = [.. group.Members ?? []],
                    }).ToList()
                    : [],
            };

        private static void ApplyFunctionGate(TyhpdefFunction function, string? constraint)
        {
            // Top-level functions use declare blocks, not member attributes.
            _ = function;
            _ = constraint;
        }

        private static void ApplyConstantGate(TyhpdefConstant constant, string? constraint)
        {
            _ = constant;
            _ = constraint;
        }

        private static void AddFunction(object target, TyhpdefFunction function, bool isGlobal)
        {
            if (isGlobal && target is TyhpdefFile file)
            {
                file.GlobalFunctions.Add(function);
                return;
            }

            if (target is TyhpdefNamespace ns)
            {
                ns.Functions.Add(function);
            }
        }

        private static void AddConstant(object target, TyhpdefConstant constant, bool isGlobal)
        {
            if (isGlobal && target is TyhpdefFile file)
            {
                file.GlobalConstants.Add(constant);
                return;
            }

            if (target is TyhpdefNamespace ns)
            {
                ns.Constants.Add(constant);
            }
        }

        private static void AddAlias(object target, TyhpdefTypeAlias alias, bool isGlobal)
        {
            if (isGlobal && target is TyhpdefFile file)
            {
                file.TypeAliases.Add(alias);
                return;
            }

            if (target is TyhpdefNamespace ns)
            {
                ns.TypeAliases.Add(alias);
            }
        }

        private static void AddType(object target, TyhpdefClassDeclaration type, bool isGlobal)
        {
            if (isGlobal && target is TyhpdefFile file)
            {
                file.GlobalTypes.Add(type);
                return;
            }

            if (target is TyhpdefNamespace ns)
            {
                ns.Classes.Add(type);
            }
        }

        private static void AddGatedFunction(
            TyhpdefFile file,
            TyhpdefFunction function,
            string constraint,
            string nsName,
            bool isGlobal)
        {
            var block = GetOrCreateBlock(file, constraint);
            if (isGlobal)
            {
                block.Functions.Add(function);
                return;
            }

            NamespaceOf(block, nsName).Functions.Add(function);
        }

        private static void AddGatedConstant(
            TyhpdefFile file,
            TyhpdefConstant constant,
            string constraint,
            string nsName,
            bool isGlobal)
        {
            var block = GetOrCreateBlock(file, constraint);
            if (isGlobal)
            {
                block.Constants.Add(constant);
                return;
            }

            NamespaceOf(block, nsName).Constants.Add(constant);
        }

        private static void AddGatedAlias(
            TyhpdefFile file,
            TyhpdefTypeAlias alias,
            string constraint,
            string nsName,
            bool isGlobal)
        {
            var block = GetOrCreateBlock(file, constraint);
            if (isGlobal)
            {
                block.TypeAliases.Add(alias);
                return;
            }

            NamespaceOf(block, nsName).TypeAliases.Add(alias);
        }

        private static void AddGatedType(
            TyhpdefFile file,
            TyhpdefClassDeclaration type,
            string constraint,
            string nsName,
            bool isGlobal)
        {
            var block = GetOrCreateBlock(file, constraint);
            if (isGlobal)
            {
                block.Classes.Add(type);
                return;
            }

            NamespaceOf(block, nsName).Classes.Add(type);
        }

        private static TyhpdefDeclareBlock GetOrCreateBlock(TyhpdefFile file, string constraint)
        {
            var existing = file.DeclareBlocks.FirstOrDefault(b =>
                string.Equals(b.Constraint, constraint, StringComparison.Ordinal));
            if (existing != null)
            {
                return existing;
            }

            var created = new TyhpdefDeclareBlock { Constraint = constraint };
            file.DeclareBlocks.Add(created);
            return created;
        }

        private static TyhpdefNamespace NamespaceOf(TyhpdefDeclareBlock block, string nsName)
        {
            var existing = block.Namespaces.FirstOrDefault(n =>
                string.Equals(n.Name, nsName, StringComparison.Ordinal));
            if (existing != null)
            {
                return existing;
            }

            var created = new TyhpdefNamespace { Name = nsName };
            block.Namespaces.Add(created);
            return created;
        }

        private static bool HasNamespaceContent(TyhpdefNamespace ns)
            => (ns.Functions?.Count ?? 0) > 0
                || (ns.Constants?.Count ?? 0) > 0
                || (ns.TypeAliases?.Count ?? 0) > 0
                || (ns.Classes?.Count ?? 0) > 0;

        private static string? FirstDoc(IEnumerable<string?> comments)
            => comments.LastOrDefault(c => !string.IsNullOrWhiteSpace(c));

        private sealed class VersionedFile
        {
            public VersionedFile(string minor)
            {
                this.Minor = minor;
            }

            public string Minor { get; }

            public NamespaceSlice Global { get; } = new();

            public Dictionary<string, NamespaceSlice> Namespaces { get; } = new(StringComparer.Ordinal);

            public NamespaceSlice Namespace(string name)
            {
                var key = name?.Trim().TrimStart('\\') ?? "";
                if (key.Length == 0)
                {
                    return this.Global;
                }

                if (!this.Namespaces.TryGetValue(key, out var slice))
                {
                    slice = new NamespaceSlice();
                    this.Namespaces[key] = slice;
                }

                return slice;
            }
        }

        private sealed class NamespaceSlice
        {
            public List<TyhpdefFunction> Functions { get; } = [];

            public List<TyhpdefConstant> Constants { get; } = [];

            public List<TyhpdefTypeAlias> TypeAliases { get; } = [];

            public List<TyhpdefClassDeclaration> Types { get; } = [];
        }
    }
}
