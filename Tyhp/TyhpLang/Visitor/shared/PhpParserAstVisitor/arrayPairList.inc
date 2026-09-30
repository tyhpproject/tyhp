// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpArrayPairListAst VisitArrayPairList([NotNull] TyhpParser.ArrayPairListContext context)
            => PhpArrayPairListAst.Create(
                context._Items.Select(this.VisitPossibleArrayPair),
                context
            );
