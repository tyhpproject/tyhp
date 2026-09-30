// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpCallAst VisitCallArgumentList([NotNull] TyhpParser.CallArgumentListContext context)
            => PhpCallAst.Create(
                context.ArgumentList != null ?
                    this.VisitArgumentList(context.ArgumentList) ?? PhpArgumentListAst.Create([], context) :
                    PhpArgumentListAst.Create([], context),
                context
            ).WithGrammarAddon("functionCall", this.VisitFunctionCallGrammarAddon(context.functionCallGrammarAddon()));
