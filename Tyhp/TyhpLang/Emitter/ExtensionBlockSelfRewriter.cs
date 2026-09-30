using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Rewrites extension-member <c>self</c> / <c>self::</c> / <c>new self()</c> to the block
    /// target's PHP spelling before call-site splice. Checker and binder <c>self</c> stay the
    /// target; only the name text that PHP will see changes. <c>self</c> inside a nested
    /// object declaration is left alone (it means that object). <c>static</c> and <c>parent</c>
    /// are not rewritten.
    /// </summary>
    internal static class ExtensionBlockSelfRewriter
    {
        public static void Rewrite(IEnumerable<SrcFileAst> files, EmitContext context)
        {
            foreach (var file in files)
            {
                AstWalker.Walk(file, node =>
                {
                    if (node is TyhpExtensionDeclAst extension)
                    {
                        RewriteExtension(extension, context);
                    }
                });
            }
        }

        private static void RewriteExtension(TyhpExtensionDeclAst extension, EmitContext context)
        {
            if (extension.BoundSymbol is not ObjectDeclarationSymbol block
                || block.ExtensionBlockTargetSymbol is null
                || block.PendingExtensionBlockTarget is null)
            {
                return;
            }

            var spelling = TypeSpellingHelper.Spell(
                block.PendingExtensionBlockTarget,
                context.TypeAliasMap,
                context.GlobalScope,
                context.Config.NamespacePrefix);
            if (string.IsNullOrWhiteSpace(spelling)
                || IsSelfKeyword(spelling))
            {
                return;
            }

            foreach (var member in extension.FunctionList?.GetAllNotNull() ?? [])
            {
                // Nested groups carry their own target. The file walk rewrites each group
                // when it visits that declaration.
                if (member is TyhpExtensionDeclAst)
                {
                    continue;
                }

                RewriteMember(member, spelling);
            }
        }

        private static void RewriteMember(IBase2Ast member, string spelling)
        {
            Walk(member, spelling, []);
        }

        private static void Walk(IBase2Ast node, string spelling, HashSet<IBase2Ast> visited)
        {
            if (!visited.Add(node) || node is not Base2Ast parent)
            {
                return;
            }

            // A nested class, interface, trait, enum, or anonymous class has its own `self`.
            if (node is PhpObjectTypeDeclAst)
            {
                return;
            }

            foreach (var child in parent.AstChildren.ToList())
            {
                if (child is null)
                {
                    continue;
                }

                if (child is PhpNameAst name && IsSelfKeyword(name.ValueString ?? name.Identifier))
                {
                    var replacement = RewriteName(name, spelling);
                    parent.ReplaceChild(name, replacement);
                    if (replacement is TyhpGenericIdentifierAst { GenericArguments: IBase2Ast args })
                    {
                        Walk(args, spelling, visited);
                    }

                    continue;
                }

                Walk(child, spelling, visited);
            }

            foreach (var pair in parent.AstGrammarAddons.ToList())
            {
                if (pair.Value is PhpNameAst name && IsSelfKeyword(name.ValueString ?? name.Identifier))
                {
                    var replacement = RewriteName(name, spelling);
                    parent.AddGrammarAddon(pair.Key, replacement);
                    if (replacement is TyhpGenericIdentifierAst { GenericArguments: IBase2Ast args })
                    {
                        Walk(args, spelling, visited);
                    }

                    continue;
                }

                Walk(pair.Value, spelling, visited);
            }
        }

        private static PhpNameAst RewriteName(PhpNameAst name, string spelling)
        {
            PhpNameAst replacement = name is TyhpGenericIdentifierAst generic
                ? TyhpGenericIdentifierAst.CreateFromContext(spelling, generic.GenericArguments, name)
                : PhpNameAst.CreateFromContext(spelling, name);
            // Builtin and struct spellings (`string`, `array`) must not be re-resolved
            // through a class BoundSymbol into an FQN. Class spellings already contain `\`.
            replacement.BoundSymbol = spelling.Contains('\\') ? name.BoundSymbol : null;
            replacement.OwningFile = name.OwningFile;
            replacement.OriginalAst = name.OriginalAst ?? name;
            foreach (var pair in name.AstGrammarAddons)
            {
                replacement.AddGrammarAddon(pair.Key, pair.Value);
            }

            return replacement;
        }

        private static bool IsSelfKeyword(string? name) =>
            string.Equals(name, "self", StringComparison.OrdinalIgnoreCase);
    }
}
