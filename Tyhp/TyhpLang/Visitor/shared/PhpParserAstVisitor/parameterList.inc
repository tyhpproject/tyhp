// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        public override PhpParameterListAst? VisitParameterList([NotNull] TyhpParser.ParameterListContext context)
        {
            // Error recovery can invoke this with a null labeled child parent; never NRE.
            if (context == null)
            {
                return null;
            }

            return context.ParameterList != null ? this.VisitNonEmptyParameterList(context.ParameterList) : null;
        }
