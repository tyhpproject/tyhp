using System.Text.RegularExpressions;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Parses <c>.tyhpdef</c> files into a <see cref="TyhpdefApiIndex"/> for <c>--verify</c>
    /// overlay apply (not binder overlay load).
    /// </summary>
    public static class TyhpdefAstApiReader
    {
        private static readonly Regex OmitLine = new(
            @"^\s*omit\s+(?:(?:public|protected|private|static|final|abstract|readonly)\s+)*(function|class|interface|trait|enum|const|fn)\s+(\S+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static TyhpdefApiIndex ReadFiles(
            IEnumerable<string> paths,
            DiagnosticBag diagnostics,
            bool tagless = false)
        {
            var index = new TyhpdefApiIndex();
            foreach (var path in paths)
            {
                MergeFile(index, path, diagnostics, tagless, isOverlay: false);
            }

            return index;
        }

        public static void MergeOverlayFile(
            TyhpdefApiIndex index,
            string path,
            DiagnosticBag diagnostics,
            bool tagless = false)
            => MergeFile(index, path, diagnostics, tagless, isOverlay: true);

        private static void MergeFile(
            TyhpdefApiIndex index,
            string path,
            DiagnosticBag diagnostics,
            bool tagless,
            bool isOverlay)
        {
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    path,
                    0,
                    0,
                    ex.Message);
                return;
            }

            var omits = isOverlay ? ExtractOmits(content) : [];
            var parseSource = isOverlay ? StripOmitStatements(content) : content;
            var fileDiagnostics = new DiagnosticBag();
            SrcFileAst? ast;
            try
            {
                ast = Tyhpdef.ParseContent(parseSource, path, ParseMode.Tyhpdef, fileDiagnostics, tagless);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, path, 0, 0, ex.Message);
                return;
            }

            if (ast is null || fileDiagnostics.HasErrors)
            {
                diagnostics.AddRange(fileDiagnostics.Errors);
                if (!fileDiagnostics.HasErrors)
                {
                    diagnostics.AddError(MessageCode.TyhpdefParseError, path, 0, 0, path);
                }

                return;
            }

            var parsed = new TyhpdefApiIndex();
            Walk(ast, parsed, ns: "");
            if (!isOverlay)
            {
                foreach (var symbol in parsed.Symbols.Values)
                {
                    index.AddOrReplace(symbol);
                }

                return;
            }

            ApplyOverlayIndex(index, parsed);
            foreach (var omit in omits)
            {
                index.Omit(omit);
            }
        }

        private static void ApplyOverlayIndex(TyhpdefApiIndex baseline, TyhpdefApiIndex overlay)
        {
            var membersByType = overlay.Symbols.Values
                .Where(s => s.Fqn.Contains("::", StringComparison.Ordinal))
                .GroupBy(s => s.Fqn[..s.Fqn.IndexOf("::", StringComparison.Ordinal)], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in overlay.Symbols.Values.Where(s => !s.Fqn.Contains("::", StringComparison.Ordinal)))
            {
                membersByType.TryGetValue(symbol.Fqn, out var members);
                if (IsTypeKind(symbol.Kind))
                {
                    baseline.MergeOverlayType(symbol, members ?? [], symbol.IsPartial);
                    membersByType.Remove(symbol.Fqn);
                    continue;
                }

                baseline.AddOrReplace(symbol);
            }

            foreach (var leftover in membersByType.SelectMany(kv => kv.Value))
            {
                baseline.AddOrReplace(leftover);
            }
        }

        private static bool IsTypeKind(string kind)
            => kind is "class" or "interface" or "trait" or "enum" or "struct" or "extension";

        internal static List<string> ExtractOmits(string source)
        {
            var omits = new List<string>();
            var braceDepth = 0;
            string? lastType = null;

            foreach (var rawLine in SplitLines(source))
            {
                var line = rawLine.Trim();
                var omitMatch = OmitLine.Match(rawLine);
                if (omitMatch.Success)
                {
                    var kind = omitMatch.Groups[1].Value;
                    var name = TrimOmitName(omitMatch.Groups[2].Value);
                    if (braceDepth > 0 && lastType is not null && !IsTypeKind(kind.ToLowerInvariant()))
                    {
                        omits.Add(TyhpdefApiIndex.MemberFqn(lastType, name));
                    }
                    else
                    {
                        omits.Add(TyhpdefApiIndex.NormalizeFqn(name));
                    }
                }
                else
                {
                    var typeName = MatchTypeHeader(line);
                    if (typeName is not null)
                    {
                        lastType = TyhpdefApiIndex.NormalizeFqn(typeName);
                    }
                }

                braceDepth += CountChar(rawLine, '{') - CountChar(rawLine, '}');
                if (braceDepth <= 0)
                {
                    braceDepth = 0;
                    lastType = null;
                }
            }

            return omits;
        }

        internal static string StripOmitStatements(string source)
        {
            var lines = new List<string>();
            foreach (var line in SplitLines(source))
            {
                if (OmitLine.IsMatch(line))
                {
                    continue;
                }

                lines.Add(line);
            }

            return string.Join('\n', lines);
        }

        private static IEnumerable<string> SplitLines(string source)
            => source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        private static string TrimOmitName(string raw)
        {
            var name = raw.Trim().TrimEnd(';');
            var paren = name.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0)
            {
                name = name[..paren];
            }

            var brace = name.IndexOf('{', StringComparison.Ordinal);
            if (brace >= 0)
            {
                name = name[..brace];
            }

            return name.Trim().TrimStart('$');
        }

        private static string? MatchTypeHeader(string line)
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (IsTypeKind(tokens[i].ToLowerInvariant()) && i + 1 < tokens.Length)
                {
                    return TrimOmitName(tokens[i + 1]);
                }
            }

            return null;
        }

        private static int CountChar(string text, char ch)
        {
            var count = 0;
            foreach (var c in text)
            {
                if (c == ch)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Walk(IBase2Ast? node, TyhpdefApiIndex index, string ns)
        {
            if (node is null)
            {
                return;
            }

            switch (node)
            {
                case PhpTopStatementListAst list:
                    foreach (var item in list.GetAllNotNull())
                    {
                        Walk(item, index, ns);
                    }

                    return;
                case PhpNamespaceDeclAst namespaceDecl:
                    Walk(namespaceDecl.TopStatements, index, CombineNs(ns, namespaceDecl.Identifier));
                    return;
                case PhpBlockNamespaceDeclAst blockNs:
                    Walk(blockNs.TopStatements, index, CombineNs(ns, blockNs.Identifier));
                    return;
                case PhpDeclareAst declare:
                    Walk(declare.Body as IBase2Ast, index, ns);
                    return;
                case TyhpdefImportFunctionDeclAst function:
                    AddFunction(index, TyhpdefApiIndex.Qualify(ns, TyhpdefApiIndex.NameText(function.NameOrAlias).Length > 0
                        ? TyhpdefApiIndex.NameText(function.NameOrAlias)
                        : TyhpdefApiIndex.NameText(function)), "function", function.Parameters, function.ReturnType);
                    return;
                case TyhpdefImportConstAst constant:
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = TyhpdefApiIndex.Qualify(ns, TyhpdefApiIndex.NameText(constant.NameOrAlias).Length > 0
                            ? TyhpdefApiIndex.NameText(constant.NameOrAlias)
                            : TyhpdefApiIndex.NameText(constant)),
                        Kind = "const",
                        ReturnType = TyhpdefApiIndex.TypeText(constant.TypeExpr),
                    });
                    return;
                case TyhpdefImportObjectDeclAst type:
                    AddObject(index, ns, type);
                    return;
                case TyhpStructDeclAst structDecl:
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = TyhpdefApiIndex.Qualify(ns, TyhpdefApiIndex.NameText(structDecl)),
                        Kind = "struct",
                    });
                    return;
                case TyhpTypeAliasAst alias:
                {
                    var aliasFqn = TyhpdefApiIndex.Qualify(ns, TyhpdefApiIndex.NameText(alias.Name).Length > 0
                        ? TyhpdefApiIndex.NameText(alias.Name)
                        : TyhpdefApiIndex.NameText(alias));
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = aliasFqn,
                        Kind = alias.StructShape is not null ? "struct" : "typealias",
                        ReturnType = TyhpdefApiIndex.TypeText(alias.TypeExpression),
                    });
                    return;
                }
                case TyhpdefStandaloneExtensionDeclAst extension:
                    AddExtension(index, ns, extension);
                    return;
                default:
                    foreach (var child in node.AstChildren)
                    {
                        Walk(child, index, ns);
                    }

                    return;
            }
        }

        private static void AddObject(TyhpdefApiIndex index, string ns, TyhpdefImportObjectDeclAst type)
        {
            var kind = KindFromDecl(type.DeclType);
            var typeFqn = TyhpdefApiIndex.Qualify(ns, TyhpdefApiIndex.NameText(type.NameOrAlias).Length > 0
                ? TyhpdefApiIndex.NameText(type.NameOrAlias)
                : TyhpdefApiIndex.NameText(type));
            index.AddOrReplace(new TyhpdefApiSymbol
            {
                Fqn = typeFqn,
                Kind = kind,
                IsPartial = type.IsPartial,
            });

            if (type.Body is null)
            {
                return;
            }

            foreach (var member in type.Body.GetAllNotNull())
            {
                AddClassMember(index, typeFqn, member);
            }
        }

        private static void AddClassMember(TyhpdefApiIndex index, string typeFqn, IClassMember member)
        {
            switch (member)
            {
                case PhpMethodDeclAst method:
                    AddFunction(
                        index,
                        TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(method)),
                        "method",
                        method.Parameters,
                        method.ReturnType);
                    return;
                case PhpPropertyDeclAst property:
                    foreach (var item in property.Properties?.GetAllNotNull() ?? [])
                    {
                        index.AddOrReplace(new TyhpdefApiSymbol
                        {
                            Fqn = TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(item)),
                            Kind = "property",
                            ReturnType = TyhpdefApiIndex.TypeText(property.Type),
                        });
                    }

                    return;
                case TyhpdefImportConstDeclListAst consts:
                    foreach (var item in consts.GetAllNotNull())
                    {
                        index.AddOrReplace(new TyhpdefApiSymbol
                        {
                            Fqn = TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(item)),
                            Kind = "const",
                        });
                    }

                    return;
                case TyhpdefConstDeclListAst tyhpdefConsts:
                    foreach (var item in tyhpdefConsts.GetAllNotNull())
                    {
                        index.AddOrReplace(new TyhpdefApiSymbol
                        {
                            Fqn = TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(item)),
                            Kind = "const",
                        });
                    }

                    return;
                case PhpConstDeclListAst phpConsts:
                    foreach (var item in phpConsts.GetAllNotNull())
                    {
                        index.AddOrReplace(new TyhpdefApiSymbol
                        {
                            Fqn = TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(item)),
                            Kind = "const",
                            ReturnType = TyhpdefApiIndex.TypeText(item.Type),
                        });
                    }

                    return;
                case PhpEnumCaseAst enumCase:
                    index.AddOrReplace(new TyhpdefApiSymbol
                    {
                        Fqn = TyhpdefApiIndex.MemberFqn(
                            typeFqn,
                            TyhpdefApiIndex.NameText(enumCase.Name).Length > 0
                                ? TyhpdefApiIndex.NameText(enumCase.Name)
                                : TyhpdefApiIndex.NameText(enumCase)),
                        Kind = "enumcase",
                    });
                    return;
                case TyhpdefInlineExtensionFunctionAst inline:
                    if (inline.Method is not null)
                    {
                        AddFunction(
                            index,
                            TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(inline.Method)),
                            "method",
                            inline.Method.Parameters,
                            inline.Method.ReturnType);
                    }

                    return;
                case PhpTraitUseAst traitUse:
                    WalkAdaptations(traitUse);
                    return;
                default:
                    foreach (var child in member.AstChildren)
                    {
                        if (child is IClassMember nested)
                        {
                            AddClassMember(index, typeFqn, nested);
                        }
                    }

                    return;
            }
        }

        /// <summary>
        /// Walk <c>use</c> / <c>use extension</c> adaptations with the same <see cref="TyhpdefApiIndex.NameText"/>
        /// rule as binder hide/insteadof (Identifier defaults to <c>""</c>).
        /// </summary>
        private static void WalkAdaptations(PhpTraitUseAst traitUse)
        {
            var adaptations = traitUse.Adaptations;
            if (adaptations is null)
            {
                return;
            }

            foreach (var adaptation in adaptations.AstChildren)
            {
                if (adaptation is PhpTraitAliasAst alias)
                {
                    _ = AdaptationMemberKey(alias.MethodReference);
                }
            }
        }

        private static string AdaptationMemberKey(PhpTraitMemberRefAst? methodRef)
        {
            if (methodRef is null)
            {
                return "";
            }

            if (methodRef.IsOperator)
            {
                var op = methodRef.OperatorToken ?? "";
                var target = TyhpdefApiIndex.NameText(methodRef.OperatorTarget);
                return string.IsNullOrEmpty(target) ? op : op + "<" + target + ">";
            }

            return TyhpdefApiIndex.NameText(methodRef.MemberName);
        }

        private static void AddExtension(TyhpdefApiIndex index, string ns, TyhpdefStandaloneExtensionDeclAst extension)
        {
            var typeFqn = TyhpdefApiIndex.Qualify(ns, TyhpdefApiIndex.NameText(extension));
            index.AddOrReplace(new TyhpdefApiSymbol
            {
                Fqn = typeFqn,
                Kind = "extension",
            });

            foreach (var member in extension.FunctionList?.GetAllNotNull() ?? [])
            {
                if (member is TyhpdefInlineExtensionFunctionAst inline && inline.Method is not null)
                {
                    AddFunction(
                        index,
                        TyhpdefApiIndex.MemberFqn(typeFqn, TyhpdefApiIndex.NameText(inline.Method)),
                        "fn",
                        inline.Method.Parameters,
                        inline.Method.ReturnType);
                }
            }
        }

        private static void AddFunction(
            TyhpdefApiIndex index,
            string fqn,
            string kind,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType)
        {
            var existing = index.Get(fqn);
            var symbol = new TyhpdefApiSymbol
            {
                Fqn = fqn,
                Kind = kind,
                ReturnType = TyhpdefApiIndex.TypeText(returnType),
                Parameters = ReadParameters(parameters),
            };
            if (existing is not null)
            {
                var overloads = existing.Overloads.Count > 0
                    ? existing.Overloads.ToList()
                    : [existing];
                overloads.Add(symbol);
                index.AddOrReplace(existing with { Overloads = overloads });
                return;
            }

            index.AddOrReplace(symbol);
        }

        private static List<TyhpdefApiParameter> ReadParameters(PhpParameterListAst? parameters)
        {
            var list = new List<TyhpdefApiParameter>();
            if (parameters is null)
            {
                return list;
            }

            foreach (var parameter in parameters.GetAllNotNull())
            {
                list.Add(new TyhpdefApiParameter
                {
                    Name = parameter.Name,
                    Type = TyhpdefApiIndex.TypeText(parameter.Type),
                    Optional = parameter.DefaultValue is not null,
                    Variadic = parameter.IsVariadic,
                });
            }

            return list;
        }

        private static string KindFromDecl(TokenValueAst? declType)
        {
            var text = (declType?.ValueString ?? "class").Trim().ToLowerInvariant();
            return text switch
            {
                "interface" => "interface",
                "trait" => "trait",
                "enum" => "enum",
                "struct" => "struct",
                "extension" => "extension",
                _ => "class",
            };
        }

        private static string CombineNs(string ns, string? extra)
        {
            var added = (extra ?? "").Trim().TrimStart('\\');
            if (added.Length == 0)
            {
                return ns;
            }

            if (string.IsNullOrWhiteSpace(ns))
            {
                return added;
            }

            return ns.Trim().Trim('\\') + "\\" + added;
        }
    }
}
