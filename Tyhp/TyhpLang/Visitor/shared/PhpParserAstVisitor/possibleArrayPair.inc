// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpArrayPairAst? VisitPossibleArrayPair([NotNull] TyhpParser.PossibleArrayPairContext context)
            => context.ArrayPair != null
                ? this.VisitArrayPair(context.ArrayPair)
                : PhpArrayPairAst.CreateSkippedSlot(
                    context,
                    PhpParserAstVisitor.GetCurrentLanguageMode(context));
