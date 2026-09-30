using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private const string OutputFileDeclareKey = "output_file";
        private const string AutoloadDeclareKey = "autoload";
        private const string PhpDeclareKey = "php";
        private const string TyhpPhpAttributeFqn = "\\Tyhp\\Php";

        private EmitItem ApplyDocComment(IBase2Ast node, EmitItem item)
        {
            if (!this._context.Config.IncludeComments || string.IsNullOrWhiteSpace(node.DocComment))
            {
                return item;
            }

            return EmitItem.AttachDocComment(node.DocComment, item);
        }

        /// <summary>
        /// Inserts <c>#[…]</c> lines onto <paramref name="target"/> after any leading docblock and
        /// before the declaration signature. PHP / PHPDoc convention is docblock → attributes →
        /// declaration (not attributes above the docblock).
        /// </summary>
        private void AttachAttributes(IBase2Ast attributeSource, EmitItem target)
        {
            var lines = this.CollectAttributeLines(attributeSource);
            if (lines.Count == 0)
            {
                return;
            }

            InsertLinesAfterDocComment(target, lines);
        }

        /// <summary>
        /// Formats attributes for inline use on a parameter or property hook
        /// (<c>#[Attr] string $p</c>, <c>#[Attr] get { … }</c>).
        /// </summary>
        private string FormatInlineAttributes(IBase2Ast attributeSource)
        {
            var lines = this.CollectAttributeLines(attributeSource);
            return lines.Count == 0 ? "" : string.Join(" ", lines) + " ";
        }

        private List<string> CollectAttributeLines(IBase2Ast attributeSource)
        {
            var lines = new List<string>();
            foreach (var attributeNode in attributeSource.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute
                    && !NoEmitAttributeSupport.ShouldOmitAttributeUsage(attribute)
                    && !GenericRuntimeAttributeSupport.IsGenericRuntimeAttribute(attribute))
                {
                    lines.Add(this.FormatAttributeLine(attribute));
                }
            }

            var stamp = this.TryFormatGenericRuntimeStamp(attributeSource);
            if (stamp is not null)
            {
                lines.Add(stamp);
            }

            return lines;
        }

        private string FormatAttributeLine(PhpAttributeAst attribute)
        {
            // Attribute names are always root-anchored when resolved (predates Prop-init #17's
            // BoundSymbol-on-bare-names change; see TrackAndBuildName's forceFqnForBoundSymbol).
            var name = attribute.Name is PhpNameAst attributeName
                ? this.TrackAndBuildName(attributeName, forceFqnForBoundSymbol: true)
                : this.BuildExpression(attribute.Name);
            var args = attribute.Arguments?.GetAllNotNull().Any() == true
                ? "(" + this.FormatArgumentList(attribute.Arguments) + ")"
                : "";
            return $"#[{name}{args}]";
        }

        /// <summary>
        /// Warns for each attribute that cannot be represented on the target PHP version.
        /// Stripping changes Reflection semantics, so the diagnostic always fires when attributes
        /// are present (they would be required at runtime for <c>Reflection*::getAttributes</c>).
        /// </summary>
        private void ReportStrippedAttributes(
            IBase2Ast attributeSource,
            string targetDescription,
            string requiredPhpVersion)
        {
            var phpVersion = this._context.Config.TargetPhpVersion ?? requiredPhpVersion;
            var file = this._context.CurrentSourceFile?.Identifier ?? "";
            foreach (var attributeNode in attributeSource.AstAttributes)
            {
                if (attributeNode is not PhpAttributeAst attribute
                    || NoEmitAttributeSupport.ShouldOmitAttributeUsage(attribute))
                {
                    continue;
                }

                this._context.Diagnostics.AddWarningFromAst(
                    MessageCode.EmitterAttributeStrippedForPhpVersion,
                    attribute,
                    file,
                    FormatAttributeNameForDiagnostic(attribute),
                    targetDescription,
                    phpVersion);
            }
        }

        private void ReportStrippedPropertyHookAttributes(PhpPropertyHookListAst? hooks)
        {
            if (hooks == null)
            {
                return;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                var hookName = string.IsNullOrEmpty(hook.Identifier) ? "property hook" : $"property hook `{hook.Identifier}`";
                this.ReportStrippedAttributes(hook, hookName, requiredPhpVersion: "8.4");
            }
        }

        /// <summary>
        /// Display name for strip diagnostics — does not track imports (attributes are not emitted).
        /// </summary>
        private static string FormatAttributeNameForDiagnostic(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol objectSymbol }
                && !string.IsNullOrWhiteSpace(objectSymbol.FullyQualifiedName))
            {
                return "\\" + objectSymbol.FullyQualifiedName.TrimStart('\\');
            }

            return attribute.Name switch
            {
                PhpNameAst name => name.ValueString ?? "?",
                TokenValueAst token => token.ValueString ?? "?",
                { } expr => expr.Identifier ?? "?",
                _ => "?",
            };
        }

        private static void InsertLinesAfterDocComment(EmitItem emit, List<string> lines)
        {
            var start = emit.StartContent is List<string> list
                ? list
                : emit.StartContent.ToList();
            if (!ReferenceEquals(start, emit.StartContent))
            {
                emit.StartContent = start;
            }

            var insertAt = 0;
            if (start.Count > 0 && start[0].TrimStart().StartsWith("/**", StringComparison.Ordinal))
            {
                for (var i = 0; i < start.Count; i++)
                {
                    if (start[i].TrimEnd().EndsWith("*/", StringComparison.Ordinal))
                    {
                        insertAt = i + 1;
                        break;
                    }
                }
            }

            start.InsertRange(insertAt, lines);
        }

        private string FormatModifiers(PhpModifierListAst? modifiers)
        {
            if (modifiers == null)
            {
                return "";
            }

            var parts = new List<(PhpModifier Modifier, string Text)>();
            foreach (var modifier in modifiers.Modifiers)
            {
                switch (modifier)
                {
                    case PhpModifier.Public:
                    case PhpModifier.Protected:
                    case PhpModifier.Private:
                    case PhpModifier.Static:
                    case PhpModifier.Abstract:
                    case PhpModifier.Final:
                    case PhpModifier.Readonly:
                    case PhpModifier.Var:
                        parts.Add((modifier, modifier.ToString().ToLowerInvariant()));
                        break;
                    case PhpModifier.PublicSet:
                        parts.Add((modifier, "public(set)"));
                        break;
                    case PhpModifier.ProtectedSet:
                        parts.Add((modifier, "protected(set)"));
                        break;
                    case PhpModifier.PrivateSet:
                        parts.Add((modifier, "private(set)"));
                        break;
                    case PhpModifier.Internal:
                        // Tyhp-only: never appears in PHP. Class members get `public` via
                        // EnsureClassMemberVisibility / EnsureMethodVisibility after this strip.
                        break;
                }
            }

            if (parts.Count == 0)
            {
                return "";
            }

            // PER-CS 2.0 §4.6 / 3.0 asymmetric visibility: [abstract|final]
            // [public|protected|private] [public(set)|protected(set)|private(set)] [static] [readonly]
            var ordered = parts
                .Select((p, index) => (p.Text, Key: PhpModifierOrderKey(p.Modifier), index))
                .OrderBy(p => p.Key)
                .ThenBy(p => p.index)
                .Select(p => p.Text);
            return string.Join(" ", ordered) + " ";
        }

        private static int PhpModifierOrderKey(PhpModifier modifier)
            => modifier switch
            {
                PhpModifier.Abstract or PhpModifier.Final => 0,
                PhpModifier.Public or PhpModifier.Protected or PhpModifier.Private => 1,
                PhpModifier.PublicSet or PhpModifier.ProtectedSet or PhpModifier.PrivateSet => 2,
                PhpModifier.Static => 3,
                PhpModifier.Readonly => 4,
                PhpModifier.Var => 5,
                _ => 6,
            };

        /// <summary>
        /// PHP has no class/interface/trait/enum visibility. Keep only <c>abstract</c> /
        /// <c>final</c> / <c>readonly</c>; drop <c>public</c>/<c>protected</c>/<c>private</c>
        /// and Tyhp <c>internal</c>.
        /// </summary>
        private string FormatTypeDeclarationModifiers(PhpModifierListAst? modifiers)
        {
            if (modifiers == null)
            {
                return "";
            }

            var parts = new List<(int Key, int Index, string Text)>();
            var index = 0;
            foreach (var modifier in modifiers.Modifiers)
            {
                switch (modifier)
                {
                    case PhpModifier.Abstract:
                    case PhpModifier.Final:
                        parts.Add((0, index++, modifier.ToString().ToLowerInvariant()));
                        break;
                    case PhpModifier.Readonly:
                        parts.Add((1, index++, "readonly"));
                        break;
                }
            }

            return parts.Count == 0
                ? ""
                : string.Join(" ", parts.OrderBy(p => p.Key).ThenBy(p => p.Index).Select(p => p.Text)) + " ";
        }

        /// <summary>
        /// After stripping <c>internal</c>, class members that have no PHP visibility emit
        /// <c>public</c>. <c>var</c> already means public and must not become <c>public var</c>.
        /// </summary>
        private string EnsureClassMemberVisibility(string modifiers)
        {
            if (modifiers.Contains("var", StringComparison.OrdinalIgnoreCase))
            {
                return modifiers;
            }

            return this.EnsureMethodVisibility(modifiers);
        }

        private string ApplyNamespacePrefix(string? namespaceName)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                return "";
            }

            var prefix = this._context.Config.NamespacePrefix;
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return namespaceName;
            }

            return $"{prefix.TrimEnd('\\')}\\{namespaceName.TrimStart('\\')}";
        }

        private void TrackImport(string importFqn)
        {
            this._context.TrackUsedImport(importFqn);
        }

        private void ReportTyhpConstructNotImplemented(IBase2Ast node, string constructName)
        {
            this._context.Diagnostics.AddWarningFromAst(
                MessageCode.EmitterTyhpConstructNotImplemented,
                node,
                this._context.CurrentSourceFile?.Identifier ?? "",
                constructName);
        }

        /// <summary>
        /// Layout 1 is the only dispatch this compiler implements. Empty intersection with the
        /// declaration's <c>layouts</c> is an error — do not guess a call shape.
        /// </summary>
        private bool GenericRuntimeLayoutIsSupported(IBaseSymbol? symbol, IBase2Ast? node)
        {
            var info = GenericRuntimeAttributeSupport.TryRead(symbol);
            if (info is null || info.HasSupportedLayout())
            {
                return true;
            }

            var compilerSuffix = string.IsNullOrWhiteSpace(info.Compiler)
                ? ""
                : "; compiled by `" + info.Compiler + "`";
            var fileName = this._context.CurrentSourceFile?.Identifier
                ?? symbol?.SourceFile
                ?? "";
            if (node is not null)
            {
                this._context.Diagnostics.AddErrorFromAst(
                    MessageCode.EmitterGenericRuntimeLayoutUnsupported,
                    node,
                    fileName,
                    info.FormatLayoutsForDiagnostic(),
                    compilerSuffix);
            }
            else if (symbol?.DeclaringAstNode is { } declaring)
            {
                this._context.Diagnostics.AddErrorFromAst(
                    MessageCode.EmitterGenericRuntimeLayoutUnsupported,
                    declaring,
                    fileName,
                    info.FormatLayoutsForDiagnostic(),
                    compilerSuffix);
            }

            return false;
        }

        private string? TryFormatGenericRuntimeStamp(IBase2Ast source)
        {
            if (this._currentVariantGenericParams.Count > 0)
            {
                return null;
            }

            var stamp = this.TryBuildGenericRuntimeStamp(source.BoundSymbol);
            return stamp is null ? null : GenericRuntimeAttributeSupport.FormatAttributeLine(stamp);
        }

        private GenericRuntimeInfo? TryBuildGenericRuntimeStamp(IBaseSymbol? symbol)
        {
            switch (symbol)
            {
                case ObjectDeclarationSymbol obj:
                    return this.BuildClassGenericRuntimeStamp(obj);
                case ObjectMethodSymbol method:
                    return this.BuildCallableGenericRuntimeStamp(
                        method,
                        method.GenericParameters,
                        method.IsAbstract,
                        method.Name + GeneratedNames.GenericVariantSuffix);
                case FunctionDeclarationSymbol function:
                    return this.BuildCallableGenericRuntimeStamp(
                        function,
                        function.GenericParameters,
                        isAbstract: false,
                        function.Name + GeneratedNames.GenericVariantSuffix);
                default:
                    return null;
            }
        }

        private GenericRuntimeInfo? BuildClassGenericRuntimeStamp(ObjectDeclarationSymbol obj)
        {
            if (obj.GenericParameters.Count == 0
                || obj.IsStruct
                || obj.ObjectKind is PhpTypeDeclType.Interface or PhpTypeDeclType.Trait
                || (obj.Visibility & MemberModifier.Abstract) != 0)
            {
                return null;
            }

            var tracked = this._context.RequiresRuntimeGenericTrackingFor(obj);
            return new GenericRuntimeInfo
            {
                Erased = !tracked,
                Factory = tracked ? GeneratedNames.GenericFactory(obj.FullyQualifiedName) : null,
                Layouts = [GenericRuntimeAttributeSupport.CurrentLayout],
                Compiler = IncrementalBuildService.GetCompilerVersion(),
            };
        }

        private GenericRuntimeInfo? BuildCallableGenericRuntimeStamp(
            IBaseSymbol symbol,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            bool isAbstract,
            string binderName)
        {
            if (genericParameters.Count == 0 || isAbstract)
            {
                return null;
            }

            if (symbol is ObjectMethodSymbol method
                && TryGetOwningObject(method) is { ObjectKind: PhpTypeDeclType.Interface })
            {
                return null;
            }

            var tracked = this._context.RequiresGenericVariantFor(symbol);
            return new GenericRuntimeInfo
            {
                Erased = !tracked,
                Binder = tracked ? binderName : null,
                Layouts = [GenericRuntimeAttributeSupport.CurrentLayout],
                Compiler = IncrementalBuildService.GetCompilerVersion(),
            };
        }

        private static ObjectDeclarationSymbol? TryGetOwningObject(IBaseSymbol symbol)
        {
            for (var scope = symbol.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }
            }

            return null;
        }

        /// <summary>
        /// Defense in depth: a <c>.tyhp</c> type position that still names an <c>extern</c>
        /// placeholder should have been TYHP4307 at check. Do not silently spell it as PHP.
        /// </summary>
        private void ReportInternalErrorIfExternType(ITypeExpression? typeExpression)
        {
            if (typeExpression is null || IsTyhpdefEmitSource())
            {
                return;
            }

            var externSymbol = Checker.Rules.ExternTypeUse.FindExternSymbol(typeExpression);
            if (externSymbol is null)
            {
                return;
            }

            var displayName = string.IsNullOrEmpty(externSymbol.FullyQualifiedName)
                ? externSymbol.Name
                : externSymbol.FullyQualifiedName;
            this._context.Diagnostics.AddErrorFromAst(
                MessageCode.EmitterUnsupportedConstruct,
                typeExpression,
                this._context.CurrentSourceFile?.Identifier ?? "",
                "extern type " + displayName);
        }

        private void ReportInternalErrorIfExternCheckedType(ICheckedType? type)
        {
            if (type is null || IsTyhpdefEmitSource())
            {
                return;
            }

            var externSymbol = Checker.Rules.ExternTypeUse.FindExternSymbol(type);
            if (externSymbol is null)
            {
                return;
            }

            var displayName = string.IsNullOrEmpty(externSymbol.FullyQualifiedName)
                ? externSymbol.Name
                : externSymbol.FullyQualifiedName;
            var construct = "extern type " + displayName;
            if (this._context.CurrentSourceFile is { } sourceFile)
            {
                this._context.Diagnostics.AddErrorFromAst(
                    MessageCode.EmitterUnsupportedConstruct,
                    sourceFile,
                    sourceFile.Identifier ?? "",
                    construct);
                return;
            }

            this._context.Diagnostics.AddError(
                MessageCode.EmitterUnsupportedConstruct,
                "",
                0,
                0,
                construct);
        }

        private bool IsTyhpdefEmitSource()
        {
            var file = this._context.CurrentSourceFile?.FileName
                ?? this._context.CurrentSourceFile?.Identifier
                ?? string.Empty;
            return file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    this._context.CurrentSourceFile?.LanguageMode,
                    "tyhpdef",
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAsyncModifiers(IBase2Ast node)
        {
            if (node.AstGrammarAddons.ContainsKey("isAsync"))
            {
                return true;
            }

            // Class methods: visitor attaches `isAsync` on PhpModifierListAst (Modifiers), not the method node.
            if (node is PhpMethodDeclAst method
                && method.Modifiers?.AstGrammarAddons.ContainsKey("isAsync") == true)
            {
                return true;
            }

            if (node is PhpMethodDeclAst { BoundSymbol: ObjectMethodSymbol { IsAsync: true } }
                || node is PhpFunctionDeclAst { BoundSymbol: FunctionDeclarationSymbol { IsAsync: true } })
            {
                return true;
            }

            if (!node.AstGrammarAddons.TryGetValue("modifiers", out var addon))
            {
                return false;
            }

            return addon switch
            {
                TokenValueListAst list => list.GetAllNotNull().Any(IsAsyncToken),
                TokenValueAst token => IsAsyncToken(token),
                _ => false,
            };
        }

        private static bool IsAsyncToken(TokenValueAst token) =>
            string.Equals(token.ValueString, "async", StringComparison.OrdinalIgnoreCase)
            || token.ValueInt64 == TyhpParser.T_TYHP_ASYNC;

        private string FormatExpressionList(IEnumerable<IExpression?>? expressions, string separator = ", ")
        {
            if (expressions == null)
            {
                return "";
            }

            return string.Join(separator, expressions.Where(e => e != null).Select(e => this.BuildExpression(e)));
        }

        private string FormatExpressionList(PhpExpressionListAst? list, string separator = ", ")
            => this.FormatExpressionList(list?.GetAll(), separator);

        private string FormatVariableList(PhpVariableListAst? list, string separator = ", ")
        {
            if (list == null)
            {
                return "";
            }

            return string.Join(separator, list.GetAllNotNull().Select(v =>
                (v is PhpVariableAst { IsRef: true } ? "&" : "") + this.BuildExpression(v)));
        }

        private string FormatClassNameList(PhpClassNameListAst? list, string separator = ", ")
        {
            if (list == null)
            {
                return "";
            }

            return string.Join(separator, list.GetAllNotNull().Select(c => this.BuildClassName(c)));
        }

        private string BuildClassName(IClassName? className)
        {
            if (className == null)
            {
                return "";
            }

            if (className is IExpression expr)
            {
                return this.BuildExpression(expr);
            }

            return className.Identifier ?? "";
        }

        /// <summary>
        /// Text of a name node used for matching trait-use names.
        /// <see cref="IBase2Ast.Identifier"/> defaults to <c>""</c> (not null) on nodes such as
        /// <see cref="PhpNameAst"/>, so <c>node.Identifier ?? node.ValueString</c> never falls
        /// through to <c>ValueString</c>.
        /// </summary>
        private static string NameText(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            return string.IsNullOrEmpty(node.Identifier) ? (node.ValueString ?? "") : node.Identifier;
        }

        private EmitItem EmitStatementBlock(PhpStatementBlockAst? block, EmitItem parent, EmitType emitType)
        {
            if (block == null)
            {
                return EmitItem.Block(parent.Provider, emitType, "{", "}", parent);
            }

            var wrapper = EmitItem.Block(block, emitType, "{", "}", parent);
            this.EmitBlockContents(block, wrapper, emitType);
            return wrapper;
        }

        private static bool ContainsUsingEqualAssignment(IBase2Ast? node)
        {
            if (node is PhpBinaryOpAst binary && IsUsingEqualOperator(binary))
            {
                return true;
            }

            if (node == null)
            {
                return false;
            }

            foreach (var child in node.AstChildren)
            {
                // Nested closures own their disposable scope — a closure-local `:=` must not
                // make the enclosing block create a `$__scope` the closure can't reach.
                if (child is PhpInlineFunctionAst or TyhpAsyncBlockAst)
                {
                    continue;
                }

                if (child is IBase2Ast ast && ContainsUsingEqualAssignment(ast))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsingEqualOperator(PhpBinaryOpAst binary)
        {
            var token = binary.Operator;
            if (token is null)
            {
                return false;
            }

            return PhpAssignmentOperatorExtensions.FromToken(token.TokenValue, token.ValueString)
                == PhpAssignmentOperator.UsingEqual;
        }

        private EmitItem EmitBracedBody(IStatement? body, EmitItem parent, EmitType emitType)
        {
            var wrapper = EmitItem.Block(
                body ?? parent.Provider,
                emitType,
                "{",
                "}",
                parent);

            if (body is PhpStatementBlockAst block)
            {
                this.EmitBlockContents(block, wrapper, emitType);
            }
            else if (body != null)
            {
                this.EmitStatement(body, wrapper);
            }

            return wrapper;
        }

        private bool ShouldEmitFileDeclare(PhpDeclareAst declare)
        {
            if (declare.Declarations == null)
            {
                return true;
            }

            var directives = declare.Declarations.GetAllNotNull().ToList();
            return directives.Count == 0
                || directives.Any(c => !IsTyhpOnlyDeclareKey(c.Identifier));
        }

        private static bool IsTyhpOnlyDeclareKey(string? identifier) =>
            string.Equals(identifier, OutputFileDeclareKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(identifier, AutoloadDeclareKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(identifier, PhpDeclareKey, StringComparison.OrdinalIgnoreCase)
            || IsExtDeclareKey(identifier);

        private static bool IsPhpDeclareKey(string? identifier) =>
            string.Equals(identifier, PhpDeclareKey, StringComparison.OrdinalIgnoreCase);

        /// <summary><c>declare(ext="name")</c> is a compile-time gate like <c>declare(php=…)</c>.</summary>
        private static bool IsExtDeclareKey(string? identifier) =>
            string.Equals(identifier, "ext", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when every directive is a Tyhp-only key and at least one is <c>php</c>.
        /// Those declares never appear in PHP: empty ones are omitted, blocks unwrap.
        /// </summary>
        private static bool IsPhpOnlyDeclare(PhpDeclareAst declare)
        {
            var hasPhp = false;
            foreach (var directive in declare.Declarations?.GetAllNotNull() ?? [])
            {
                if (IsPhpDeclareKey(directive.Identifier) || IsExtDeclareKey(directive.Identifier))
                {
                    hasPhp = true;
                    continue;
                }

                if (!IsTyhpOnlyDeclareKey(directive.Identifier))
                {
                    return false;
                }
            }

            return hasPhp;
        }

        /// <summary>
        /// True when the binder marked this block's <c>declare(php=…)</c> gate inactive.
        /// File-level (non-block) inactivity is <see cref="FileSymbol.IsPhpVersionGateInactive"/>
        /// and is handled at split time.
        /// </summary>
        private static bool IsPhpVersionGateInactive(PhpDeclareAst declareAst)
            => declareAst.BoundSymbol is DeclareBlockSymbol { IsPhpVersionGateInactive: true };

        /// <summary>
        /// Omits declarations the binder dropped because <c>#[\Tyhp\Php]</c> did not match.
        /// Declarations without that attribute still emit when unbound (parse-only emit tests).
        /// </summary>
        private static bool ShouldEmitPhpVersionGatedDeclaration(IBase2Ast node)
        {
            if (!HasTyhpPhpGateAttribute(node))
            {
                return true;
            }

            return node.BoundSymbol != null;
        }

        private static bool HasTyhpPhpGateAttribute(IBase2Ast node)
        {
            foreach (var attributeNode in node.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsTyhpPhpGateAttribute(attribute))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compile-time <c>#[\Tyhp\Php]</c> gate — identified by bound FQCN or written name,
        /// matching binder recognition. Used to omit unsatisfied gated declarations, not to
        /// strip the attribute from PHP (that is <see cref="NoEmitAttributeSupport"/>).
        /// </summary>
        private static bool IsTyhpPhpGateAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol objectSymbol }
                && IsTyhpPhpFullyQualifiedName(objectSymbol.FullyQualifiedName))
            {
                return true;
            }

            var written = attribute.Name switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                { } expr => expr.Identifier,
                _ => null,
            };
            return IsTyhpPhpFullyQualifiedName(written);
        }

        private static bool IsTyhpPhpFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            return trimmed.Equals(TyhpPhpAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\Php", StringComparison.OrdinalIgnoreCase);
        }

        // `is` is the Tyhp source spelling that aliases `instanceof`
        // (Tyhp/TyhpLang/Grammar/TyhpParser.g4 phpExprBinaryOpGrammarAddon002; PhpBinaryOperator.cs maps
        // T_TYHP_IS to PhpBinaryOperator.InstanceOf for checker/binder purposes). Emitting the raw
        // token text here would put the literal word `is` into the output PHP, which PHP does not
        // recognize as an operator at all. Normalize to the one spelling PHP understands.
        private string GetOperatorText(TokenValueAst? op)
        {
            if (op is null)
            {
                return "";
            }

            var text = op.ValueString;
            if (!string.IsNullOrEmpty(text))
            {
                return string.Equals(text, "is", StringComparison.OrdinalIgnoreCase)
                    ? "instanceof"
                    : text;
            }

            return op.ValueInt64 == TyhpParser.T_TYHP_IS ? "instanceof" : "";
        }

        /// <summary>
        /// True for PHP <c>instanceof</c> and the Tyhp <c>is</c> alias (same binary operator).
        /// </summary>
        private static bool IsInstanceofLikeOperator(PhpBinaryOpAst binary) =>
            PhpBinaryOperatorExtensions.FromToken(
                binary.Operator is { } op ? op.TokenValue : -1,
                binary.Operator?.ValueString) == PhpBinaryOperator.InstanceOf;

        private string ParenthesizeIfNeeded(IExpression? expr, bool needsParens)
        {
            var text = this.BuildExpression(expr);
            return needsParens ? $"({text})" : text;
        }

        private static bool IsNestedBinaryOrTernary(IExpression? expr)
            => expr is PhpBinaryOpAst or PhpTernaryOpAst;

        /// <summary>
        /// True when <paramref name="name"/> is the special PHP/Tyhp identifier <c>this</c>
        /// (with or without a leading <c>$</c>).
        /// </summary>
        private static bool IsThisParameterName(string? name) =>
            string.Equals(name?.TrimStart('$'), "this", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// PHP variable spelling for a declared parameter/local name, applying the extension-receiver
        /// <c>$this</c> → <c>$this_</c> rename when active.
        /// </summary>
        private string EmitParameterVariableName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name ?? "";
            }

            if (this._context.ExtensionReceiverThisAlias is { } alias
                && IsThisParameterName(name))
            {
                return alias;
            }

            return name;
        }

        /// <summary>
        /// If the first parameter of an extension method is named <c>$this</c>, begin rewriting it to
        /// <see cref="GeneratedNames.ExtensionReceiverThisAlias"/> (or a collision-safe fallback, see
        /// <see cref="ResolveCollisionSafeThisAlias"/>) for the duration of that method's signature and
        /// body emit. Callers must restore the previous alias (typically via try/finally).
        /// </summary>
        private void BeginExtensionReceiverThisRenameIfNeeded(PhpParameterListAst? parameters)
        {
            var allParams = parameters?.GetAllNotNull().ToList();
            var receiver = allParams?.FirstOrDefault();
            if (receiver != null && IsThisParameterName(receiver.Name))
            {
                this._context.ExtensionReceiverThisAlias = ResolveCollisionSafeThisAlias(
                    allParams!.Skip(1).Select(p => p.Name));
            }
        }

        /// <summary>
        /// Block-target extension methods take a synthesized receiver. The receiver is by-ref
        /// only when the member is annotated <c>&amp;$this</c>. A pure or by-value method passes
        /// <c>$this</c> by value, including object, class, interface, and enum targets. The PHP
        /// name is <c>$this_</c> (or a longer alias when a real parameter already uses that spelling).
        /// </summary>
        private void ActivateExtensionReceiverEmit(PhpFunctionDeclAst function)
        {
            var block = FindExtensionBlock(function.BoundSymbol);
            if (block?.ExtensionBlockTargetSymbol == null)
            {
                this._extensionReceiverParameterText = null;
                this.BeginExtensionReceiverThisRenameIfNeeded(function.Parameters);
                return;
            }

            this._context.ExtensionReceiverThisAlias = ResolveCollisionSafeThisAlias(
                function.Parameters?.GetAllNotNull().Select(p => p.Name) ?? []);
            var alias = this._context.ExtensionReceiverThisAlias;
            var typeText = this.SpellExtensionBlockTarget(block);
            var byRef = function.AstGrammarAddons.ContainsKey(TyhpExtensionDeclAst.ByRefReceiverAddonKey);
            var refPrefix = byRef ? "&" : "";
            this._extensionReceiverParameterText = string.IsNullOrWhiteSpace(typeText)
                ? refPrefix + alias
                : typeText + " " + refPrefix + alias;
        }

        private ObjectDeclarationSymbol? FindExtensionBlock(IBaseSymbol? member)
        {
            for (var scope = member?.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj
                    && (obj.IsExtension || obj.IsExtensionTargetGroup)
                    && obj.ExtensionBlockTargetSymbol != null)
                {
                    return obj;
                }
            }

            return null;
        }

        private string? SpellExtensionBlockTarget(ObjectDeclarationSymbol block)
        {
            if (block.PendingExtensionBlockTarget == null)
            {
                return null;
            }

            var spelled = this.BuildTypeExpression(block.PendingExtensionBlockTarget);
            return string.IsNullOrWhiteSpace(spelled) ? null : spelled;
        }

        /// <summary>
        /// <see cref="GeneratedNames.ExtensionReceiverThisAlias"/> (<c>$this_</c>), or — on the rare
        /// chance the author already declared a sibling parameter/operand literally named
        /// <c>$this_</c> — the shortest <c>$this_</c>-prefixed name (<c>$this__</c>, <c>$this___</c>, …)
        /// that does not collide with <paramref name="otherDeclaredNames"/>. Without this, an
        /// extension-method parameter (or operator operand) named <c>$this_</c> alongside a
        /// <c>$this</c> receiver would collide with the renamed receiver — duplicate PHP parameters
        /// (fatal parse error) for a signature, or a silently overwritten local for an operator
        /// operand assignment. PHP variable names are case-sensitive, so comparison is ordinal.
        /// </summary>
        private static string ResolveCollisionSafeThisAlias(IEnumerable<string?> otherDeclaredNames)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in otherDeclaredNames)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    taken.Add(name);
                }
            }

            var alias = GeneratedNames.ExtensionReceiverThisAlias;
            while (taken.Contains(alias))
            {
                alias += "_";
            }

            return alias;
        }

        /// <summary>
        /// PER-CS 3.0: when the parenthesized expression of a control structure spans
        /// multiple lines, the first expression must start on the line after the opening <c>(</c>.
        /// </summary>
        private static string FormatControlStructureOpen(string keyword, string inner, string suffix = " {")
        {
            var normalized = (inner ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (!normalized.Contains('\n'))
            {
                return $"{keyword} ({normalized}){suffix}";
            }

            var indented = string.Join(
                "\n",
                normalized.Split('\n').Select(line => line.Length == 0 ? "" : "    " + line));
            return $"{keyword} (\n{indented}\n){suffix}";
        }
    }
}
