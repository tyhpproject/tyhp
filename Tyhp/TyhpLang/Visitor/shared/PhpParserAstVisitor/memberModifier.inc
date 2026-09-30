// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override TokenValueAst VisitMemberModifier([NotNull] TyhpParser.MemberModifierContext context)
        {
            if (context.TokenValueGrammarAddon != null)
            {
                return this.VisitMemberModifierGrammarAddon(context.TokenValueGrammarAddon);
            }

            if (context.TokenValue is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "memberModifier");
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return TokenValueAst.Create(context.TokenValue, context);
        }
