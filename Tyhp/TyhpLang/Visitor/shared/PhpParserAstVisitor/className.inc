// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpNameAst VisitClassName([NotNull] TyhpParser.ClassNameContext context)
        {
            PhpNameAst result;
            if (context.Identifier != null)
            {
                result = this.VisitName(context.Identifier);
            }
            else if (context.IsStatic != null)
            {
                result = PhpNameAst.Create(context.IsStatic, context);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "class-name");
                result = PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return result.WithGrammarAddon("identifier", this.VisitClassNameIdentifierGrammarAddon(context.classNameIdentifierGrammarAddon()));
        }
