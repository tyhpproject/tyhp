// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        /// <summary>
        /// Preserve Tyhp <c>async</c> / <c>internal</c> on member modifier lists. PHP's
        /// <see cref="PhpModifier"/> has no Async value historically, so the base visitor
        /// mapped that token to <see cref="PhpModifier.None"/>. Attach grammar addons so the
        /// binder can set <c>IsAsync</c> / <c>IsInternal</c> / the matching <see cref="MemberModifier"/> flags.
        /// </summary>
        public override PhpModifierListAst VisitNonEmptyMemberModifiers([NotNull] TyhpParser.NonEmptyMemberModifiersContext context)
        {
            var result = base.VisitNonEmptyMemberModifiers(context);
            foreach (var item in context._Items)
            {
                var token = this.VisitMemberModifier(item);
                if (string.Equals(token.ValueString, "async", StringComparison.OrdinalIgnoreCase)
                    || token.ValueInt64 == TyhpParser.T_TYHP_ASYNC)
                {
                    result.AddGrammarAddon("isAsync", token);
                }
                else if (string.Equals(token.ValueString, "internal", StringComparison.OrdinalIgnoreCase)
                    || token.ValueInt64 == TyhpParser.T_TYHP_INTERNAL)
                {
                    result.AddGrammarAddon("isInternal", token);
                }
            }

            return result;
        }
