using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Parses Layer 1 / Layer 2 <c>.tyhpdef</c> files into FQN-keyed signatures for
    /// <see cref="StubHarvestAuditService"/>. Overlay header <c>;</c> and brace
    /// <c>partial</c> for the same name merge: written generics / inheritance replace
    /// those clauses; members union with last-wins.
    /// </summary>
    internal static class StubAuditTyhpdefCatalog
    {
        public static StubAuditIndex ReadFiles(IEnumerable<string> paths, DiagnosticBag diagnostics)
        {
            var index = new StubAuditIndex();
            foreach (var path in paths)
            {
                MergeFile(index, path, diagnostics);
            }

            return index;
        }

        private static void MergeFile(StubAuditIndex index, string path, DiagnosticBag diagnostics)
        {
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.AddError(MessageCode.TyhpdefGenerationError, path, 0, 0, ex.Message);
                return;
            }

            var fileDiagnostics = new DiagnosticBag();
            SrcFileAst? ast;
            try
            {
                ast = Tyhpdef.ParseContent(content, path, ParseMode.Tyhpdef, fileDiagnostics);
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

            Walk(ast, index, ns: "");
        }

        private static void Walk(IBase2Ast? node, StubAuditIndex index, string ns)
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
                    MergeFunction(index, ReadFunction(ns, function));
                    return;
                case TyhpdefImportObjectDeclAst type:
                    MergeType(index, ReadType(ns, type));
                    return;
                default:
                    foreach (var child in node.AstChildren)
                    {
                        Walk(child, index, ns);
                    }

                    return;
            }
        }

        private static StubAuditCallable ReadFunction(string ns, TyhpdefImportFunctionDeclAst function)
        {
            var name = TyhpdefApiIndex.NameText(function.NameOrAlias);
            if (name.Length == 0)
            {
                name = TyhpdefApiIndex.NameText(function);
            }

            return new StubAuditCallable
            {
                Fqn = TyhpdefApiIndex.Qualify(ns, name),
                Name = name.Contains('\\', StringComparison.Ordinal)
                    ? name[(name.LastIndexOf('\\') + 1)..]
                    : name,
                Generics = SpellGenericList(ExtractGenericList(function.NameOrAlias)),
                ReturnType = TyhpdefApiIndex.TypeText(function.ReturnType),
                Parameters = ReadParameters(function.Parameters),
            };
        }

        private static StubAuditType ReadType(string ns, TyhpdefImportObjectDeclAst type)
        {
            var name = TyhpdefApiIndex.NameText(type.NameOrAlias);
            if (name.Length == 0)
            {
                name = TyhpdefApiIndex.NameText(type);
            }

            var fqn = TyhpdefApiIndex.Qualify(ns, name);
            var result = new StubAuditType
            {
                Fqn = fqn,
                Kind = KindFromDecl(type.DeclType),
                Generics = SpellGenericList(ExtractGenericList(type.NameOrAlias)),
                Extends = SpellTypeList(type.Extends),
                Implements = SpellTypeList(type.Implements),
            };

            if (type.Body is null)
            {
                return result;
            }

            foreach (var member in type.Body.GetAllNotNull())
            {
                switch (member)
                {
                    case PhpMethodDeclAst method:
                        var methodName = TyhpdefApiIndex.NameText(method);
                        result.Methods[methodName] = new StubAuditCallable
                        {
                            Fqn = TyhpdefApiIndex.MemberFqn(fqn, methodName),
                            Name = methodName,
                            Generics = SpellGenericList(ExtractGenericList(method)),
                            ReturnType = TyhpdefApiIndex.TypeText(method.ReturnType),
                            Parameters = ReadParameters(method.Parameters),
                        };
                        break;
                    case PhpPropertyDeclAst property:
                        var propertyType = TyhpdefApiIndex.TypeText(property.Type);
                        foreach (var item in property.Properties?.GetAllNotNull() ?? [])
                        {
                            var propertyName = TyhpdefApiIndex.NameText(item).TrimStart('$');
                            if (propertyName.Length > 0)
                            {
                                result.Properties[propertyName] = propertyType;
                            }
                        }

                        break;
                }
            }

            return result;
        }

        private static void MergeFunction(StubAuditIndex index, StubAuditCallable function)
        {
            if (index.Functions.TryGetValue(function.Fqn, out var existing))
            {
                index.Functions[function.Fqn] = MergeCallable(existing, function);
                return;
            }

            index.Functions[function.Fqn] = function;
        }

        private static void MergeType(StubAuditIndex index, StubAuditType type)
        {
            if (!index.Types.TryGetValue(type.Fqn, out var existing))
            {
                index.Types[type.Fqn] = type;
                return;
            }

            if (type.Generics.Length > 0)
            {
                existing.Generics = type.Generics;
            }

            if (type.Extends.Length > 0)
            {
                existing.Extends = type.Extends;
            }

            if (type.Implements.Length > 0)
            {
                existing.Implements = type.Implements;
            }

            foreach (var (name, method) in type.Methods)
            {
                existing.Methods[name] = existing.Methods.TryGetValue(name, out var previous)
                    ? MergeCallable(previous, method)
                    : method;
            }

            foreach (var (name, propertyType) in type.Properties)
            {
                existing.Properties[name] = propertyType;
            }
        }

        private static StubAuditCallable MergeCallable(StubAuditCallable existing, StubAuditCallable incoming)
        {
            if (incoming.Generics.Length > 0)
            {
                existing.Generics = incoming.Generics;
            }

            if (incoming.ReturnType.Length > 0)
            {
                existing.ReturnType = incoming.ReturnType;
            }

            if (incoming.Parameters.Count > 0)
            {
                existing.Parameters = incoming.Parameters;
            }

            return existing;
        }

        private static List<StubAuditParameter> ReadParameters(PhpParameterListAst? parameters)
        {
            var list = new List<StubAuditParameter>();
            if (parameters is null)
            {
                return list;
            }

            foreach (var parameter in parameters.GetAllNotNull())
            {
                var name = (parameter.Name ?? "").Trim().TrimStart('$');
                if (name.Length == 0)
                {
                    continue;
                }

                list.Add(new StubAuditParameter(name, TyhpdefApiIndex.TypeText(parameter.Type)));
            }

            return list;
        }

        private static TyhpGenericsTypeArgumentListAst? ExtractGenericList(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias is null)
            {
                return null;
            }

            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericParameters", out var addon)
                && addon is TyhpGenericsTypeArgumentListAst addonList)
            {
                return addonList;
            }

            if (nameOrAlias is TyhpGenericIdentifierAst genericId
                && genericId.GenericArguments is TyhpGenericsTypeArgumentListAst childList)
            {
                return childList;
            }

            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericArguments", out var args)
                && args is TyhpGenericsTypeArgumentListAst functionList)
            {
                return functionList;
            }

            return null;
        }

        private static string SpellGenericList(TyhpGenericsTypeArgumentListAst? list)
        {
            if (list is null)
            {
                return "";
            }

            var parts = new List<string>();
            foreach (var arg in list.GetAllNotNull())
            {
                var name = arg.Name?.ValueString ?? arg.Identifier ?? "";
                if (name.Length == 0)
                {
                    continue;
                }

                var text = name;
                var constraint = TyhpdefApiIndex.TypeText(arg.TypeConstraint as IBase2Ast);
                if (constraint.Length > 0)
                {
                    text += " extends " + constraint;
                }

                var defaultType = TyhpdefApiIndex.TypeText(arg.DefaultType as IBase2Ast);
                if (defaultType.Length > 0)
                {
                    text += " = " + defaultType;
                }

                parts.Add(text);
            }

            return parts.Count == 0 ? "" : "<" + string.Join(", ", parts) + ">";
        }

        private static string SpellTypeList(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            if (node is PhpClassNameListAst list)
            {
                var parts = list.GetAllNotNull()
                    .Select(item => TyhpdefApiIndex.TypeText(item))
                    .Where(t => t.Length > 0)
                    .ToList();
                return string.Join(", ", parts);
            }

            return TyhpdefApiIndex.TypeText(node);
        }

        private static string KindFromDecl(TokenValueAst? declType)
        {
            var text = (declType?.ValueString ?? "class").Trim().ToLowerInvariant();
            return text switch
            {
                "interface" => "interface",
                "trait" => "trait",
                "enum" => "enum",
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

    internal sealed class StubAuditIndex
    {
        public Dictionary<string, StubAuditType> Types { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, StubAuditCallable> Functions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class StubAuditType
    {
        public string Fqn { get; init; } = "";

        public string Kind { get; init; } = "class";

        public string Generics { get; set; } = "";

        public string Extends { get; set; } = "";

        public string Implements { get; set; } = "";

        public Dictionary<string, StubAuditCallable> Methods { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class StubAuditCallable
    {
        public string Fqn { get; init; } = "";

        public string Name { get; init; } = "";

        public string Generics { get; set; } = "";

        public string ReturnType { get; set; } = "";

        public List<StubAuditParameter> Parameters { get; set; } = [];
    }

    internal sealed record StubAuditParameter(string Name, string Type);
}
