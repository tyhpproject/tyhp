using System.Collections.Generic;
using System.Text;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Compact Layer 1 overlay stamps (<c>// @overlay-against:</c>) and emit-compatibility
    /// checks for overlay replace without a stamp.
    /// </summary>
    internal static class TyhpdefOverlayStamp
    {
        public const string GrammarAddonKey = "overlayAgainst";

        // PHP keeps functions, types, and constants in separate name spaces (and methods
        // vs class constants on a type). Stamp keys include the kind so `class Phar` /
        // `const PHAR` and `function debug` / `const DEBUG` do not last-wins overwrite.
        public const string KindFunction = "function";
        public const string KindType = "type";
        public const string KindConst = "const";
        public const string KindVariable = "variable";
        public const string KindMethod = "method";
        public const string KindProperty = "property";
        public const string KindObjectConst = "object-const";

        public static string? TryGetAuthoredStamp(IBase2Ast node)
        {
            if (node.AstGrammarAddons.TryGetValue(GrammarAddonKey, out var addon)
                && addon is TokenValueAst token
                && !string.IsNullOrWhiteSpace(token.ValueString))
            {
                return Normalize(token.ValueString);
            }

            return null;
        }

        public static string Normalize(string stamp)
        {
            // Compact stamps omit `...` on variadic parameters; authored comments often keep it.
            var stripped = StripGenerics(stamp).Replace("...", "", StringComparison.Ordinal).Trim();
            var builder = new StringBuilder(stripped.Length);
            var previousWhitespace = false;
            foreach (var ch in stripped)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWhitespace)
                    {
                        builder.Append(' ');
                    }

                    previousWhitespace = true;
                    continue;
                }

                previousWhitespace = false;
                builder.Append(ch);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Layer 1 may declare several overloads of one function/method. Capture stores every
        /// compact spell under the same key, separated by <see cref="StampListSeparator"/>.
        /// Stamp compare (8021) succeeds when the authored comment matches any of them.
        /// <c>tyhp overlay stamp</c> still writes the first (primary) overload.
        /// </summary>
        public const char StampListSeparator = '\n';

        public static string PrimaryStamp(string stamp)
        {
            if (string.IsNullOrEmpty(stamp))
            {
                return stamp;
            }

            var separator = stamp.IndexOf(StampListSeparator);
            return separator < 0 ? stamp : stamp[..separator];
        }

        public static bool MatchesAnyRecordedStamp(string authored, string? recorded)
        {
            if (string.IsNullOrEmpty(authored) || string.IsNullOrEmpty(recorded))
            {
                return false;
            }

            foreach (var candidate in EnumerateRecordedStamps(recorded))
            {
                if (string.Equals(authored, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string Spell(IBaseSymbol symbol)
        {
            return symbol switch
            {
                FunctionDeclarationSymbol function => SpellFunction(function),
                ObjectDeclarationSymbol type => SpellTypeHeader(type),
                ObjectMethodSymbol method => SpellMethod(method),
                ObjectPropertySymbol property => SpellProperty(property),
                ObjectConstantSymbol constant => SpellConstant(constant.Name, constant.DeclaredType),
                ConstantSymbol constant => SpellConstant(constant.Name, constant.DeclaredType),
                VariableSymbol variable => SpellVariable(variable),
                _ => symbol.Name,
            };
        }

        public static string MemberKey(string typeFullyQualifiedName, string memberName)
            => MemberKey(typeFullyQualifiedName, memberName, KindMethod);

        public static string MemberKey(string typeFullyQualifiedName, string memberName, string memberKind)
            => NormalizeFqn(typeFullyQualifiedName) + "::" + memberKind + ":" + memberName;

        public static string SymbolKey(string fqn, string kind)
            => kind + ":" + NormalizeFqn(fqn);

        public static string KindOf(IBaseSymbol symbol) => symbol switch
        {
            FunctionDeclarationSymbol => KindFunction,
            ObjectDeclarationSymbol => KindType,
            ConstantSymbol => KindConst,
            VariableSymbol => KindVariable,
            _ => KindType,
        };

        public static string MemberKindOf(IBaseSymbol member) => member switch
        {
            ObjectConstantSymbol => KindObjectConst,
            ObjectPropertySymbol => KindProperty,
            _ => KindMethod,
        };

        public static string KindOfDeclaration(IBase2Ast node) => node switch
        {
            TyhpdefImportFunctionDeclAst => KindFunction,
            TyhpdefImportObjectDeclAst => KindType,
            TyhpdefImportConstAst => KindConst,
            TyhpdefImportVariableAst => KindVariable,
            PhpMethodDeclAst => KindMethod,
            PhpPropertyDeclAst => KindProperty,
            _ => KindType,
        };

        public static string MemberKindOfDeclaration(IBase2Ast member) => member switch
        {
            PhpPropertyDeclAst => KindProperty,
            PhpMethodDeclAst => KindMethod,
            _ => KindObjectConst,
        };

        /// <summary>
        /// Records compact Layer 1 stamps for <paramref name="symbol"/> (and, when it is a
        /// type, each of its members / constants) into <paramref name="stamps"/>.
        /// </summary>
        public static void Record(IBaseSymbol symbol, IDictionary<string, string> stamps)
        {
            if (symbol is not BaseSymbol baseSymbol
                || string.IsNullOrEmpty(baseSymbol.FullyQualifiedName))
            {
                return;
            }

            if (symbol is not ObjectMethodSymbol
                and not ObjectPropertySymbol
                and not ObjectConstantSymbol)
            {
                var key = SymbolKey(baseSymbol.FullyQualifiedName, KindOf(symbol));
                AddRecordedStamp(stamps, key, Spell(symbol));
                if (symbol is FunctionDeclarationSymbol function)
                {
                    foreach (var overload in function.Overloads)
                    {
                        AddRecordedStamp(stamps, key, Spell(overload));
                    }
                }
            }

            if (symbol is ObjectDeclarationSymbol type)
            {
                foreach (var member in type.EnumerateMembersAndConstants())
                {
                    var memberKey = MemberKey(
                        baseSymbol.FullyQualifiedName,
                        member.Name,
                        MemberKindOf(member));
                    AddRecordedStamp(stamps, memberKey, Spell(member));
                    if (member is ObjectMethodSymbol method)
                    {
                        foreach (var overload in method.Overloads)
                        {
                            AddRecordedStamp(stamps, memberKey, Spell(overload));
                        }
                    }
                }
            }
        }

        private static void AddRecordedStamp(IDictionary<string, string> stamps, string key, string stamp)
        {
            var normalized = Normalize(stamp);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            if (!stamps.TryGetValue(key, out var existing) || string.IsNullOrEmpty(existing))
            {
                stamps[key] = normalized;
                return;
            }

            foreach (var candidate in EnumerateRecordedStamps(existing))
            {
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            stamps[key] = existing + StampListSeparator + normalized;
        }

        private static IEnumerable<string> EnumerateRecordedStamps(string recorded)
        {
            var start = 0;
            while (start < recorded.Length)
            {
                var separator = recorded.IndexOf(StampListSeparator, start);
                if (separator < 0)
                {
                    yield return recorded[start..];
                    yield break;
                }

                if (separator > start)
                {
                    yield return recorded[start..separator];
                }

                start = separator + 1;
            }
        }

        /// <summary>
        /// True when <paramref name="stamp"/> is a name-only function/method stamp
        /// (<c>function foo</c> / <c>function foo;</c>) with no parameter list.
        /// </summary>
        public static bool IsNameOnlyFunctionStamp(string stamp)
        {
            var normalized = Normalize(stamp);
            const string prefix = "function ";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = normalized[prefix.Length..].Trim().TrimEnd(';').Trim();
            return rest.Length > 0 && rest.IndexOf('(') < 0;
        }

        public static string NormalizeFqn(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var trimmed = name.Trim();
            return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed;
        }

        public static bool IsCompatibleReplace(IBaseSymbol baseline, IBase2Ast overlayDecl)
        {
            if (baseline is FunctionDeclarationSymbol function
                && overlayDecl is TyhpdefImportFunctionDeclAst overlayFunction)
            {
                return IsCompatibleFunction(function, overlayFunction);
            }

            if (baseline is ObjectMethodSymbol method
                && overlayDecl is PhpMethodDeclAst overlayMethod)
            {
                return IsCompatibleMethod(method, overlayMethod);
            }

            if (baseline is ObjectDeclarationSymbol baselineType
                && overlayDecl is TyhpdefImportObjectDeclAst overlayType)
            {
                // A kind-unspecified `extern \Name;` baseline claims no kind, so any real
                // class / interface / enum overlay is a compatible replacement.
                return baselineType.ObjectKind == PhpTypeDeclType.Unspecified
                    || string.Equals(
                        baselineType.ObjectKind.ToString(),
                        overlayType.DeclType?.ValueString ?? "",
                        StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        /// <summary>
        /// Unstamped 8022 should judge the overlay against the captured Layer 1 compact
        /// stamps, not only the live symbol. A prior overlay (Layer 2 stub harvest) may
        /// already have replaced <paramref name="baseline"/>, and adding a compile-only
        /// attribute or restoring a Layer 1 parameter type is still a compatible rewrite
        /// of Reflection.
        /// </summary>
        public static bool IsCompatibleReplace(
            IBaseSymbol baseline,
            IBase2Ast overlayDecl,
            string? layer1Stamps)
        {
            if (IsCompatibleReplace(baseline, overlayDecl))
            {
                return true;
            }

            return !string.IsNullOrEmpty(layer1Stamps)
                && IsCompatibleReplaceAgainstStamps(layer1Stamps, overlayDecl);
        }

        public static bool IsCompatibleReplaceAgainstStamps(string layer1Stamps, IBase2Ast overlayDecl)
        {
            if (overlayDecl is not TyhpdefImportFunctionDeclAst and not PhpMethodDeclAst)
            {
                return false;
            }

            foreach (var stamp in EnumerateRecordedStamps(layer1Stamps))
            {
                if (TryParseStampParameterTypes(stamp, out var stampParams)
                    && IsCompatibleOverlayParameters(overlayDecl, stampParams))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCompatibleOverlayParameters(
            IBase2Ast overlayDecl,
            IReadOnlyList<(string Type, bool Optional, bool Variadic)> baseline)
        {
            List<PhpParameterAst> overlayParams;
            IReadOnlyDictionary<string, string> genericConstraints;
            if (overlayDecl is TyhpdefImportFunctionDeclAst overlayFunction)
            {
                overlayParams = overlayFunction.Parameters?.GetAllNotNull().ToList() ?? [];
                genericConstraints = CollectOverlayGenericConstraints(overlayFunction);
            }
            else if (overlayDecl is PhpMethodDeclAst overlayMethod)
            {
                overlayParams = overlayMethod.Parameters?.GetAllNotNull().ToList() ?? [];
                genericConstraints = CollectOverlayGenericConstraints(overlayMethod);
            }
            else
            {
                return false;
            }

            return IsCompatibleParameterLists(baseline, overlayParams.Count, i =>
            {
                var param = overlayParams[i];
                return (SpellType(param.Type), param.DefaultValue != null, param.IsVariadic);
            }, genericConstraints);
        }

        private static bool TryParseStampParameterTypes(
            string stamp,
            out List<(string Type, bool Optional, bool Variadic)> parameters)
        {
            parameters = [];
            var normalized = Normalize(stamp);
            var open = normalized.IndexOf('(');
            if (open < 0)
            {
                return false;
            }

            var close = normalized.LastIndexOf(')');
            if (close <= open)
            {
                return false;
            }

            var inside = normalized[(open + 1)..close].Trim();
            if (inside.Length == 0)
            {
                return true;
            }

            foreach (var part in SplitTopLevel(inside, ','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var dollar = trimmed.LastIndexOf('$');
                string type;
                if (dollar < 0)
                {
                    type = trimmed;
                }
                else if (dollar > 0 && trimmed[dollar - 1] == '&')
                {
                    type = trimmed[..(dollar - 1)].Trim();
                }
                else
                {
                    type = trimmed[..dollar].Trim();
                }

                parameters.Add((type, Optional: false, Variadic: false));
            }

            return true;
        }

        private static IEnumerable<string> SplitTopLevel(string text, char separator)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch is '(' or '<' or '[')
                {
                    depth++;
                    continue;
                }

                if (ch is ')' or '>' or ']' && depth > 0)
                {
                    depth--;
                    continue;
                }

                if (ch == separator && depth == 0)
                {
                    yield return text[start..i];
                    start = i + 1;
                }
            }

            if (start <= text.Length)
            {
                yield return text[start..];
            }
        }

        private static bool IsCompatibleFunction(
            FunctionDeclarationSymbol baseline,
            TyhpdefImportFunctionDeclAst overlay)
        {
            var overlayParams = overlay.Parameters?.GetAllNotNull().ToList() ?? [];
            var genericConstraints = CollectOverlayGenericConstraints(overlay);
            return IsCompatibleParameterLists(baseline.Parameters, overlayParams.Count, i =>
            {
                var param = overlayParams[i];
                return (SpellType(param.Type), param.DefaultValue != null, param.IsVariadic);
            }, genericConstraints);
        }

        private static bool IsCompatibleMethod(ObjectMethodSymbol baseline, PhpMethodDeclAst overlay)
        {
            var overlayParams = overlay.Parameters?.GetAllNotNull().ToList() ?? [];
            var genericConstraints = CollectOverlayGenericConstraints(overlay);
            return IsCompatibleParameterLists(baseline.Parameters, overlayParams.Count, i =>
            {
                var param = overlayParams[i];
                return (SpellType(param.Type), param.DefaultValue != null, param.IsVariadic);
            }, genericConstraints);
        }

        private static bool IsCompatibleParameterLists(
            IReadOnlyList<ParameterInfo> baseline,
            int overlayCount,
            Func<int, (string Type, bool Optional, bool Variadic)> overlayAt,
            IReadOnlyDictionary<string, string> overlayGenericConstraints)
        {
            var spelled = new (string Type, bool Optional, bool Variadic)[baseline.Count];
            for (var i = 0; i < baseline.Count; i++)
            {
                spelled[i] = (
                    SpellType(baseline[i].DeclaredType),
                    baseline[i].DefaultValue != null,
                    baseline[i].IsVariadic);
            }

            return IsCompatibleParameterLists(spelled, overlayCount, overlayAt, overlayGenericConstraints);
        }

        private static bool IsCompatibleParameterLists(
            IReadOnlyList<(string Type, bool Optional, bool Variadic)> baseline,
            int overlayCount,
            Func<int, (string Type, bool Optional, bool Variadic)> overlayAt,
            IReadOnlyDictionary<string, string> overlayGenericConstraints)
        {
            if (overlayCount > baseline.Count)
            {
                return false;
            }

            for (var i = overlayCount; i < baseline.Count; i++)
            {
                if (!baseline[i].Optional && !baseline[i].Variadic)
                {
                    return false;
                }
            }

            for (var i = 0; i < overlayCount; i++)
            {
                var overlay = overlayAt(i);
                if (!IsCompatibleParamType(baseline[i].Type, overlay.Type, overlayGenericConstraints))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCompatibleParamType(
            string baseline,
            string overlay,
            IReadOnlyDictionary<string, string> overlayGenericConstraints,
            HashSet<string>? visitingGenerics = null)
        {
            var left = Normalize(baseline);
            var right = Normalize(overlay);
            if (string.IsNullOrEmpty(left)
                || string.Equals(left, "mixed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (LooksLikeStaticValue(right))
            {
                return true;
            }

            if (overlayGenericConstraints.TryGetValue(right, out var constraint)
                && !string.IsNullOrEmpty(constraint)
                && (visitingGenerics == null || !visitingGenerics.Contains(right)))
            {
                visitingGenerics ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                visitingGenerics.Add(right);
                return IsCompatibleParamType(
                    left,
                    constraint,
                    overlayGenericConstraints,
                    visitingGenerics);
            }

            return false;
        }

        private static Dictionary<string, string> CollectOverlayGenericConstraints(IBase2Ast overlayDecl)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var list = ExtractOverlayGenericList(overlayDecl);
            if (list == null)
            {
                return result;
            }

            foreach (var genericArg in list.GetAllNotNull())
            {
                var name = !string.IsNullOrEmpty(genericArg.Identifier)
                    ? genericArg.Identifier
                    : genericArg.Name?.ValueString;
                if (string.IsNullOrEmpty(name) || genericArg.TypeConstraint == null)
                {
                    continue;
                }

                var spelled = SpellType(genericArg.TypeConstraint);
                if (!string.IsNullOrEmpty(spelled))
                {
                    result[name] = spelled;
                }
            }

            return result;
        }

        private static TyhpGenericsTypeArgumentListAst? ExtractOverlayGenericList(IBase2Ast overlayDecl)
        {
            if (overlayDecl is TyhpdefImportFunctionDeclAst function)
            {
                return ExtractGenericListFromName(function.NameOrAlias);
            }

            if (overlayDecl is PhpMethodDeclAst method)
            {
                if (method.AstGrammarAddons.TryGetValue("identifier", out var identifierAddon)
                    && identifierAddon is TyhpGenericsTypeArgumentListAst fromIdentifier)
                {
                    return fromIdentifier;
                }

                if (method.AstGrammarAddons.TryGetValue("nameOrAlias", out var nameOrAlias))
                {
                    return ExtractGenericListFromName(nameOrAlias);
                }
            }

            return null;
        }

        private static TyhpGenericsTypeArgumentListAst? ExtractGenericListFromName(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias == null)
            {
                return null;
            }

            // Functions attach the list as "GenericArguments"; some name forms use
            // "GenericParameters" (same list shape as class/method identifier addons).
            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericArguments", out var arguments)
                && arguments is TyhpGenericsTypeArgumentListAst argumentsList)
            {
                return argumentsList;
            }

            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericParameters", out var parameters)
                && parameters is TyhpGenericsTypeArgumentListAst parametersList)
            {
                return parametersList;
            }

            return null;
        }

        private static bool LooksLikeStaticValue(string type)
        {
            if (string.Equals(type, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (type.Length >= 2
                && ((type[0] == '"' && type[^1] == '"')
                    || (type[0] == '\'' && type[^1] == '\'')))
            {
                return true;
            }

            return double.TryParse(type, out _);
        }

        private static string SpellFunction(FunctionDeclarationSymbol function)
        {
            var name = function.OriginalPhpName ?? function.Name;
            return "function " + name + SpellParameters(function.Parameters) + SpellReturn(function.ReturnType);
        }

        private static string SpellMethod(ObjectMethodSymbol method)
            // Compact stamps omit visibility / static / abstract / final.
            => "function " + method.Name + SpellParameters(method.Parameters) + SpellReturn(method.ReturnType);

        private static string SpellTypeHeader(ObjectDeclarationSymbol type)
        {
            // A kind-unspecified `extern \Name;` has no kind word of its own; stamp it the way
            // it is written rather than leaking the internal enum name.
            var kind = type.ObjectKind == PhpTypeDeclType.Unspecified
                ? "extern"
                : type.ObjectKind.ToString().ToLowerInvariant();
            var name = type.OriginalPhpName ?? type.Name;
            var builder = new StringBuilder();
            builder.Append(kind).Append(' ').Append(name);
            if (type.ExtendsType != null)
            {
                builder.Append(" extends ").Append(SpellType(type.ExtendsType));
            }

            if (type.ImplementsTypes.Count > 0)
            {
                var clause = type.ObjectKind == PhpTypeDeclType.Interface ? " extends " : " implements ";
                builder.Append(clause);
                builder.Append(string.Join(", ", type.ImplementsTypes.Select(SpellType)));
            }

            return builder.ToString();
        }

        private static string SpellProperty(ObjectPropertySymbol property)
            => (SpellType(property.DeclaredType) + " " + property.Name).Trim();

        private static string SpellConstant(string name, ITypeExpression? type)
            => ("const " + SpellType(type) + " " + name).Trim();

        private static string SpellVariable(VariableSymbol variable)
            => (SpellType(variable.DeclaredType) + " " + variable.Name).Trim();

        private static string SpellParameters(IReadOnlyList<ParameterInfo> parameters)
        {
            var parts = parameters.Select(p =>
            {
                var type = SpellType(p.DeclaredType);
                return string.IsNullOrEmpty(type) ? p.Name : type + " " + p.Name;
            });
            return "(" + string.Join(", ", parts) + ")";
        }

        private static string SpellReturn(ITypeExpression? returnType)
        {
            var spelled = SpellType(returnType);
            return string.IsNullOrEmpty(spelled) ? "" : ": " + spelled;
        }

        private static string SpellType(ITypeExpression? type)
        {
            if (type == null)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(type.ValueString))
            {
                return ApplyNullable(type, StripGenerics(type.ValueString));
            }

            if (!string.IsNullOrEmpty(type.Identifier))
            {
                return ApplyNullable(type, StripGenerics(type.Identifier));
            }

            if (type is PhpNamedTypeAst named && named.Name != null)
            {
                var fromName = named.Name.ValueString ?? named.Name.Identifier;
                if (!string.IsNullOrEmpty(fromName))
                {
                    return StripGenerics(fromName);
                }
            }

            if (type is PhpTypeExpressionAst composite)
            {
                var parts = composite.Types?.GetAllNotNull()
                    .Select(SpellType)
                    .Where(static part => part.Length > 0)
                    .ToList();
                if (parts is { Count: > 0 })
                {
                    var separator = composite.TypeKind == PhpTypeKind.Intersection ? "&" : "|";
                    return ApplyNullable(composite, string.Join(separator, parts));
                }
            }

            foreach (var child in type.AstChildren)
            {
                if (child is ITypeExpression nested)
                {
                    var spelled = SpellType(nested);
                    if (!string.IsNullOrEmpty(spelled))
                    {
                        return spelled;
                    }
                }

                if (child is PhpNameAst name)
                {
                    var fromChild = name.ValueString ?? name.Identifier;
                    if (!string.IsNullOrEmpty(fromChild))
                    {
                        return StripGenerics(fromChild);
                    }
                }
            }

            return "";
        }

        /// <summary>
        /// <c>?T</c> when the spelled type is a single arm. A nullable union or
        /// intersection appends <c>|null</c>. An arm that is the type <c>null</c>
        /// stays as written.
        /// </summary>
        private static string ApplyNullable(ITypeExpression type, string spelled)
        {
            if (spelled.Length == 0 || type is not PhpTypeExpressionAst { IsNullable: true })
            {
                return spelled;
            }

            if (ContainsNullArm(spelled))
            {
                return spelled;
            }

            if (!spelled.Contains('|', StringComparison.Ordinal)
                && !spelled.Contains('&', StringComparison.Ordinal))
            {
                return "?" + spelled.TrimStart('?');
            }

            return spelled + "|null";
        }

        /// <summary>
        /// A null arm is the type <c>null</c>. A name that merely contains those letters
        /// (<c>NullLogger</c>) is still nullable.
        /// </summary>
        private static bool ContainsNullArm(string spelled)
        {
            var start = 0;
            for (var i = 0; i <= spelled.Length; i++)
            {
                if (i < spelled.Length && spelled[i] is not ('|' or '&'))
                {
                    continue;
                }

                if (IsNullArm(spelled.AsSpan(start, i - start)))
                {
                    return true;
                }

                start = i + 1;
            }

            return false;
        }

        private static bool IsNullArm(ReadOnlySpan<char> arm)
        {
            arm = arm.Trim();
            while (arm.Length > 1 && arm[0] == '(' && arm[^1] == ')')
            {
                arm = arm[1..^1].Trim();
            }

            if (arm.Length > 0 && arm[0] == '?')
            {
                arm = arm[1..].Trim();
            }

            return arm.Equals("null", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripGenerics(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            var depth = 0;
            foreach (var ch in text)
            {
                if (ch == '<')
                {
                    depth++;
                    continue;
                }

                if (ch == '>' && depth > 0)
                {
                    depth--;
                    continue;
                }

                if (depth == 0)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }
}
