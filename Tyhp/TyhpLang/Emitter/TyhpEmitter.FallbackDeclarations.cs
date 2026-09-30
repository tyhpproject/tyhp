using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        /// <summary>
        /// Declarations currently being emitted inside their own conditional wrapper. The wrapper
        /// re-enters <see cref="EmitNode"/> for the declaration itself; membership here keeps that
        /// inner call from wrapping again.
        /// </summary>
        private readonly HashSet<IBase2Ast> _fallbackWrapped = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Wraps a declaration in the runtime checks it needs. Outermost first: the
        /// <c>\PHP_VERSION_ID</c> / <c>\extension_loaded</c> checks from enclosing
        /// <c>declare(php=…)</c> / <c>declare(ext=…)</c> blocks and <c>#[\Tyhp\Php]</c>, then, for a
        /// <c>fallback</c> declaration, the same existence check that a hand-written
        /// <c>if (!\function_exists(...))</c> gate produces (see <c>DeclarationExistenceGateHelper</c>):
        /// <c>function</c> / <c>class</c> / <c>interface</c> / <c>trait</c> / <c>enum</c> use the
        /// matching <c>*_exists</c> call; <c>const</c> uses <c>\defined</c> / <c>\define</c>.
        /// </summary>
        private bool TryEmitConditionalDeclaration(IBase2Ast node, EmitItem parent, out EmitItem emitted)
        {
            emitted = null!;
            if (this._fallbackWrapped.Contains(node))
            {
                return false;
            }

            if (node is PhpConstDeclListAst constList)
            {
                var constConditions = RuntimeGateEmission.GetConditions(constList, this._context.Config.TargetPhpVersion);
                var isFallbackConst = FallbackDeclaration.IsFallback(constList);
                if ((!isFallbackConst && constConditions.Count == 0)
                    || !ShouldEmitPhpVersionGatedDeclaration(constList))
                {
                    return false;
                }

                emitted = this.EmitConditionalConstList(constList, parent, constConditions, isFallbackConst);
                return true;
            }

            var (kind, name) = node switch
            {
                PhpFunctionDeclAst function => ("function", function.Identifier ?? ""),
                PhpObjectTypeDeclAst { IsAnonymousClass: false } objectDecl => (
                    objectDecl.DeclType?.ValueString?.ToLowerInvariant() ?? "class",
                    objectDecl.Identifier ?? ""),
                _ => ("", ""),
            };

            if (kind.Length == 0 || name.Length == 0)
            {
                return false;
            }

            var conditions = new List<string>(RuntimeGateEmission.GetConditions(node, this._context.Config.TargetPhpVersion));
            if (FallbackDeclaration.IsFallback(node))
            {
                conditions.Add($"!\\{kind}_exists(__NAMESPACE__ . '\\{name}')");
            }

            if (conditions.Count == 0)
            {
                return false;
            }

            // An inactive `declare(php=…)` / NoEmit declaration emits nothing; do not leave an
            // empty `if` behind.
            if (!ShouldEmitPhpVersionGatedDeclaration(node) || NoEmitAttributeSupport.ShouldOmitDeclaration(node))
            {
                return false;
            }

            this._fallbackWrapped.Add(node);
            try
            {
                emitted = this.EmitNestedConditions(node, parent, EmitType.RootStatement, conditions, 0, block => this.EmitNode(node, block));
            }
            finally
            {
                this._fallbackWrapped.Remove(node);
            }

            return true;
        }

        /// <summary>
        /// Emits <c>if (c0) { if (c1) { … } }</c>, one block per condition, calling
        /// <paramref name="emitInner"/> inside the innermost.
        /// </summary>
        private EmitItem EmitNestedConditions(
            IBase2Ast provider,
            EmitItem parent,
            EmitType emitType,
            IReadOnlyList<string> conditions,
            int index,
            Action<EmitItem> emitInner)
        {
            var segments = new List<(string Open, Action<EmitItem> Body)>
            {
                (
                    FormatControlStructureOpen("if", conditions[index]),
                    block =>
                    {
                        if (index + 1 < conditions.Count)
                        {
                            this.EmitNestedConditions(provider, block, EmitType.SubBlockStatement, conditions, index + 1, emitInner);
                        }
                        else
                        {
                            emitInner(block);
                        }
                    }
                ),
            };
            return this.EmitBraceSegments(provider, parent, emitType, segments);
        }

        /// <summary>
        /// A <c>const</c> that sits behind runtime checks and/or <c>fallback</c>. PHP has no conditional
        /// <c>const</c>, so each declarator emits as <c>\define(NAME, value)</c> inside the checks (with
        /// <c>!\defined(NAME)</c> innermost for <c>fallback</c>). The name has no leading backslash in
        /// the global namespace because <c>\define('\NAME', …)</c> would register a constant that plain
        /// <c>NAME</c> cannot find.
        /// </summary>
        private EmitItem EmitConditionalConstList(
            PhpConstDeclListAst constList,
            EmitItem parent,
            IReadOnlyList<string> runtimeConditions,
            bool isFallback)
        {
            var currentNamespace = this._context.CurrentOutputFile?.FileNameSpace switch
            {
                PhpNamespaceDeclAst ns => ns.Identifier,
                PhpBlockNamespaceDeclAst block => block.Identifier,
                _ => null,
            };

            // File-level attributes on a const are native only from PHP 8.5, and `\define` cannot
            // carry them at all.
            this.ReportStrippedAttributes(constList, "constant", requiredPhpVersion: "8.5");

            EmitItem? first = null;
            foreach (var decl in constList.GetAllNotNull())
            {
                var identifier = decl.Identifier ?? "";
                if (identifier.Length == 0)
                {
                    continue;
                }

                var nameArgument = string.IsNullOrWhiteSpace(currentNamespace)
                    ? $"'{identifier}'"
                    : $"__NAMESPACE__ . '\\{identifier}'";
                var value = this.BuildExpression(decl.Value);

                var conditions = new List<string>(runtimeConditions);
                if (isFallback)
                {
                    conditions.Add($"!\\defined({nameArgument})");
                }

                var item = this.ApplyDocComment(
                    decl,
                    this.EmitNestedConditions(
                        decl,
                        parent,
                        EmitType.RootStatement,
                        conditions,
                        0,
                        block => EmitItem.Line(
                            decl,
                            EmitType.SubBlockStatement,
                            $"\\define({nameArgument}, {value});",
                            block)));
                first ??= item;
            }

            return first ?? EmitItem.Empty(constList, EmitType.RootStatement, parent);
        }
    }
}
