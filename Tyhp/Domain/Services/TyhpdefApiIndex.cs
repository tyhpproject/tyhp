using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Flattened tyhpdef API used by <c>--verify</c> (merged IR/symbols, not file text).
    /// </summary>
    public sealed class TyhpdefApiIndex
    {
        private static readonly HashSet<string> WeakPhpTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "mixed",
            "array",
            "iterable",
            "object",
            "callable",
        };

        public Dictionary<string, TyhpdefApiSymbol> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Omitted { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TyhpdefApiSymbol? Get(string fqn)
            => this.Symbols.TryGetValue(NormalizeFqn(fqn), out var symbol) ? symbol : null;

        public void AddOrReplace(TyhpdefApiSymbol symbol)
        {
            var key = NormalizeFqn(symbol.Fqn);
            this.Symbols[key] = symbol with { Fqn = key };
            this.Omitted.Remove(key);
        }

        public void Omit(string fqn)
        {
            var key = NormalizeFqn(fqn);
            this.Symbols.Remove(key);
            this.Omitted.Add(key);
            if (string.Equals(KindOf(key), "type", StringComparison.Ordinal))
            {
                var prefix = key + "::";
                var members = this.Symbols.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var member in members)
                {
                    this.Symbols.Remove(member);
                    this.Omitted.Add(member);
                }
            }
        }

        public void MergeOverlayType(TyhpdefApiSymbol overlayType, IEnumerable<TyhpdefApiSymbol> overlayMembers, bool partial)
        {
            var key = NormalizeFqn(overlayType.Fqn);
            if (!partial || !this.Symbols.TryGetValue(key, out var existing))
            {
                if (!partial)
                {
                    var prefix = key + "::";
                    foreach (var memberKey in this.Symbols.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        this.Symbols.Remove(memberKey);
                    }
                }

                this.AddOrReplace(overlayType);
            }
            else if (!string.Equals(existing.Kind, overlayType.Kind, StringComparison.OrdinalIgnoreCase))
            {
                this.AddOrReplace(overlayType);
            }

            foreach (var member in overlayMembers)
            {
                this.AddOrReplace(member);
            }
        }

        public static TyhpdefApiIndex FromIr(TyhpdefFile file)
        {
            var index = new TyhpdefApiIndex();
            AddIrLists(index, "", file.GlobalConstants, file.GlobalFunctions, file.TypeAliases, file.GlobalTypes);
            foreach (var ns in file.Namespaces ?? [])
            {
                AddIrLists(index, ns.Name, ns.Constants, ns.Functions, ns.TypeAliases, ns.Classes);
            }

            return index;
        }

        public static string Qualify(string ns, string name)
        {
            var trimmedName = (name ?? "").Trim().TrimStart('\\');
            var trimmedNs = (ns ?? "").Trim().TrimStart('\\').Replace('/', '\\');
            if (trimmedNs.Length == 0)
            {
                return "\\" + trimmedName;
            }

            return "\\" + trimmedNs + "\\" + trimmedName;
        }

        public static string MemberFqn(string typeFqn, string memberName)
            => NormalizeFqn(typeFqn) + "::" + (memberName ?? "").Trim().TrimStart('$');

        public static string NormalizeFqn(string fqn)
        {
            var value = (fqn ?? "").Trim();
            if (value.Length == 0)
            {
                return "\\";
            }

            value = value.Replace('/', '\\');
            if (!value.StartsWith('\\'))
            {
                value = "\\" + value;
            }

            return value;
        }

        /// <summary>
        /// Text of an AST name node. <c>Identifier</c> defaults to <c>""</c> (not null), so
        /// <c>Identifier ?? ValueString</c> never falls through — same rule as binder
        /// <c>NameText</c> for <c>use extension</c> hide/insteadof.
        /// </summary>
        public static string NameText(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            if (node is TyhpdefIdentifierAliasAst alias)
            {
                var aliased = NameText(alias.AliasedAs);
                if (aliased.Length > 0)
                {
                    return aliased;
                }
            }

            if (!string.IsNullOrEmpty(node.Identifier))
            {
                return node.Identifier;
            }

            return node.ValueString ?? "";
        }

        public static string TypeText(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            switch (node)
            {
                case PhpTypeExpressionAst composite:
                    var parts = (composite.Types?.GetAllNotNull() ?? [])
                        .Select(TypeText)
                        .Where(t => t.Length > 0)
                        .ToList();
                    var joined = composite.TypeKind switch
                    {
                        PhpTypeKind.Intersection => string.Join("&", parts),
                        PhpTypeKind.Union => string.Join("|", parts),
                        _ => parts.Count > 0 ? parts[0] : "",
                    };
                    if (composite.IsNullable && joined.Length > 0 && !joined.StartsWith('?'))
                    {
                        joined = "?" + joined;
                    }

                    return joined;
                case PhpNamedTypeAst named:
                    return TypeText(named.Name);
                case TyhpGenericIdentifierAst generic:
                    var head = generic.ValueString ?? generic.Identifier ?? "";
                    var args = TypeText(generic.GenericArguments);
                    return args.Length > 0 ? head + "<" + args + ">" : head;
                default:
                    var name = NameText(node);
                    if (name.Length > 0 && node.AstChildren.Count == 0)
                    {
                        return name;
                    }

                    var childText = string.Join(
                        ", ",
                        node.AstChildren.Where(c => c is not null).Select(c => TypeText(c!)).Where(t => t.Length > 0));
                    if (name.Length > 0 && childText.Length > 0)
                    {
                        return name + "<" + childText + ">";
                    }

                    return name.Length > 0 ? name : childText;
            }
        }

        public static string NormalizeType(string type)
        {
            var text = (type ?? "").Trim();
            if (text.Length == 0)
            {
                return "";
            }

            text = text.Replace(" ", "", StringComparison.Ordinal);
            if (text.StartsWith('\\') && text.IndexOf('\\', 1) < 0)
            {
                var inner = text[1..];
                if (WeakPhpTypes.Contains(inner) || inner.Equals("string", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("int", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("float", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("bool", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("void", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("null", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("never", StringComparison.OrdinalIgnoreCase)
                    || inner.Equals("resource", StringComparison.OrdinalIgnoreCase))
                {
                    text = inner;
                }
            }

            return text;
        }

        /// <summary>
        /// Final type still covers the golden PHP shape: equal, generic/weak refinement,
        /// extra union arms, or (for params) a wider type. Illegal narrowing fails.
        /// </summary>
        public static bool CoversPhpShape(string goldenType, string finalType)
        {
            var golden = NormalizeType(goldenType);
            var final = NormalizeType(finalType);
            if (string.Equals(golden, final, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (golden.Length == 0)
            {
                // No reflected type hint at all — any stub/overlay-provided type is a
                // legitimate refinement (Reflection / PHP-source harvest's primary stub-enrichment case).
                return true;
            }

            if (WeakPhpTypes.Contains(golden))
            {
                // An explicit weak hint (mixed/array/iterable/object/callable) may be
                // refined into a wider, more descriptive shape (union arms and/or
                // generics), but narrowing it down to a single bare concrete type would
                // reject values the golden PHP signature still accepts at runtime.
                var finalPartsForWeak = SplitUnion(final);
                if (finalPartsForWeak.Count > 1 || final.Contains('<', StringComparison.Ordinal) || WeakPhpTypes.Contains(final))
                {
                    return true;
                }

                return false;
            }

            if (IsGenericRefinement(golden, final))
            {
                return true;
            }

            var goldenParts = SplitUnion(golden);
            var finalParts = SplitUnion(final);
            foreach (var part in goldenParts)
            {
                if (finalParts.Any(f => string.Equals(part, f, StringComparison.OrdinalIgnoreCase) || IsGenericRefinement(part, f)))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        public static bool CallablesCover(TyhpdefApiSymbol golden, TyhpdefApiSymbol final)
        {
            if (final.Overloads.Count == 0)
            {
                return SignatureCovers(golden, final);
            }

            return final.Overloads.Any(o => SignatureCovers(golden, o)) || SignatureCovers(golden, final);
        }

        private static bool SignatureCovers(TyhpdefApiSymbol golden, TyhpdefApiSymbol final)
        {
            if (!CoversPhpShape(golden.ReturnType, final.ReturnType))
            {
                return false;
            }

            var goldenParams = golden.Parameters;
            var finalParams = final.Parameters;
            var requiredFinal = finalParams.Count(p => !p.Optional && !p.Variadic);
            var goldenRequired = goldenParams.Count(p => !p.Optional && !p.Variadic);
            if (requiredFinal > goldenRequired && goldenRequired > 0)
            {
                return false;
            }

            var count = Math.Min(goldenParams.Count, finalParams.Count);
            for (var i = 0; i < count; i++)
            {
                if (!CoversPhpShape(goldenParams[i].Type, finalParams[i].Type))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsGenericRefinement(string golden, string final)
        {
            var goldenBase = BaseName(golden);
            var finalBase = BaseName(final);
            if (!string.Equals(goldenBase, finalBase, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var goldenHasArgs = golden.Contains('<', StringComparison.Ordinal);
            var finalHasArgs = final.Contains('<', StringComparison.Ordinal);
            return !goldenHasArgs && finalHasArgs;
        }

        private static string BaseName(string type)
        {
            var text = NormalizeType(type).TrimStart('?');
            var generic = text.IndexOf('<', StringComparison.Ordinal);
            if (generic >= 0)
            {
                text = text[..generic];
            }

            return text;
        }

        private static List<string> SplitUnion(string type)
            => type.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static string KindOf(string fqn)
            => fqn.Contains("::", StringComparison.Ordinal) ? "member" : "type";

        private static void AddIrLists(
            TyhpdefApiIndex index,
            string ns,
            IEnumerable<TyhpdefConstant>? constants,
            IEnumerable<TyhpdefFunction>? functions,
            IEnumerable<TyhpdefTypeAlias>? aliases,
            IEnumerable<TyhpdefClassDeclaration>? types)
        {
            foreach (var constant in constants ?? [])
            {
                index.AddOrReplace(new TyhpdefApiSymbol
                {
                    Fqn = Qualify(ns, constant.Name),
                    Kind = "const",
                    ReturnType = constant.Type ?? "",
                });
            }

            foreach (var function in functions ?? [])
            {
                index.AddOrReplace(FromIrFunction(Qualify(ns, function.Name), "function", function));
            }

            foreach (var alias in aliases ?? [])
            {
                index.AddOrReplace(new TyhpdefApiSymbol
                {
                    Fqn = Qualify(ns, alias.Name),
                    Kind = "typealias",
                    ReturnType = alias.HasObjectShape ? "object" : alias.AliasedType ?? "",
                });
            }

            foreach (var type in types ?? [])
            {
                var typeFqn = Qualify(ns, type.Name);
                index.AddOrReplace(new TyhpdefApiSymbol
                {
                    Fqn = typeFqn,
                    Kind = string.IsNullOrWhiteSpace(type.Kind) ? "class" : type.Kind,
                    IsPartial = type.IsPartial,
                });

                foreach (var constant in type.Constants ?? [])
                {
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = MemberFqn(typeFqn, constant.Name),
                        Kind = "const",
                        ReturnType = constant.Type ?? "",
                    });
                }

                foreach (var property in type.Properties ?? [])
                {
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = MemberFqn(typeFqn, property.Name),
                        Kind = "property",
                        ReturnType = property.Type ?? "",
                    });
                }

                foreach (var method in type.Methods ?? [])
                {
                    index.AddOrReplace(FromIrFunction(MemberFqn(typeFqn, method.Name), "method", method));
                }

                foreach (var op in type.Operators ?? [])
                {
                    index.AddOrReplace(FromIrFunction(MemberFqn(typeFqn, "operator " + op.Name), "operator", op));
                }

                foreach (var ext in type.ExtensionMembers ?? [])
                {
                    AddIrExtensionMember(index, typeFqn, ext);
                }

                foreach (var group in type.ExtensionGroups ?? [])
                {
                    foreach (var ext in group.Members ?? [])
                    {
                        AddIrExtensionMember(index, typeFqn, ext);
                    }
                }

                foreach (var enumCase in type.EnumCases ?? [])
                {
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = MemberFqn(typeFqn, enumCase.Name),
                        Kind = "enumcase",
                    });
                }
            }
        }

        private static void AddIrExtensionMember(TyhpdefApiIndex index, string typeFqn, TyhpdefExtensionMember ext)
        {
            index.AddOrReplace(new TyhpdefApiSymbol
            {
                Fqn = MemberFqn(typeFqn, ext.Name),
                Kind = string.IsNullOrWhiteSpace(ext.Kind) ? "function" : ext.Kind,
                ReturnType = ext.ReturnType ?? "",
                Parameters = (ext.Parameters ?? []).Select(FromIrParameter).ToList(),
            });
        }

        private static TyhpdefApiSymbol FromIrFunction(string fqn, string kind, TyhpdefMethod method)
        {
            var symbol = new TyhpdefApiSymbol
            {
                Fqn = fqn,
                Kind = kind,
                ReturnType = method.ReturnType ?? "",
                Parameters = (method.Parameters ?? []).Select(FromIrParameter).ToList(),
            };
            if (method.Overloads is { Count: > 0 })
            {
                symbol = symbol with
                {
                    Overloads = method.Overloads.Select(o => FromIrFunction(fqn, kind, o)).ToList(),
                };
            }

            return symbol;
        }

        private static TyhpdefApiParameter FromIrParameter(TyhpdefParameter parameter)
            => new()
            {
                Name = parameter.Name,
                Type = parameter.Type ?? "",
                Optional = parameter.DefaultValue is not null,
                Variadic = parameter.IsVariadic,
            };
    }

    public sealed record TyhpdefApiSymbol
    {
        public string Fqn { get; init; } = "";

        public string Kind { get; init; } = "";

        public bool IsPartial { get; init; }

        public string ReturnType { get; init; } = "";

        public List<TyhpdefApiParameter> Parameters { get; init; } = [];

        public List<TyhpdefApiSymbol> Overloads { get; init; } = [];
    }

    public sealed record TyhpdefApiParameter
    {
        public string Name { get; init; } = "";

        public string Type { get; init; } = "";

        public bool Optional { get; init; }

        public bool Variadic { get; init; }
    }
}
