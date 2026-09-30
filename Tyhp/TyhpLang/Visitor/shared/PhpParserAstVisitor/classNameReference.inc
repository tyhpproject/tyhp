// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override Ast.Interfaces.IClassNameReference VisitClassNameReference([NotNull] TyhpParser.ClassNameReferenceContext context)
        {
            if (context.ClassName != null)
            {
                return this.VisitClassName(context.ClassName);
            }
            if (context.NewVariable != null)
            {
                return this.VisitNewVariable(context.NewVariable)
                    .WithGrammarAddon("identifier", this.VisitClassNameIdentifierGrammarAddon(context.classNameIdentifierGrammarAddon()));
            }
            if (context.Expr != null)
            {
                return this.VisitExpr(context.Expr)
                    .WithGrammarAddon("identifier", this.VisitClassNameIdentifierGrammarAddon(context.classNameIdentifierGrammarAddon()));
            }

            this.ReportUnexpectedAlternative(context, "class-name-reference");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
