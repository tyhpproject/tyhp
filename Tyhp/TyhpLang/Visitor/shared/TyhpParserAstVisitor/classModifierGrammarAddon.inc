// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override TokenValueAst VisitClassModifierGrammarAddon([NotNull] TyhpParser.ClassModifierGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue)
                ?? TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
