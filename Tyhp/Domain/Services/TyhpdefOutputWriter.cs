using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Serializes <see cref="TyhpdefFile"/> IR into valid <c>.tyhpdef</c> text.
    /// Splitting by namespace/type is the caller's job; this writer formats one file
    /// (or a sequence of files) at a time. Declaration decorations follow PER Coding
    /// Style 3.0 §12.2: docblock, then attributes, then the structure.
    /// </summary>
    public static class TyhpdefOutputWriter
    {
        public static string Write(TyhpdefFile file)
            => Write(file, TyhpdefOutputOptions.Default);

        public static string Write(TyhpdefFile file, bool includeDocComments)
            => Write(file, new TyhpdefOutputOptions { IncludeDocComments = includeDocComments });

        public static string Write(TyhpdefFile file, TyhpdefOutputOptions options)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(options);
            return new Emitter(options).EmitFile(file);
        }

        /// <summary>
        /// Formats each file independently. Used when the caller has already split IR
        /// (<c>--split=namespace</c> / <c>--split=type</c>).
        /// </summary>
        public static IReadOnlyList<string> Write(IEnumerable<TyhpdefFile> files, bool includeDocComments = true)
        {
            ArgumentNullException.ThrowIfNull(files);
            var options = new TyhpdefOutputOptions { IncludeDocComments = includeDocComments };
            return files.Select(file => Write(file, options)).ToList();
        }

        public static IReadOnlyList<string> Write(IEnumerable<TyhpdefFile> files, TyhpdefOutputOptions options)
        {
            ArgumentNullException.ThrowIfNull(files);
            ArgumentNullException.ThrowIfNull(options);
            return files.Select(file => Write(file, options)).ToList();
        }

        private sealed class Emitter
        {
            private readonly StringBuilder _sb = new();
            private readonly bool _includeDocs;
            private string _currentNamespace = "";
            private int _indent;
            private bool _atLineStart = true;

            public Emitter(TyhpdefOutputOptions options)
            {
                this._includeDocs = options.IncludeDocComments;
            }

            public string EmitFile(TyhpdefFile file)
            {
                this.AppendLine("<?tyhpdef");
                this.WriteRawAttributeLines(file.FileAttributes);
                this.WriteHeader(file.Header);

                foreach (var globalUse in file.GlobalUses ?? [])
                {
                    var text = globalUse?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        this.AppendLine(text);
                    }
                }

                this.WriteDeclarationLists(
                    file.GlobalConstants,
                    file.GlobalFunctions,
                    file.TypeAliases,
                    file.GlobalTypes);

                foreach (var ns in file.Namespaces ?? [])
                {
                    this.WriteNamespace(ns);
                }

                foreach (var block in file.DeclareBlocks ?? [])
                {
                    this.WriteDeclareBlock(block);
                }

                return Cleanup(this._sb.ToString());
            }

            private void WriteHeader(string header)
            {
                var trimmed = header?.Trim() ?? "";
                if (trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    foreach (var line in SplitLines(trimmed))
                    {
                        this.AppendLine(line.TrimEnd());
                    }

                    return;
                }

                this.AppendLine("/**");
                this.AppendLine(" * AUTO-GENERATED, DO NOT EDIT");
                if (trimmed.Length > 0)
                {
                    foreach (var line in SplitLines(trimmed))
                    {
                        this.AppendLine(" * " + line.TrimEnd());
                    }
                }

                this.AppendLine(" */");
            }

            private void WriteDeclareBlock(TyhpdefDeclareBlock block)
            {
                var constraint = (block.Constraint ?? "").Trim();
                if (constraint.Length == 0)
                {
                    this.WriteDeclarationLists(block.Constants, block.Functions, block.TypeAliases, block.Classes);
                    foreach (var ns in block.Namespaces ?? [])
                    {
                        this.WriteNamespace(ns);
                    }

                    return;
                }

                if (!constraint.StartsWith("declare", StringComparison.OrdinalIgnoreCase))
                {
                    // Bare ranges like `>=8.4` contain `=` in the operator. An explicit
                    // `php=` or `ext=` key is already fully formed.
                    constraint = ConstraintHasExplicitKey(constraint)
                        ? "declare(" + constraint + ")"
                        : "declare(php=\"" + constraint + "\")";
                }

                this.AppendLine(constraint + " {");
                this._indent++;
                this.WriteDeclarationLists(block.Constants, block.Functions, block.TypeAliases, block.Classes);
                foreach (var ns in block.Namespaces ?? [])
                {
                    this.WriteNamespace(ns);
                }

                foreach (var child in block.Children ?? [])
                {
                    this.WriteDeclareBlock(child);
                }

                this._indent--;
                this.AppendLine("}");
            }

            /// <summary>
            /// True when <paramref name="constraint"/> already has a <c>php=</c> key
            /// (<c>php="&gt;=8.4"</c>). Bare Composer ranges such as <c>&gt;=8.4</c>
            /// contain <c>=</c> in the operator and must still be wrapped as
            /// <c>declare(php="…")</c>.
            /// </summary>
            private static bool ConstraintHasExplicitKey(string constraint)
            {
                var trimmed = constraint.TrimStart();
                return KeyEquals(trimmed, "php") || KeyEquals(trimmed, "ext");

                static bool KeyEquals(string text, string key)
                {
                    if (!text.StartsWith(key, StringComparison.OrdinalIgnoreCase) || text.Length <= key.Length)
                    {
                        return false;
                    }

                    return text[key.Length..].TrimStart().StartsWith('=');
                }
            }

            private void WriteNamespace(TyhpdefNamespace ns)
            {
                var name = ns.Name?.Trim() ?? "";
                if (name.Length == 0)
                {
                    this._currentNamespace = "";
                    this.WriteDeclarationLists(ns.Constants, ns.Functions, ns.TypeAliases, ns.Classes);
                    return;
                }

                this._currentNamespace = name.TrimStart('\\');
                this.AppendLine("namespace " + this._currentNamespace + " {");
                this._indent++;
                this.WriteDeclarationLists(ns.Constants, ns.Functions, ns.TypeAliases, ns.Classes);
                this._indent--;
                this.AppendLine("}");
                this._currentNamespace = "";
            }

            private void WriteDeclarationLists(
                IEnumerable<TyhpdefConstant>? constants,
                IEnumerable<TyhpdefFunction>? functions,
                IEnumerable<TyhpdefTypeAlias>? aliases,
                IEnumerable<TyhpdefClassDeclaration>? types)
            {
                foreach (var constant in constants ?? [])
                {
                    this.WriteConstant(constant);
                }

                foreach (var function in functions ?? [])
                {
                    this.WriteFunction(function);
                }

                foreach (var alias in aliases ?? [])
                {
                    this.WriteTypeAlias(alias);
                }

                foreach (var type in types ?? [])
                {
                    this.WriteType(type);
                }
            }

            private void WriteConstant(TyhpdefConstant constant, bool isClassMember = false)
            {
                if (!isClassMember && constant.IsExtern)
                {
                    this.WriteProvidedBy(constant.ProvidedBy);
                    this.AppendLine("extern const " + FormatConstName(constant.Name, isClassMember: false) + ";");
                    return;
                }

                this.WriteDeclarationDecorations(
                    constant.DocComment,
                    isClassMember ? constant.PhpGate : null,
                    constant.Attributes);
                var sb = new StringBuilder();
                AppendDeprecated(sb, constant.IsDeprecated, obsolete: false);
                if (!isClassMember && constant.IsFallback)
                {
                    sb.Append("fallback ");
                }

                if (isClassMember)
                {
                    // tyhpdefImportConstStatement (global const) has no Modifiers; only
                    // tyhpdefImportClassConst (class body) accepts methodModifiers.
                    AppendModifiers(sb, constant.Modifiers);
                }

                sb.Append("const ");
                var type = FormatType(constant.Type);
                if (type.Length == 0)
                {
                    type = "mixed";
                }

                sb.Append(type);
                sb.Append(' ');
                sb.Append(FormatConstName(constant.Name, isClassMember));
                if (!string.IsNullOrEmpty(constant.Value))
                {
                    // tyhpdefImportConstStatement / tyhpdefImportClassConstDecl accept `?? expr`, not `=`.
                    sb.Append(" ?? ");
                    sb.Append(constant.Value);
                }

                sb.Append(';');
                this.AppendLine(sb.ToString());
            }

            private void WriteFunction(TyhpdefFunction function)
            {
                if (function.IsExtern)
                {
                    this.WriteProvidedBy(function.ProvidedBy);
                    this.AppendLine("extern function " + FormatDeclaredTypeName(function.Name, this._currentNamespace) + ";");
                    return;
                }

                this.WriteCallable(function, isExtensionFunction: function.IsExtension, isOperator: false, isTopLevelFunction: true);
                foreach (var overload in function.Overloads ?? [])
                {
                    var asFunction = overload as TyhpdefFunction ?? new TyhpdefFunction
                    {
                        Name = overload.Name,
                        Modifiers = overload.Modifiers,
                        Attributes = overload.Attributes,
                        Parameters = overload.Parameters,
                        ReturnType = overload.ReturnType,
                        GenericParameters = overload.GenericParameters,
                        DocComment = overload.DocComment,
                        IsDeprecated = overload.IsDeprecated,
                        IsObsolete = overload.IsObsolete,
                        ReturnsReference = overload.ReturnsReference,
                        IsAsync = overload.IsAsync,
                        PhpGate = overload.PhpGate,
                        IsExtension = function.IsExtension,
                    };
                    this.WriteCallable(asFunction, isExtensionFunction: asFunction.IsExtension, isOperator: false, isTopLevelFunction: true);
                }
            }

            private void WriteMethod(TyhpdefMethod method, bool isOperator)
            {
                this.WriteCallable(method, isExtensionFunction: false, isOperator: isOperator, isTopLevelFunction: false);
                foreach (var overload in method.Overloads ?? [])
                {
                    this.WriteCallable(overload, isExtensionFunction: false, isOperator: isOperator, isTopLevelFunction: false);
                }
            }

            /// <summary>
            /// Emits a global function, class method, or class operator. PHP gates and attributes
            /// are legal on top-level functions and on tyhpdef class methods/constants/properties
            /// (<c>#[\Tyhp\Php]</c>). Class operators still have no attribute slot.
            /// Decorations follow PER Coding Style 3.0 §12.2: docblock, then attributes, then
            /// the declaration.
            /// </summary>
            private void WriteCallable(TyhpdefMethod method, bool isExtensionFunction, bool isOperator, bool isTopLevelFunction)
            {
                if (isOperator)
                {
                    this.WriteDoc(method.DocComment);
                }
                else
                {
                    this.WriteDeclarationDecorations(method.DocComment, method.PhpGate, method.Attributes);
                }

                var sb = new StringBuilder();
                AppendDeprecated(sb, method.IsDeprecated, method.IsObsolete);

                if (isTopLevelFunction && method is TyhpdefFunction { IsFallback: true })
                {
                    sb.Append("fallback ");
                }

                if (!isOperator)
                {
                    if (method.IsAsync)
                    {
                        sb.Append("async ");
                    }

                    if (!isTopLevelFunction)
                    {
                        AppendModifiers(sb, method.Modifiers);
                    }
                }

                if (isOperator)
                {
                    sb.Append("operator ");
                    sb.Append(method.Name);
                }
                else
                {
                    sb.Append("function ");
                    if (method.ReturnsReference)
                    {
                        sb.Append('&');
                    }

                    sb.Append(method.Name);
                    sb.Append(FormatGenerics(method.GenericParameters));
                }

                sb.Append('(');
                if (isExtensionFunction)
                {
                    sb.Append("extends ");
                }

                sb.Append(FormatParameters(method.Parameters));
                sb.Append(')');
                AppendReturnType(sb, method.ReturnType, method.Name);
                sb.Append(';');
                this.AppendLine(sb.ToString());
            }

            private void WriteTypeAlias(TyhpdefTypeAlias alias)
            {
                this.WriteDeclarationDecorations(alias.DocComment, phpGate: null, alias.Attributes);
                if (alias.HasObjectShape)
                {
                    this.WriteObjectShapeAlias(alias);
                    return;
                }

                var sb = new StringBuilder();
                AppendModifiers(sb, alias.Modifiers);
                sb.Append("type ");
                sb.Append(alias.Name);
                sb.Append(FormatGenerics(alias.GenericParameters));
                sb.Append(" = ");
                sb.Append(FormatType(alias.AliasedType));
                sb.Append(';');
                this.AppendLine(sb.ToString());
            }

            private void WriteObjectShapeAlias(TyhpdefTypeAlias alias)
            {
                var header = new StringBuilder();
                AppendModifiers(header, alias.Modifiers);
                header.Append("type ");
                header.Append(alias.Name);
                header.Append(FormatGenerics(alias.GenericParameters));
                header.Append(" = ");
                var parents = (alias.IntersectionTypes ?? [])
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(FormatType)
                    .ToList();
                if (parents.Count > 0)
                {
                    header.Append(string.Join(" & ", parents));
                    header.Append(" & ");
                }

                header.Append("object {");
                this.AppendLine(header.ToString());
                this._indent++;
                var shape = alias.ObjectShape!;
                foreach (var constant in shape.Constants ?? [])
                {
                    this.WriteConstant(constant, isClassMember: true);
                }

                foreach (var property in shape.Properties ?? [])
                {
                    this.WriteProperty(property, isStruct: false);
                }

                foreach (var method in shape.Methods ?? [])
                {
                    this.WriteMethod(method, isOperator: false);
                }

                this._indent--;
                this.AppendLine("};");
            }

            private void WriteType(TyhpdefClassDeclaration type)
            {
                var kind = (type.Kind ?? "class").Trim().ToLowerInvariant();
                if (kind == "extension")
                {
                    this.WriteStandaloneExtension(type);
                    return;
                }

                if (kind == "struct")
                {
                    this.WriteStruct(type);
                    return;
                }

                if (type.IsExtern)
                {
                    this.WriteExtern(type, kind);
                    return;
                }

                this.WriteDeclarationDecorations(type.DocComment, type.PhpGate, type.Attributes);

                var header = this.FormatTypeHeader(type, kind);
                if (type.IsHeaderOnly)
                {
                    this.AppendLine(header + ";");
                    return;
                }

                this.AppendLine(header + " {");
                this._indent++;
                this.WriteTypeBody(type, kind);
                this._indent--;
                this.AppendLine("}");
            }

            private void WriteProvidedBy(string? providedBy)
            {
                var trimmed = (providedBy ?? "").Trim();
                if (trimmed.Length > 0)
                {
                    this.AppendLine("// @provided-by: " + trimmed);
                }
            }

            private void WriteExtern(TyhpdefClassDeclaration type, string kind)
            {
                this.WriteProvidedBy(type.ProvidedBy);

                var name = FormatDeclaredTypeName(type.Name, this._currentNamespace);
                if (kind.Length == 0
                    || string.Equals(kind, "extern", StringComparison.OrdinalIgnoreCase))
                {
                    this.AppendLine("extern " + name + ";");
                    return;
                }

                this.AppendLine("extern " + kind + " " + name + ";");
            }

            private string FormatTypeHeader(TyhpdefClassDeclaration type, string kind)
            {
                var sb = new StringBuilder();
                AppendDeprecated(sb, type.IsDeprecated, type.IsObsolete);
                if (kind == "class")
                {
                    // Only tyhpdefImportClassDeclarationStatement has a Modifiers slot;
                    // trait/interface/enum declarations do not accept classModifiers.
                    AppendModifiers(sb, type.Modifiers);
                }

                if (type.IsPartial && !ContainsModifier(type.Modifiers, "partial"))
                {
                    sb.Append("partial ");
                }

                sb.Append(kind);
                sb.Append(' ');

                if (kind == "class" && !string.IsNullOrEmpty(type.AsAlias))
                {
                    // `class PhpName as TyhpName`: PhpName binds to `AliasOf=className` (FQN-capable),
                    // TyhpName binds to a bare `Identifier=T_STRING` (never namespaced).
                    // Keyword PhpNames (`Isset`, `Is`) need the same FQCN prefix as the
                    // non-alias branch; FormatType only backslash-qualifies names that
                    // already contain `\`.
                    sb.Append(FormatDeclaredTypeName(type.Name, this._currentNamespace));
                    sb.Append(" as ");
                    sb.Append(type.AsAlias);
                }
                else
                {
                    sb.Append(FormatDeclaredTypeName(type.Name, this._currentNamespace));
                }

                sb.Append(FormatGenerics(type.GenericParameters));

                if (kind == "enum" && !string.IsNullOrEmpty(type.BackingType))
                {
                    sb.Append(": ");
                    sb.Append(FormatType(type.BackingType));
                }

                if (kind == "interface")
                {
                    var parents = new List<string>();
                    if (!string.IsNullOrWhiteSpace(type.Extends))
                    {
                        parents.Add(FormatType(type.Extends!));
                    }

                    foreach (var item in type.Implements ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            parents.Add(FormatType(item));
                        }
                    }

                    if (parents.Count > 0)
                    {
                        sb.Append(" extends ");
                        sb.Append(string.Join(", ", parents));
                    }
                }
                else if (kind != "trait")
                {
                    // tyhpdefImportTraitDeclarationStatement has neither Extends nor Implements;
                    // class supports both, enum supports Implements only (no Extends).
                    if (!string.IsNullOrWhiteSpace(type.Extends) && kind != "enum")
                    {
                        sb.Append(" extends ");
                        sb.Append(FormatType(type.Extends!));
                    }

                    var implements = (type.Implements ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(FormatType).ToList();
                    if (implements.Count > 0)
                    {
                        sb.Append(" implements ");
                        sb.Append(string.Join(", ", implements));
                    }
                }

                return sb.ToString();
            }

            private void WriteTypeBody(TyhpdefClassDeclaration type, string kind)
            {
                var uses = (type.Uses ?? []).Where(u => !string.IsNullOrWhiteSpace(u)).Select(FormatType).ToList();
                if (uses.Count > 0)
                {
                    if (uses.Exists(u => u.Contains('{', StringComparison.Ordinal)))
                    {
                        foreach (var used in uses)
                        {
                            this.AppendLine(
                                used.Contains('{', StringComparison.Ordinal)
                                    ? "use " + used
                                    : "use " + used + ";");
                        }
                    }
                    else
                    {
                        this.AppendLine("use " + string.Join(", ", uses) + ";");
                    }
                }

                foreach (var constant in type.Constants ?? [])
                {
                    this.WriteConstant(constant, isClassMember: true);
                }

                foreach (var alias in type.TypeAliases ?? [])
                {
                    this.WriteTypeAlias(alias);
                }

                if (kind == "enum")
                {
                    foreach (var enumCase in type.EnumCases ?? [])
                    {
                        this.WriteEnumCase(enumCase);
                    }
                }

                foreach (var property in type.Properties ?? [])
                {
                    this.WriteProperty(property, isStruct: false);
                }

                foreach (var method in type.Methods ?? [])
                {
                    this.WriteMethod(method, isOperator: false);
                }

                foreach (var op in type.Operators ?? [])
                {
                    this.WriteMethod(op, isOperator: true);
                }

                foreach (var member in type.ExtensionMembers ?? [])
                {
                    this.WriteClassBodyExtensionMember(member);
                }
            }

            private void WriteStruct(TyhpdefClassDeclaration type)
            {
                this.WriteDoc(type.DocComment);
                var sb = new StringBuilder();
                AppendDeprecated(sb, type.IsDeprecated, type.IsObsolete);

                sb.Append("type ");
                sb.Append(type.Name);
                sb.Append(FormatGenerics(type.GenericParameters));
                sb.Append(" = struct");
                if (!string.IsNullOrWhiteSpace(type.Extends))
                {
                    sb.Append(" extends ");
                    sb.Append(FormatType(type.Extends!));
                }

                this.AppendLine(sb + " {");
                this._indent++;
                foreach (var property in type.Properties ?? [])
                {
                    this.WriteProperty(property, isStruct: true);
                }

                this._indent--;
                this.AppendLine("};");
            }

            private void WriteStandaloneExtension(TyhpdefClassDeclaration type)
            {
                this.WriteDoc(type.DocComment);
                var sb = new StringBuilder();
                AppendDeprecated(sb, type.IsDeprecated, type.IsObsolete);
                sb.Append("extension ");
                sb.Append(type.Name);
                sb.Append(FormatGenerics(type.GenericParameters));
                if (!string.IsNullOrWhiteSpace(type.Extends))
                {
                    sb.Append(" extends ");
                    sb.Append(FormatType(type.Extends));
                }

                this.AppendLine(sb + " {");
                this._indent++;
                foreach (var member in type.ExtensionMembers ?? [])
                {
                    this.WriteStandaloneExtensionMember(member);
                }

                foreach (var group in type.ExtensionGroups ?? [])
                {
                    this.WriteExtensionGroup(group);
                }

                this._indent--;
                this.AppendLine("}");
            }

            private void WriteExtensionGroup(TyhpdefExtensionGroup group)
            {
                var sb = new StringBuilder();
                sb.Append("extends");
                sb.Append(FormatGenerics(group.GenericParameters));
                var target = FormatType(group.TargetType);
                if (target.Length > 0)
                {
                    sb.Append(' ');
                    sb.Append(target);
                }

                this.AppendLine(sb + " {");
                this._indent++;
                foreach (var member in group.Members ?? [])
                {
                    this.WriteStandaloneExtensionMember(member);
                }

                this._indent--;
                this.AppendLine("}");
            }

            private void WriteClassBodyExtensionMember(TyhpdefExtensionMember member)
            {
                this.WriteDeclarationDecorations(member.DocComment, phpGate: null, member.Attributes);
                var kind = (member.Kind ?? "function").Trim().ToLowerInvariant();
                var sb = new StringBuilder();
                AppendDeprecated(sb, member.IsDeprecated, member.IsObsolete);
                sb.Append("extension ");
                if (kind == "operator")
                {
                    sb.Append("operator ");
                    sb.Append(member.Name);
                    sb.Append(FormatOperatorTarget(member.OperatorTarget));
                    sb.Append(FormatGenerics(member.GenericParameters));
                    sb.Append('(');
                    sb.Append(FormatParameters(member.Parameters));
                    sb.Append(')');
                    AppendReturnType(sb, member.ReturnType, member.Name);
                    this.AppendLine(sb + FormatArrowBody(member.Body) + ";");
                    return;
                }

                sb.Append("fn ");
                sb.Append(member.Name);
                sb.Append(FormatGenerics(member.GenericParameters));
                sb.Append('(');
                sb.Append(FormatParameters(member.Parameters));
                sb.Append(')');
                AppendReturnType(sb, member.ReturnType, member.Name);
                this.AppendLine(sb + FormatArrowBody(member.Body) + ";");
            }

            private void WriteStandaloneExtensionMember(TyhpdefExtensionMember member)
            {
                // tyhpdefStandaloneExtensionMember only has two alternatives: `fn ... => ...;`
                // and `operator ... => ...;` — `function … =>` is illegal here
                // (short members use `fn`, matching live Tyhp), so every non-operator member must emit `fn`, not
                // `function`.
                this.WriteDeclarationDecorations(member.DocComment, phpGate: null, member.Attributes);
                var kind = (member.Kind ?? "function").Trim().ToLowerInvariant();
                var sb = new StringBuilder();
                AppendDeprecated(sb, member.IsDeprecated, member.IsObsolete);
                if (kind == "operator")
                {
                    sb.Append("operator ");
                    sb.Append(member.Name);
                    sb.Append(FormatGenerics(member.GenericParameters));
                }
                else
                {
                    sb.Append("fn ");
                    sb.Append(member.Name);
                    sb.Append(FormatGenerics(member.GenericParameters));
                }

                sb.Append('(');
                var parameters = FormatParameters(member.Parameters);
                if (member.ByRefReceiver && kind != "operator")
                {
                    sb.Append("&$this");
                    if (parameters.Length > 0)
                    {
                        sb.Append(", ");
                        sb.Append(parameters);
                    }
                }
                else
                {
                    sb.Append(parameters);
                }

                sb.Append(')');
                AppendReturnType(sb, member.ReturnType, member.Name);
                this.AppendLine(sb + FormatArrowBody(member.Body) + ";");
            }

            private void WriteProperty(TyhpdefProperty property, bool isStruct)
            {
                if (isStruct)
                {
                    this.WriteDoc(property.DocComment);
                }
                else
                {
                    this.WriteDeclarationDecorations(property.DocComment, property.PhpGate, property.Attributes);
                }
                var sb = new StringBuilder();
                if (!isStruct)
                {
                    // tyhpStructProperty has no tyhpdefDeprecatedOrObsolete prefix.
                    AppendDeprecated(sb, property.IsDeprecated, obsolete: false);

                    // tyhpdefClassProperty requires propertyModifiers to be non-empty (at least
                    // one of public/protected/private/var) — an empty Modifiers list would emit
                    // an unparsable bare `Type $name;`. Reflection-based codegen should always
                    // know visibility, but default defensively to "public" (PHP's default
                    // visibility) since the IR field is just a List<string> that can be empty.
                    var modifiers = property.Modifiers.Count > 0 ? property.Modifiers : ["public"];
                    AppendModifiers(sb, modifiers);
                }

                var type = FormatType(property.Type);
                if (type.Length > 0)
                {
                    sb.Append(type);
                    sb.Append(' ');
                }

                sb.Append(FormatPropertyName(property.Name, isStruct));

                // Tyhpdef properties use `?? expr` for an expected PHP start value, matching
                // tyhpdef consts. Structs keep real `=` defaults on tyhpStructProperty; do
                // not emit `??` there.
                if (!isStruct && !string.IsNullOrEmpty(property.CoalesceValue))
                {
                    sb.Append(" ?? ");
                    sb.Append(property.CoalesceValue);
                }

                // Structs have no hook grammar (tyhpStructProperty). Empty Hooks stays
                // `Type $name;` so unhooked class/interface/trait properties match today.
                List<TyhpdefPropertyHook> hooks = [];
                if (!isStruct)
                {
                    hooks = (property.Hooks ?? [])
                        .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                        .ToList();
                }

                if (hooks.Count > 0)
                {
                    sb.Append(" { ");
                    sb.Append(string.Join(" ", hooks.Select(FormatPropertyHook)));
                    sb.Append(" }");
                }
                else
                {
                    sb.Append(';');
                }

                this.AppendLine(sb.ToString());
            }

            private void WriteEnumCase(TyhpdefEnumCase enumCase)
            {
                this.WriteDeclarationDecorations(enumCase.DocComment, enumCase.PhpGate, enumCase.Attributes);
                var line = "case " + enumCase.Name;
                if (!string.IsNullOrEmpty(enumCase.BackingValue))
                {
                    line += " = " + enumCase.BackingValue;
                }

                this.AppendLine(line + ";");
            }

            /// <summary>
            /// Declaration decorations in PER Coding Style 3.0 §12.2 order: docblock, then
            /// attributes (including <c>#[\Tyhp\Php]</c>), then the caller emits the structure.
            /// Overlay <c>// @overlay-against:</c> stamps are inserted separately immediately
            /// before the declaration keyword.
            /// </summary>
            private void WriteDeclarationDecorations(
                string? docComment,
                string? phpGate = null,
                IEnumerable<TyhpdefAttribute>? attributes = null)
            {
                this.WriteDoc(docComment);
                this.WritePhpGate(phpGate);
                this.WriteAttributes(attributes);
            }

            private void WriteDoc(string? comment)
            {
                if (!this._includeDocs || string.IsNullOrWhiteSpace(comment))
                {
                    return;
                }

                foreach (var line in NormalizeDocComment(comment))
                {
                    this.AppendLine(line);
                }
            }

            private void WriteAttributes(IEnumerable<TyhpdefAttribute>? attributes)
            {
                foreach (var attribute in attributes ?? [])
                {
                    this.AppendLine(FormatAttribute(attribute));
                }
            }

            private void WritePhpGate(string? phpGate)
            {
                var gate = phpGate?.Trim();
                if (string.IsNullOrEmpty(gate))
                {
                    return;
                }

                if (gate.StartsWith("#[", StringComparison.Ordinal))
                {
                    this.AppendLine(gate);
                    return;
                }

                var argument = gate.Contains('"', StringComparison.Ordinal) ? gate : "\"" + gate + "\"";
                this.AppendLine("#[\\Tyhp\\Php(" + argument + ")]");
            }

            private void WriteRawAttributeLines(IEnumerable<string>? attributes)
            {
                foreach (var attribute in attributes ?? [])
                {
                    var text = attribute?.Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    this.AppendLine(text.StartsWith("#[", StringComparison.Ordinal) ? text : "#[" + text + "]");
                }
            }

            private void AppendLine(string text)
            {
                if (!this._atLineStart)
                {
                    this._sb.Append('\n');
                }

                if (text.Length > 0 && this._indent > 0)
                {
                    this._sb.Append(' ', this._indent * 4);
                }

                this._sb.Append(text);
                this._sb.Append('\n');
                this._atLineStart = true;
            }
        }

        private static string FormatParameters(IEnumerable<TyhpdefParameter>? parameters)
        {
            if (parameters == null)
            {
                return "";
            }

            return string.Join(", ", parameters.Select(FormatParameter));
        }

        private static string FormatParameter(TyhpdefParameter parameter)
        {
            var sb = new StringBuilder();
            foreach (var attribute in parameter.Attributes ?? [])
            {
                sb.Append(FormatAttribute(attribute));
                sb.Append(' ');
            }

            var type = FormatType(parameter.Type);
            if (type.Length == 0 || !PhpAstTypeExtractor.IsBalanced(type))
            {
                type = "mixed";
            }

            sb.Append(type);
            sb.Append(' ');

            if (parameter.IsByReference)
            {
                sb.Append('&');
            }

            if (parameter.IsVariadic)
            {
                sb.Append("...");
            }

            sb.Append(FormatVariableName(parameter.Name));
            if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                var defaultValue = PhpAstTypeExtractor.SanitizeDefaultValue(parameter.DefaultValue);
                sb.Append(" = ");
                sb.Append(string.IsNullOrEmpty(defaultValue) ? "null" : defaultValue);
            }

            return sb.ToString();
        }

        private static string FormatGenerics(IEnumerable<TyhpdefGenericParameter>? parameters)
        {
            var list = (parameters ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (list.Count == 0)
            {
                return "";
            }

            var parts = list.Select(p =>
            {
                var text = p.Name;
                if (!string.IsNullOrWhiteSpace(p.Constraint))
                {
                    text += " extends " + FormatType(p.Constraint!);
                }

                if (!string.IsNullOrWhiteSpace(p.Default))
                {
                    text += " = " + FormatType(p.Default!);
                }

                return text;
            });
            return "<" + string.Join(", ", parts) + ">";
        }

        private static string FormatOperatorTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return "";
            }

            var trimmed = target.Trim();
            if (trimmed.StartsWith('<'))
            {
                return trimmed;
            }

            return "<" + FormatType(trimmed) + ">";
        }

        private static string FormatPropertyHook(TyhpdefPropertyHook hook)
        {
            var sb = new StringBuilder();
            foreach (var attribute in hook.Attributes ?? [])
            {
                sb.Append(FormatAttribute(attribute));
                sb.Append(' ');
            }

            AppendModifiers(sb, hook.Modifiers);
            if (hook.ReturnsRef)
            {
                sb.Append('&');
            }

            sb.Append(hook.Name.Trim());
            sb.Append(';');
            return sb.ToString();
        }

        private static string FormatAttribute(TyhpdefAttribute attribute)
        {
            var name = attribute.Name?.Trim() ?? "";
            if (name.StartsWith("#[", StringComparison.Ordinal))
            {
                return name;
            }

            var args = attribute.Arguments ?? [];
            if (args.Count == 0)
            {
                return "#[" + name + "]";
            }

            var formatted = "#[" + name + "(" + string.Join(", ", args) + ")]";
            return PhpAstTypeExtractor.IsBalanced(formatted) ? formatted : "#[" + name + "]";
        }

        private static void AppendDeprecated(StringBuilder sb, bool isDeprecated, bool obsolete)
        {
            if (obsolete)
            {
                sb.Append("obsolete ");
                return;
            }

            if (isDeprecated)
            {
                sb.Append("deprecated ");
            }
        }

        private static void AppendModifiers(StringBuilder sb, IEnumerable<string>? modifiers)
        {
            foreach (var modifier in modifiers ?? [])
            {
                if (string.IsNullOrWhiteSpace(modifier))
                {
                    continue;
                }

                sb.Append(modifier.Trim());
                sb.Append(' ');
            }
        }

        private static void AppendReturnType(StringBuilder sb, string? returnType, string? memberName = null)
        {
            var formatted = FormatType(TyhpdefRequiredTypes.ReturnType(memberName, returnType));
            sb.Append(": ");
            sb.Append(formatted);
        }

        private static bool ContainsModifier(IEnumerable<string>? modifiers, string name)
            => (modifiers ?? []).Any(m => string.Equals(m?.Trim(), name, StringComparison.OrdinalIgnoreCase));

        private static string FormatPropertyName(string name, bool isStruct)
        {
            var trimmed = name?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                return isStruct ? "$value" : "$prop";
            }

            if (trimmed.StartsWith('$')
                || char.IsDigit(trimmed[0])
                || trimmed[0] is '\'' or '"')
            {
                return trimmed;
            }

            if (trimmed.Contains(" as ", StringComparison.Ordinal))
            {
                return trimmed;
            }

            return "$" + trimmed.TrimStart('$');
        }

        private static string FormatVariableName(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return "$value";
            }

            return trimmed.StartsWith('$') ? trimmed : "$" + trimmed;
        }

        private static string FormatArrowBody(string? body)
        {
            var expr = (body ?? "").Trim();
            if (expr.StartsWith("=>", StringComparison.Ordinal))
            {
                expr = expr[2..].Trim();
            }

            if (expr.StartsWith('{'))
            {
                expr = StripBraceReturn(expr);
            }

            expr = expr.TrimEnd().TrimEnd(';').Trim();
            return expr.Length == 0 ? " =>" : " => " + expr;
        }

        private static string StripBraceReturn(string braceBody)
        {
            var inner = braceBody.Trim();
            if (inner.StartsWith('{') && inner.EndsWith('}'))
            {
                inner = inner[1..^1].Trim();
            }

            if (inner.StartsWith("return ", StringComparison.Ordinal))
            {
                inner = inner["return ".Length..].Trim();
            }

            return inner.TrimEnd(';').Trim();
        }

        private static IReadOnlyList<string> NormalizeDocComment(string comment)
        {
            var trimmed = comment.Trim();
            if (!trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                var wrapped = new List<string> { "/**" };
                foreach (var line in SplitLines(trimmed))
                {
                    wrapped.Add(" * " + line.TrimEnd());
                }

                wrapped.Add(" */");
                return wrapped;
            }

            var lines = SplitLines(trimmed);
            var result = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                var content = line.TrimStart();
                result.Add(content.Length == 0 ? " *" : content);
            }

            if (result.Count > 0 && result[0].StartsWith("/*", StringComparison.Ordinal) && !result[0].StartsWith("/**", StringComparison.Ordinal))
            {
                result[0] = "/**" + result[0][2..];
            }

            return result;
        }

        private static List<string> SplitLines(string text)
            => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        /// <summary>
        /// Ensures namespaced types begin with <c>\</c>. Leaves primitives and unqualified names alone.
        /// </summary>
        internal static string FormatType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "";
            }

            return FqnRegex.Replace(type.Trim(), match =>
            {
                var value = match.Value;
                return value.StartsWith('\\') ? value : "\\" + value;
            });
        }

        /// <summary>
        /// Tyhpdef keywords cannot be a bare <c>class Deprecated</c> / <c>class Is</c> /
        /// <c>class Isset</c> name. Prefix a fully-qualified name so the lexer takes
        /// <c>T_NAME_FULLY_QUALIFIED</c> (<c>class \Hamcrest\Core\Is</c>) instead of
        /// <c>T_TYHP_IS</c> / <c>T_ISSET</c> / <c>T_EMPTY</c> / <c>T_LIST</c>.
        /// </summary>
        internal static string FormatDeclaredTypeName(string? name, string? enclosingNamespace = null)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            var bare = trimmed.TrimStart('\\');
            var shortName = bare;
            var slash = bare.LastIndexOf('\\');
            if (slash >= 0)
            {
                shortName = bare[(slash + 1)..];
            }

            if (!IsTyhpdefKeywordTypeName(shortName))
            {
                return trimmed.Contains('\\') ? FormatType(trimmed) : trimmed;
            }

            if (trimmed.Contains('\\'))
            {
                return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed;
            }

            var ns = (enclosingNamespace ?? "").Trim().Trim('\\');
            if (ns.Length > 0)
            {
                return "\\" + ns + "\\" + shortName;
            }

            return "\\" + shortName;
        }

        /// <summary>
        /// Class-constant names that are PHP/tyhpdef tokens (<c>FUNCTION</c>, <c>Default</c>)
        /// must use <c>self::NAME as NAME</c>.
        /// </summary>
        internal static string FormatConstName(string? name, bool isClassMember)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            if (isClassMember && ReservedConstMemberNames.Contains(trimmed)
                && !trimmed.Contains("::", StringComparison.Ordinal)
                && !trimmed.Contains(" as ", StringComparison.OrdinalIgnoreCase))
            {
                return "self::" + trimmed + " as " + trimmed;
            }

            return trimmed;
        }

        /// <summary>
        /// True when a bare class-name slot would not lex as <c>T_STRING</c> /
        /// <c>T_NAME_*</c> in tyhpdef mode. <c>tyhpdefClassNameWithOptionalAlias</c>
        /// binds <c>Identifier=name</c>, so any other token (<c>T_ISSET</c>,
        /// <c>T_TYHP_IS</c>, <c>T_ENUM</c>, …) fails parse unless emitted as FQCN.
        /// Classification probes the real lexer with <c>class NAME as Alias</c> so
        /// the set tracks <c>reservedNonModifiersBase</c>, <c>semiReservedBase</c>,
        /// and tyhpdef-mode keywords instead of a hand-curated incident list.
        /// </summary>
        internal static bool IsTyhpdefKeywordTypeName(string? shortName)
        {
            var name = (shortName ?? "").Trim();
            if (name.Length == 0 || !IsSimpleIdentifier(name))
            {
                return false;
            }

            return KeywordTypeNameCache.GetOrAdd(name, ProbeIsKeywordTypeName);
        }

        private static readonly ConcurrentDictionary<string, bool> KeywordTypeNameCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool IsSimpleIdentifier(string name)
        {
            if (!char.IsAsciiLetter(name[0]) && name[0] != '_')
            {
                return false;
            }

            for (var i = 1; i < name.Length; i++)
            {
                if (!char.IsAsciiLetterOrDigit(name[i]) && name[i] != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ProbeIsKeywordTypeName(string shortName)
        {
            // `as Alias` is the same lookahead as the alias header, so contextual
            // keywords (`enum`, `deprecated`, `struct`, …) fire the keyword token.
            var source = "<?tyhpdef\nclass " + shortName + " as Alias {\n}\n";
            try
            {
                var lexer = new TyhpdefLexer(new AntlrInputStream(source));
                lexer.RemoveErrorListeners();
                var stream = new CommonTokenStream(lexer);
                stream.Fill();

                var seenClass = false;
                foreach (var token in stream.GetTokens())
                {
                    if (token.Type == TyhpdefLexer.Eof)
                    {
                        break;
                    }

                    if (token.Channel != Lexer.DefaultTokenChannel)
                    {
                        continue;
                    }

                    if (!seenClass)
                    {
                        seenClass = token.Type == TyhpdefLexer.T_CLASS;
                        continue;
                    }

                    return token.Type is not TyhpdefLexer.T_STRING
                        and not TyhpdefLexer.T_NAME_FULLY_QUALIFIED
                        and not TyhpdefLexer.T_NAME_QUALIFIED
                        and not TyhpdefLexer.T_NAME_RELATIVE;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Prefixing is parse-safe; failing open would reintroduce TYHP8001.
            }

            return true;
        }

        private static readonly HashSet<string> ReservedConstMemberNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "function",
            "default",
            "class",
            "interface",
            "trait",
            "enum",
            "const",
            "new",
            "clone",
            "catch",
            "throw",
            "match",
            "fn",
            "parent",
            "self",
            "static",
            "mixed",
            "void",
            "never",
        };

        private static readonly Regex FqnRegex = new(
            @"(?<![\w\\$])(?:\\)?(?:[A-Za-z_][A-Za-z0-9_]*\\)+[A-Za-z_][A-Za-z0-9_]*",
            RegexOptions.Compiled);

        private static string Cleanup(string text)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
            }

            normalized = BraceBlankRegex.Replace(normalized, "{\n");
            normalized = BeforeCloseBraceRegex.Replace(normalized, "\n$1}");
            return normalized.TrimEnd() + "\n";
        }

        private static readonly Regex BraceBlankRegex = new(@"\{\n\n+", RegexOptions.Compiled);
        private static readonly Regex BeforeCloseBraceRegex = new(@"\n\n+([ \t]*)\}", RegexOptions.Compiled);
    }
}
