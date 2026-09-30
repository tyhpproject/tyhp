// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpExpressionListAst VisitEchoExprList([NotNull] TyhpParser.EchoExprListContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(this.VisitEchoExpr),
                context
            );
