// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpConstDeclListAst VisitConstList([NotNull] TyhpParser.ConstListContext context)
            => PhpConstDeclListAst.Create(
                context._Items.Select(this.VisitConstDecl),
                context
            );
