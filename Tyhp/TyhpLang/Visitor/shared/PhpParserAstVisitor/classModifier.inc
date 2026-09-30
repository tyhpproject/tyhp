// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override TokenValueAst VisitClassModifier([NotNull] TyhpParser.ClassModifierContext context)
        {
            if (context.classModifierGrammarAddon() != null)
            {
                return this.VisitClassModifierGrammarAddon(context.classModifierGrammarAddon());
            }

            if (context.TokenValue is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "classModifier");
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return TokenValueAst.Create(context.TokenValue, context);
        }
