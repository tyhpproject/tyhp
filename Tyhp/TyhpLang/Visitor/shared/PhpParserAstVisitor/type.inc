// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override ITypeExpression VisitType([NotNull] TyhpParser.TypeContext context)
        {
            if (context.BaseType != null)
            {
                return this.VisitTypeWithoutStatic(context.BaseType);
            }
            else if (context.StaticType != null)
            {
                return PhpBuiltinTypeAst.Create("static", context)
                    .WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(context.typeNameGrammarAddon()));
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "type");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }
