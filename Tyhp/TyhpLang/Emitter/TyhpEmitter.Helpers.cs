using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private const string OutputFileDeclareKey = "output_file";
        private const string AutoloadDeclareKey = "autoload";

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
        /// Formats attributes for inline use on a property hook
        /// (<c>#[Attr] get { … }</c>).
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
                if (attributeNode is PhpAttributeAst attribute)
                {
                    lines.Add(this.FormatAttributeLine(attribute));
                }
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
                if (attributeNode is not PhpAttributeAst attribute)
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

            var parts = new List<string>();
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
                        parts.Add(modifier.ToString().ToLowerInvariant());
                        break;
                    case PhpModifier.PublicSet:
                        parts.Add("public(set)");
                        break;
                    case PhpModifier.ProtectedSet:
                        parts.Add("protected(set)");
                        break;
                    case PhpModifier.PrivateSet:
                        parts.Add("private(set)");
                        break;
                }
            }

            return parts.Count == 0 ? "" : string.Join(" ", parts) + " ";
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
                if (child is PhpInlineFunctionAst)
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

            return token.ValueInt64 == TyhpParser.T_TYHP_USING_EQUAL
                || PhpAssignmentOperatorExtensions.FromToken((int)token.ValueInt64) == PhpAssignmentOperator.UsingEqual;
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
            || string.Equals(identifier, AutoloadDeclareKey, StringComparison.OrdinalIgnoreCase);

        // `is`/`isa`/`isan`/`is_a`/`is_an` are Tyhp source spellings that alias `instanceof`
        // (Tyhp/TyhpLang/Grammar/TyhpParser.g4 phpExprBinaryOpGrammarAddon002; PhpBinaryOperator.cs maps
        // T_TYHP_IS to PhpBinaryOperator.InstanceOf for checker/binder purposes). Emitting the raw
        // token text here would put the literal word `is` into the output PHP, which PHP does not
        // recognize as an operator at all — every use of the alias produced a guaranteed parse
        // error in the emitted file. Normalize to the one spelling PHP understands.
        private string GetOperatorText(TokenValueAst? op)
        {
            if (op is null)
            {
                return "";
            }

            if (op.ValueInt64 == TyhpParser.T_TYHP_IS)
            {
                return "instanceof";
            }

            return op.ValueString ?? "";
        }

        /// <summary>
        /// True for PHP <c>instanceof</c> and the Tyhp <c>is</c>/<c>isa</c>/<c>isan</c>/
        /// <c>is_a</c>/<c>is_an</c> aliases (all map to the same binary operator).
        /// </summary>
        private static bool IsInstanceofLikeOperator(PhpBinaryOpAst binary)
        {
            if (binary.Operator?.ValueInt64 == TyhpParser.T_TYHP_IS
                || binary.Operator?.ValueInt64 == TyhpParser.T_INSTANCEOF)
            {
                return true;
            }

            var opText = binary.Operator?.ValueString;
            return string.Equals(opText, "instanceof", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_an", StringComparison.OrdinalIgnoreCase);
        }

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
    }
}
