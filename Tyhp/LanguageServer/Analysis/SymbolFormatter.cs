namespace Tyhp.LanguageServer.Analysis
{
    using System.Text;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using Tyhp.TyhpLang.Checker;
    using Tyhp.TyhpLang.Enum;

    /// <summary>
    /// Formats binder symbols as Tyhp source snippets for hover and completion.
    /// </summary>
    public static class SymbolFormatter
    {
        /// <summary>
        /// Markdown hover contents: kind label, fenced Tyhp signature (narrowed type when
        /// the checker has one), optional declared-type note when it differs, optional
        /// container, doc comment, deprecation warning, and <c>@provided-by</c> for
        /// tyhpdef <c>extern</c> types.
        /// </summary>
        public static string FormatHover(BaseSymbol symbol, ICheckedType? inferredType = null)
        {
            ArgumentNullException.ThrowIfNull(symbol);

            var builder = new StringBuilder();
            builder.Append(KindLabel(symbol));
            builder.Append("\n```tyhp\n");
            builder.Append(FormatSignature(symbol, inferredType));
            builder.Append("\n```");

            string? declaredNote = FormatDeclaredTypeNote(symbol, inferredType);
            if (!string.IsNullOrEmpty(declaredNote))
            {
                builder.Append('\n');
                builder.Append(declaredNote);
            }

            string? container = FormatContainingScope(symbol);
            if (!string.IsNullOrEmpty(container))
            {
                builder.Append('\n');
                builder.Append(container);
            }

            string? doc = FormatDocComment(symbol.DocComment);
            if (!string.IsNullOrEmpty(doc))
            {
                builder.Append("\n\n");
                builder.Append(doc);
            }

            if (symbol.IsDeprecated || symbol.IsObsolete)
            {
                builder.Append("\n\n**Deprecated**");
            }

            if (symbol is ObjectDeclarationSymbol { IsExtern: true, ProvidedBy: { Length: > 0 } providedBy })
            {
                builder.Append("\n\n`@provided-by: ");
                builder.Append(providedBy);
                builder.Append('`');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Hover for a cursor position that has a checker-inferred type but no bound symbol
        /// (typically an untyped local).
        /// </summary>
        public static string FormatInferredHover(string? name, ICheckedType inferredType)
        {
            ArgumentNullException.ThrowIfNull(inferredType);
            string displayName = string.IsNullOrEmpty(name) ? "$value" : EnsureVariableName(name);
            var builder = new StringBuilder();
            builder.Append("variable");
            builder.Append("\n```tyhp\n");
            builder.Append(FormatCheckedType(inferredType));
            builder.Append(' ');
            builder.Append(displayName);
            builder.Append("\n```");
            return builder.ToString();
        }

        /// <summary>Plain-text doc comment for completion documentation, or null.</summary>
        public static string? FormatDocumentation(BaseSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            return FormatDocComment(symbol.DocComment);
        }

        /// <summary>Tyhp signature for <paramref name="symbol"/>.</summary>
        public static string FormatSignature(BaseSymbol symbol, ICheckedType? inferredType = null)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            return symbol switch
            {
                FunctionDeclarationSymbol function => FormatFunctionSignature(function),
                BuiltInFunctionSymbol builtIn => FormatBuiltInFunctionSignature(builtIn),
                ObjectMethodSymbol method => FormatMethodSignature(method),
                ObjectPropertySymbol property => FormatPropertySignature(property, inferredType),
                VariableSymbol variable => FormatVariableInfo(variable, inferredType),
                SuperGlobalSymbol super => FormatVariableName(super.Name),
                ObjectDeclarationSymbol obj => FormatClassSignature(obj),
                AnonymousObjectDeclarationSymbol => "class",
                ConstantSymbol constant => FormatConstantSignature(constant),
                ObjectConstantSymbol objectConstant => FormatObjectConstantSignature(objectConstant),
                TypeAliasSymbol alias => FormatTypeAliasSignature(alias),
                ObjectTypeAliasSymbol objectAlias => FormatObjectTypeAliasSignature(objectAlias),
                GenericTypeParameterSymbol typeParam => FormatGenericParameter(typeParam),
                NamespaceSymbol ns => "namespace " + ns.FullyQualifiedName.TrimStart('\\'),
                BuiltInTypeSymbol builtInType => builtInType.Name,
                BuiltInUtilityTypeSymbol utility => utility.FullyQualifiedName.TrimStart('\\'),
                MagicConstantSymbol magic => magic.Name,
                UseIncludeSymbol use => FormatUseInclude(use),
                _ => FormatFallback(symbol),
            };
        }

        /// <summary><c>function name(params): returnType</c></summary>
        public static string FormatFunctionSignature(FunctionDeclarationSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            var builder = new StringBuilder();
            AppendFunctionModifiers(builder, symbol.Visibility, isStatic: false, isAbstract: false, symbol.IsAsync);
            builder.Append("function ");
            builder.Append(symbol.Name);
            AppendGenericParameters(builder, symbol.GenericParameters);
            builder.Append('(');
            builder.Append(FormatParameters(symbol.Parameters));
            builder.Append(')');
            AppendReturnType(builder, symbol.ReturnType);
            return builder.ToString();
        }

        /// <summary><c>visibility [static] [abstract] function name(params): returnType</c></summary>
        public static string FormatMethodSignature(ObjectMethodSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            bool extensionMember = IsExtensionMember(symbol);
            MemberModifier visibility = symbol.Visibility;
            if (extensionMember)
            {
                visibility &= ~MemberModifier.Static;
            }

            var builder = new StringBuilder();
            AppendFunctionModifiers(
                builder,
                visibility,
                symbol.IsStatic && !extensionMember,
                symbol.IsAbstract,
                symbol.IsAsync);
            builder.Append("function ");
            builder.Append(symbol.Name);
            AppendGenericParameters(builder, symbol.GenericParameters);
            builder.Append('(');
            builder.Append(FormatParameters(CallerFacingParameters(symbol)));
            builder.Append(')');
            AppendReturnType(builder, symbol.ReturnType);
            return builder.ToString();
        }

        /// <summary>
        /// Parameters a caller writes. Extension members keep an implied receiver as
        /// <c>Parameters[0]</c> named <c>$this</c>; that parameter is not passed at the
        /// call and is omitted from hover, completion, and signature help.
        /// </summary>
        internal static IReadOnlyList<ParameterInfo> CallerFacingParameters(ObjectMethodSymbol method)
        {
            ArgumentNullException.ThrowIfNull(method);
            IReadOnlyList<ParameterInfo> parameters = method.Parameters;
            if (!HasImplicitExtensionReceiver(method))
            {
                return parameters;
            }

            return parameters.Count <= 1 ? [] : parameters.Skip(1).ToArray();
        }

        /// <summary><c>visibility [readonly] Type $name</c></summary>
        public static string FormatPropertySignature(
            ObjectPropertySymbol symbol,
            ICheckedType? inferredType = null)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            var builder = new StringBuilder();
            AppendVisibility(builder, symbol.Visibility);
            if (symbol.SymbolType == SymbolType.StaticObjectProperty
                || symbol.Visibility.HasFlag(MemberModifier.Static))
            {
                builder.Append("static ");
            }

            if (symbol.Visibility.HasFlag(MemberModifier.Readonly))
            {
                builder.Append("readonly ");
            }

            string type = EffectiveTypeDisplay(FormatType(symbol.DeclaredType), inferredType);
            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            builder.Append(EnsureVariableName(symbol.Name));
            return builder.ToString();
        }

        /// <summary>
        /// <c>Type $name</c>. Prefers the checker-narrowed type at this use when provided.
        /// </summary>
        public static string FormatVariableInfo(VariableSymbol symbol, ICheckedType? inferredType = null)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            string type = EffectiveTypeDisplay(FormatType(symbol.DeclaredType), inferredType);

            var builder = new StringBuilder();
            if (symbol.IsParameter && symbol.IsRef)
            {
                builder.Append('&');
            }

            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            builder.Append(EnsureVariableName(symbol.Name));
            return builder.ToString();
        }

        /// <summary>
        /// <c>[extern] [abstract|final] class Name [extends Parent] [implements I1, I2]</c>
        /// (or interface/trait/enum/struct/extension). A nested <c>extends Type { }</c>
        /// group is not a class at all — it is a compiler-generated symbol
        /// (<see cref="ObjectDeclarationSymbol.IsExtensionTargetGroup"/>) whose
        /// <see cref="BaseSymbol.Name"/> is an internal placeholder
        /// (<c>__TyhpExtTargetN</c>), so it formats as the bare group header instead
        /// (<c>extends[&lt;T&gt;] Target</c>), matching what the group actually looks
        /// like in source.
        /// </summary>
        public static string FormatClassSignature(ObjectDeclarationSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            if (symbol.IsStruct)
            {
                return FormatStructShapeAliasSignature(symbol);
            }

            if (symbol.IsExtensionTargetGroup)
            {
                return FormatExtensionTargetGroupSignature(symbol);
            }

            var builder = new StringBuilder();
            if (symbol.IsExtern)
            {
                builder.Append("extern ");
            }

            if (symbol.Visibility.HasFlag(MemberModifier.Abstract))
            {
                builder.Append("abstract ");
            }

            if (symbol.Visibility.HasFlag(MemberModifier.Final))
            {
                builder.Append("final ");
            }

            var kindWord = ObjectKindKeyword(symbol);
            if (kindWord.Length > 0)
            {
                builder.Append(kindWord);
                builder.Append(' ');
            }

            builder.Append(symbol.Name);
            AppendGenericParameters(builder, symbol.GenericParameters);

            string extends = FormatType(symbol.IsExtension ? symbol.PendingExtensionBlockTarget : symbol.ExtendsType);
            if (!string.IsNullOrEmpty(extends))
            {
                builder.Append(" extends ");
                builder.Append(extends);
            }

            if (symbol.ImplementsTypes.Count > 0)
            {
                var implemented = symbol.ImplementsTypes
                    .Select(FormatType)
                    .Where(static name => !string.IsNullOrEmpty(name))
                    .ToList();
                if (implemented.Count > 0)
                {
                    builder.Append(" implements ");
                    builder.Append(string.Join(", ", implemented));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// <c>extends[&lt;T&gt;] Target</c> for a nested extension target group's own
        /// synthetic symbol. Groups have no user-facing name, kind keyword, or
        /// implements clause — only the group's own generic parameters (declared as
        /// <c>extends&lt;T&gt; Target</c>, not on a name) and its target.
        /// </summary>
        private static string FormatExtensionTargetGroupSignature(ObjectDeclarationSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append("extends");
            AppendGenericParameters(builder, symbol.GenericParameters);

            string target = FormatType(symbol.PendingExtensionBlockTarget);
            if (!string.IsNullOrEmpty(target))
            {
                builder.Append(' ');
                builder.Append(target);
            }

            return builder.ToString();
        }

        /// <summary><c>const Type NAME = value</c></summary>
        public static string FormatConstantSignature(ConstantSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            var builder = new StringBuilder();
            builder.Append("const ");
            string type = FormatType(symbol.DeclaredType);
            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            builder.Append(symbol.Name);
            string? value = FormatExpression(symbol.ValueExpression);
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append(" = ");
                builder.Append(value);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Hover for an inline type expression that is a callable or struct shape rather than
        /// a named alias (the binder records those nodes as the <c>callable</c> / <c>struct</c>
        /// builtin). Named aliases still go through <see cref="FormatHover"/>.
        /// </summary>
        public static string? TryFormatTypeExpressionHover(IBase2Ast node, BaseSymbol? boundSymbol)
        {
            ArgumentNullException.ThrowIfNull(node);

            if (node is TyhpCallableShapeAst callable)
            {
                return FormatKindedTypeHover("callable shape", FormatCallableShape(callable));
            }

            if (node is TyhpStructShapeAst structShape
                && boundSymbol is not ObjectDeclarationSymbol { IsStruct: true })
            {
                return FormatKindedTypeHover("struct shape", FormatStructShape(structShape));
            }

            return null;
        }

        /// <summary>Renders an AST type expression as Tyhp source.</summary>
        public static string FormatType(ITypeExpression? type)
        {
            if (type is null)
            {
                return string.Empty;
            }

            string formatted = type switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst named => FormatNamedType(named),
                PhpTypeExpressionAst expr => FormatTypeExpression(expr),
                TyhpObjectShapeAst shape => FormatObjectShape(shape),
                TyhpCallableShapeAst callable => FormatCallableShape(callable),
                TyhpStructShapeAst structShape => FormatStructShape(structShape),
                _ => FirstNonEmpty(type.Identifier, type.ValueString),
            };

            return AppendUseSiteGenericArguments(formatted, type);
        }

        /// <summary>Renders a checker type for hover when no AST type is available.</summary>
        public static string FormatCheckedType(ICheckedType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            string name = type.DisplayName;
            if (name.StartsWith('\\'))
            {
                return name[1..];
            }

            return name;
        }

        /// <summary>
        /// Checker-narrowed type at this use, falling back to the declared AST type.
        /// </summary>
        private static string EffectiveTypeDisplay(string declaredType, ICheckedType? inferredType)
        {
            if (inferredType is not null)
            {
                string inferred = FormatCheckedType(inferredType);
                if (!string.IsNullOrEmpty(inferred))
                {
                    return inferred;
                }
            }

            return declaredType;
        }

        /// <summary>
        /// Secondary hover line when the fenced type is a narrowing of the declaration.
        /// </summary>
        private static string? FormatDeclaredTypeNote(BaseSymbol symbol, ICheckedType? inferredType)
        {
            if (inferredType is null)
            {
                return null;
            }

            string declared = symbol switch
            {
                VariableSymbol variable => FormatType(variable.DeclaredType),
                ObjectPropertySymbol property => FormatType(property.DeclaredType),
                _ => string.Empty,
            };
            if (string.IsNullOrEmpty(declared))
            {
                return null;
            }

            string inferred = FormatCheckedType(inferredType);
            if (string.IsNullOrEmpty(inferred) || TypeDisplaysMatch(declared, inferred))
            {
                return null;
            }

            return "declared `" + declared + "`";
        }

        private static bool TypeDisplaysMatch(string left, string right)
        {
            return string.Equals(
                NormalizeTypeDisplay(left),
                NormalizeTypeDisplay(right),
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Treat <c>?T</c> and <c>T|null</c> as the same spelling so hover does not
        /// repeat an equivalent declared type.
        /// </summary>
        private static string NormalizeTypeDisplay(string type)
        {
            if (type.StartsWith('?')
                && type.Length > 1
                && !type.Contains('|', StringComparison.Ordinal)
                && !type.Contains('&', StringComparison.Ordinal)
                && type[1] != '(')
            {
                return type[1..] + "|null";
            }

            return type;
        }

        internal static string KindLabel(BaseSymbol symbol)
        {
            return symbol switch
            {
                ObjectDeclarationSymbol obj => FormatExternKindLabel(obj),
                AnonymousObjectDeclarationSymbol => "class",
                FunctionDeclarationSymbol => "function",
                BuiltInFunctionSymbol => "function",
                ObjectMethodSymbol method when method.SymbolType == SymbolType.ObjectConstructor => "constructor",
                ObjectMethodSymbol => "method",
                ObjectPropertySymbol => "property",
                VariableSymbol variable when variable.IsParameter => "parameter",
                VariableSymbol or SuperGlobalSymbol => "variable",
                ObjectConstantSymbol constant when constant.IsEnumCase => "enum case",
                ObjectConstantSymbol or ConstantSymbol => "constant",
                TypeAliasSymbol or ObjectTypeAliasSymbol when ObjectShapeSupport.AliasResolvesToObjectShape(symbol)
                    => "object shape",
                TypeAliasSymbol or ObjectTypeAliasSymbol when ObjectShapeSupport.AliasResolvesToCallableShape(symbol)
                    => "callable shape",
                TypeAliasSymbol or ObjectTypeAliasSymbol => "type",
                GenericTypeParameterSymbol => "type parameter",
                NamespaceSymbol => "namespace",
                BuiltInTypeSymbol or BuiltInUtilityTypeSymbol => "type",
                MagicConstantSymbol => "constant",
                UseIncludeSymbol => "import",
                _ => symbol.SymbolType.ToString(),
            };
        }

        private static string FormatBuiltInFunctionSignature(BuiltInFunctionSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append("function ");
            builder.Append(symbol.Name);
            builder.Append('(');
            builder.Append(FormatParameters(symbol.Parameters));
            builder.Append(')');
            AppendReturnType(builder, symbol.ReturnType);
            return builder.ToString();
        }

        private static string FormatObjectConstantSignature(ObjectConstantSymbol symbol)
        {
            var builder = new StringBuilder();
            if (symbol.IsEnumCase)
            {
                builder.Append("case ");
                builder.Append(symbol.Name);
                string? caseValue = FormatExpression(symbol.ValueExpression);
                if (!string.IsNullOrEmpty(caseValue))
                {
                    builder.Append(" = ");
                    builder.Append(caseValue);
                }

                return builder.ToString();
            }

            AppendVisibility(builder, symbol.Visibility);
            builder.Append("const ");
            string type = FormatType(symbol.DeclaredType);
            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            builder.Append(symbol.Name);
            string? value = FormatExpression(symbol.ValueExpression);
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append(" = ");
                builder.Append(value);
            }

            return builder.ToString();
        }

        private static string FormatTypeAliasSignature(TypeAliasSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append("type ");
            builder.Append(symbol.Name);
            AppendGenericParameters(builder, symbol.GenericParameters);
            builder.Append(" = ");
            string aliased = FormatType(symbol.AliasedType);
            builder.Append(string.IsNullOrEmpty(aliased) ? "mixed" : aliased);
            return builder.ToString();
        }

        private static string FormatObjectTypeAliasSignature(ObjectTypeAliasSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append("type ");
            builder.Append(symbol.Name);
            AppendGenericParameters(builder, symbol.GenericParameters);
            builder.Append(" = ");
            string aliased = FormatType(symbol.AliasedType);
            builder.Append(string.IsNullOrEmpty(aliased) ? "mixed" : aliased);
            return builder.ToString();
        }

        private static string FormatObjectShape(TyhpObjectShapeAst shape)
        {
            var builder = new StringBuilder();
            builder.Append("object {");
            foreach (BaseSymbol member in ObjectShapeLspSupport.EnumerateDeclaredMembers(shape, sourceFile: string.Empty))
            {
                builder.Append(' ');
                builder.Append(FormatSignature(member));
                builder.Append(';');
            }

            builder.Append(" }");
            return builder.ToString();
        }

        private static string FormatKindedTypeHover(string kind, string signature)
        {
            var builder = new StringBuilder();
            builder.Append(kind);
            builder.Append("\n```tyhp\n");
            builder.Append(signature);
            builder.Append("\n```");
            return builder.ToString();
        }

        private static string FormatCallableShape(TyhpCallableShapeAst shape)
        {
            string returnType = FormatType(shape.ReturnType);
            if (string.IsNullOrEmpty(returnType))
            {
                returnType = "mixed";
            }

            if (shape.IsUnknownArity)
            {
                return "callable(...): " + returnType;
            }

            var parameters = new List<string>();
            foreach (TyhpCallableShapeParameterAst parameter in shape.Parameters?.GetAllNotNull() ?? [])
            {
                parameters.Add(FormatCallableShapeParameter(parameter));
            }

            return "callable(" + string.Join(", ", parameters) + "): " + returnType;
        }

        private static string FormatCallableShapeParameter(TyhpCallableShapeParameterAst parameter)
        {
            var builder = new StringBuilder();
            string type = FormatType(parameter.TypeExpression);
            builder.Append(string.IsNullOrEmpty(type) ? "mixed" : type);
            if (parameter.IsRef)
            {
                builder.Append(" &");
            }

            string? name = parameter.SourceParameterName;
            if (!string.IsNullOrEmpty(name) && name[0] != '$')
            {
                name = "$" + name;
            }

            if (parameter.IsVariadic)
            {
                builder.Append(string.IsNullOrEmpty(name) ? " ..." : " ..." + name);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                builder.Append(builder[^1] == '&' ? name : " " + name);
            }

            if (parameter.IsOptional)
            {
                builder.Append(" =");
            }

            return builder.ToString();
        }

        private static string FormatStructShapeAliasSignature(ObjectDeclarationSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append("type ");
            builder.Append(symbol.Name);
            AppendGenericParameters(builder, symbol.GenericParameters);
            builder.Append(" = ");
            string shape = FormatStructShapeFromDeclaration(symbol);
            builder.Append(string.IsNullOrEmpty(shape) ? "struct { }" : shape);
            return builder.ToString();
        }

        private static string FormatStructShapeFromDeclaration(ObjectDeclarationSymbol symbol)
        {
            if (symbol.DeclaringAstNode is TyhpTypeAliasAst { StructShape: { } aliasShape })
            {
                return FormatStructShape(aliasShape);
            }

            if (symbol.DeclaringAstNode is TyhpStructDeclAst structDecl)
            {
                return FormatStructBody(
                    structDecl.PropertyList,
                    FormatClassName(structDecl.Extends));
            }

            var builder = new StringBuilder();
            builder.Append("struct {");
            foreach (IBaseSymbol member in symbol.Members.Values)
            {
                if (member is not ObjectPropertySymbol property)
                {
                    continue;
                }

                builder.Append(' ');
                builder.Append(FormatStructPropertySignature(property));
                builder.Append(';');
            }

            builder.Append(" }");
            return builder.ToString();
        }

        private static string FormatStructShape(TyhpStructShapeAst shape)
        {
            return FormatStructBody(shape.PropertyList, FormatClassName(shape.Extends));
        }

        private static string FormatStructBody(TyhpStructPropertyListAst? properties, string? extends)
        {
            var builder = new StringBuilder();
            builder.Append("struct");
            if (!string.IsNullOrEmpty(extends))
            {
                builder.Append(" extends ");
                builder.Append(extends);
            }

            builder.Append(" {");
            foreach (TyhpStructPropertyAst property in properties?.GetAllNotNull() ?? [])
            {
                builder.Append(' ');
                builder.Append(FormatStructProperty(property));
                builder.Append(';');
            }

            builder.Append(" }");
            return builder.ToString();
        }

        private static string FormatStructProperty(TyhpStructPropertyAst property)
        {
            var builder = new StringBuilder();
            string type = FormatType(property.TypeExpression);
            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            string name = property.Property?.Identifier ?? property.Identifier;
            if (!string.IsNullOrEmpty(property.AliasOf))
            {
                builder.Append(property.AliasOf);
                builder.Append(" as ");
            }

            builder.Append(EnsureVariableName(name));

            string? defaultValue = FormatExpression(property.Property?.DefaultValue);
            if (!string.IsNullOrEmpty(defaultValue))
            {
                builder.Append(" = ");
                builder.Append(defaultValue);
            }

            return builder.ToString();
        }

        private static string FormatStructPropertySignature(ObjectPropertySymbol property)
        {
            var builder = new StringBuilder();
            string type = FormatType(property.DeclaredType);
            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            builder.Append(EnsureVariableName(property.Name));
            return builder.ToString();
        }

        private static string FormatClassName(IBase2Ast? name)
        {
            return name is null ? string.Empty : FirstNonEmpty(name.ValueString, name.Identifier);
        }

        private static string FormatGenericParameter(GenericTypeParameterSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append(symbol.Name);
            string constraint = FormatType(symbol.Constraint);
            if (!string.IsNullOrEmpty(constraint))
            {
                builder.Append(" extends ");
                builder.Append(constraint);
            }

            return builder.ToString();
        }

        private static string FormatUseInclude(UseIncludeSymbol symbol)
        {
            var builder = new StringBuilder();
            builder.Append("use ");
            builder.Append(symbol.ImportedName);
            if (!string.IsNullOrEmpty(symbol.AliasName)
                && !string.Equals(symbol.AliasName, symbol.Name, StringComparison.Ordinal))
            {
                builder.Append(" as ");
                builder.Append(symbol.AliasName);
            }

            return builder.ToString();
        }

        private static string FormatFallback(BaseSymbol symbol)
        {
            return string.IsNullOrEmpty(symbol.FullyQualifiedName)
                ? symbol.Name
                : symbol.FullyQualifiedName.TrimStart('\\');
        }

        private static bool HasImplicitExtensionReceiver(ObjectMethodSymbol method)
        {
            if (method is ObjectOperatorOverloadMethodSymbol)
            {
                return false;
            }

            if (method.Parameters.Count == 0
                || !IsThisParameterName(method.Parameters[0].Name))
            {
                return false;
            }

            if (!IsExtensionMember(method))
            {
                return false;
            }

            return !SourceParameterListDeclaresThis(method.DeclaringAstNode);
        }

        private static bool IsExtensionMember(ObjectMethodSymbol method)
        {
            for (IBaseScope? scope = method.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj.IsExtension || obj.IsExtensionTargetGroup;
                }
            }

            return false;
        }

        private static bool SourceParameterListDeclaresThis(IBase2Ast? node)
        {
            PhpParameterListAst? list = node switch
            {
                PhpFunctionDeclAst function => function.Parameters,
                PhpMethodDeclAst method => method.Parameters,
                TyhpdefInlineExtensionFunctionAst inline => inline.Method?.Parameters,
                _ => null,
            };

            if (list is null)
            {
                return false;
            }

            foreach (PhpParameterAst parameter in list.GetAllNotNull())
            {
                if (IsThisParameterName(parameter.Name) || IsThisParameterName(parameter.ValueString))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsThisParameterName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return string.Equals(name, "$this", StringComparison.Ordinal)
                || string.Equals(name, "this", StringComparison.Ordinal);
        }

        private static string FormatParameters(IReadOnlyList<ParameterInfo> parameters)
        {
            if (parameters.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", parameters.Select(FormatParameter));
        }

        /// <summary>Single-parameter label for signature help (type, name, default).</summary>
        internal static string FormatParameterLabel(ParameterInfo parameter) => FormatParameter(parameter);

        private static string FormatParameter(ParameterInfo parameter)
        {
            var builder = new StringBuilder();
            string type = FormatType(parameter.DeclaredType);
            if (!string.IsNullOrEmpty(type))
            {
                builder.Append(type);
                builder.Append(' ');
            }

            if (parameter.IsVariadic)
            {
                builder.Append("...");
            }

            if (parameter.IsByReference)
            {
                builder.Append('&');
            }

            builder.Append(EnsureVariableName(parameter.Name));
            string? defaultValue = FormatExpression(parameter.DefaultValue);
            if (!string.IsNullOrEmpty(defaultValue))
            {
                builder.Append(" = ");
                builder.Append(defaultValue);
            }

            return builder.ToString();
        }

        private static void AppendFunctionModifiers(
            StringBuilder builder,
            MemberModifier visibility,
            bool isStatic,
            bool isAbstract,
            bool isAsync)
        {
            if (visibility.HasFlag(MemberModifier.Public)
                || visibility.HasFlag(MemberModifier.Protected)
                || visibility.HasFlag(MemberModifier.Private))
            {
                AppendVisibility(builder, visibility);
            }

            if (isAbstract || visibility.HasFlag(MemberModifier.Abstract))
            {
                builder.Append("abstract ");
            }

            if (visibility.HasFlag(MemberModifier.Final))
            {
                builder.Append("final ");
            }

            if (isStatic || visibility.HasFlag(MemberModifier.Static))
            {
                builder.Append("static ");
            }

            if (isAsync || visibility.HasFlag(MemberModifier.Async))
            {
                builder.Append("async ");
            }
        }

        private static void AppendVisibility(StringBuilder builder, MemberModifier visibility)
        {
            if (visibility.HasFlag(MemberModifier.Private))
            {
                builder.Append("private ");
                return;
            }

            if (visibility.HasFlag(MemberModifier.Protected))
            {
                builder.Append("protected ");
                return;
            }

            if (visibility.HasFlag(MemberModifier.Public) || visibility == MemberModifier.None)
            {
                builder.Append("public ");
            }
        }

        private static void AppendReturnType(StringBuilder builder, ITypeExpression? returnType)
        {
            string type = FormatType(returnType);
            if (string.IsNullOrEmpty(type))
            {
                return;
            }

            builder.Append(": ");
            builder.Append(type);
        }

        private static void AppendGenericParameters(
            StringBuilder builder,
            IReadOnlyList<GenericTypeParameterSymbol>? parameters)
        {
            if (parameters is null || parameters.Count == 0)
            {
                return;
            }

            builder.Append('<');
            builder.Append(string.Join(", ", parameters.Select(FormatGenericParameter)));
            builder.Append('>');
        }

        private static string FormatNamedType(PhpNamedTypeAst named)
        {
            if (named.Name is TyhpGenericIdentifierAst generic)
            {
                string name = FirstNonEmpty(generic.ValueString, generic.Identifier);
                if (generic.GenericArguments is TyhpGenericsTypeArgumentListAst args)
                {
                    var parts = args.GetAllNotNull()
                        .Select(arg => FormatType(arg.TypeConstraint) is { Length: > 0 } constraint
                            ? constraint
                            : FirstNonEmpty(arg.Name?.ValueString, arg.Identifier))
                        .Where(static part => !string.IsNullOrEmpty(part));
                    string joined = string.Join(", ", parts);
                    return string.IsNullOrEmpty(joined) ? name : name + "<" + joined + ">";
                }

                return name;
            }

            if (named.Name is PhpNameAst nameAst)
            {
                return FirstNonEmpty(nameAst.ValueString, nameAst.Identifier);
            }

            return FirstNonEmpty(named.Name?.ValueString, named.Identifier);
        }

        /// <summary>
        /// Use-site <c>&lt;T, …&gt;</c> live on the <c>typeName</c> grammar addon
        /// (<c>array&lt;string, self&gt;</c>, <c>Box&lt;int&gt;</c>) or on a
        /// <see cref="TyhpGenericIdentifierAst"/> whose children are a
        /// <see cref="PhpTypeExpressionListAst"/>. Declaration-site parameter lists
        /// (<see cref="TyhpGenericsTypeArgumentListAst"/>) are handled by
        /// <see cref="FormatNamedType"/>.
        /// </summary>
        private static string AppendUseSiteGenericArguments(string name, IBase2Ast node)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('<', StringComparison.Ordinal))
            {
                return name;
            }

            IReadOnlyList<ITypeExpression> args = GetUseSiteGenericArguments(node);
            if (args.Count == 0)
            {
                return name;
            }

            string joined = string.Join(
                ", ",
                args.Select(FormatType).Where(static part => !string.IsNullOrEmpty(part)));
            return string.IsNullOrEmpty(joined) ? name : name + "<" + joined + ">";
        }

        private static IReadOnlyList<ITypeExpression> GetUseSiteGenericArguments(IBase2Ast node)
        {
            if (node.AstGrammarAddons.TryGetValue("typeName", out IBase2Ast? addon)
                && addon is PhpTypeExpressionListAst addonList)
            {
                return addonList.GetAllNotNull().ToList();
            }

            Base2Ast? identifierArgs = node switch
            {
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst genericOnNamed }
                    => genericOnNamed.GenericArguments,
                TyhpGenericIdentifierAst generic => generic.GenericArguments,
                _ => null,
            };

            return identifierArgs is PhpTypeExpressionListAst list
                ? list.GetAllNotNull().ToList()
                : [];
        }

        private static string FormatTypeExpression(PhpTypeExpressionAst expr)
        {
            IReadOnlyList<ITypeExpression> members = expr.Types?.GetAllNotNull().ToList()
                ?? [];
            string separator = expr.TypeKind == PhpTypeKind.Intersection ? "&" : "|";
            bool wrapCallable = expr.TypeKind is PhpTypeKind.Union or PhpTypeKind.Intersection;
            string joined = string.Join(
                separator,
                members
                    .Select(member =>
                    {
                        string text = FormatType(member);
                        if (string.IsNullOrEmpty(text))
                        {
                            return text;
                        }

                        return wrapCallable && TyhpCallableShapeAst.Find(member) is not null
                            ? "(" + text + ")"
                            : text;
                    })
                    .Where(static part => !string.IsNullOrEmpty(part)));
            if (expr.IsNullable && !string.IsNullOrEmpty(joined) && !joined.Contains('|', StringComparison.Ordinal))
            {
                return "?" + joined;
            }

            if (expr.IsNullable && !string.IsNullOrEmpty(joined) && !joined.Contains("null", StringComparison.OrdinalIgnoreCase))
            {
                return joined + "|null";
            }

            return joined;
        }

        private static string? FormatExpression(IExpression? expression)
        {
            if (expression is null)
            {
                return null;
            }

            string text = FirstNonEmpty(expression.ValueString, expression.Identifier);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static string FormatContainingScope(BaseSymbol symbol)
        {
            IBaseScope? scope = symbol.ContainingScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    var kindWord = ObjectKindKeyword(obj);
                    return "in " + (kindWord.Length == 0 ? obj.Name : kindWord + " " + obj.Name);
                }

                if (scope.DeclarationSymbol is FunctionDeclarationSymbol function)
                {
                    return "in function " + function.Name;
                }

                scope = scope.ParentScope;
            }

            return string.Empty;
        }

        private static string? FormatDocComment(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string text = raw.Trim();
            if (text.StartsWith("/**", StringComparison.Ordinal))
            {
                text = text[3..];
            }
            else if (text.StartsWith("/*", StringComparison.Ordinal))
            {
                text = text[2..];
            }

            if (text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[..^2];
            }

            var lines = text
                .Split(['\r', '\n'], StringSplitOptions.None)
                .Select(static line =>
                {
                    string trimmed = line.Trim();
                    return trimmed.StartsWith('*') ? trimmed[1..].TrimStart() : trimmed;
                })
                .ToList();

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            string joined = string.Join("\n", lines).Trim();
            return string.IsNullOrEmpty(joined) ? null : joined;
        }

        private static string ObjectKindKeyword(ObjectDeclarationSymbol symbol)
        {
            if (symbol.IsExtension)
            {
                return "extension";
            }

            if (symbol.IsStruct)
            {
                return "struct shape";
            }

            return symbol.ObjectKind switch
            {
                PhpTypeDeclType.Interface => "interface",
                PhpTypeDeclType.Trait => "trait",
                PhpTypeDeclType.Enum => "enum",
                PhpTypeDeclType.Unspecified => "",
                _ => "class",
            };
        }

        private static string FormatExternKindLabel(ObjectDeclarationSymbol obj)
        {
            if (!obj.IsExtern)
            {
                return ObjectKindKeyword(obj);
            }

            var kindWord = ObjectKindKeyword(obj);
            return kindWord.Length == 0 ? "extern" : "extern " + kindWord;
        }

        private static string EnsureVariableName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "$value";
            }

            return name.StartsWith('$') ? name : "$" + name;
        }

        private static string FormatVariableName(string name) => EnsureVariableName(name);

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
