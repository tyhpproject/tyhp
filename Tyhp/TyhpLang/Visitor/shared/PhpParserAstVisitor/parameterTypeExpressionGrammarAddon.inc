// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override ITypeExpression? VisitParameterTypeExpressionGrammarAddon([NotNull] TyhpParser.ParameterTypeExpressionGrammarAddonContext context)
        {
            if (context.optionalTypeWithoutStatic() is { } optionalType)
            {
                return this.VisitOptionalTypeWithoutStatic(optionalType);
            }

            return null;
        }
