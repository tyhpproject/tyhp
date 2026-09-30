// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpExpressionListAst VisitUnsetVariables([NotNull] TyhpParser.UnsetVariablesContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(item =>
                    this.VisitUnsetVariable(item) as IExpression
                    ?? PhpVariableAst.CreateError(item, GetCurrentLanguageMode(item))),
                context
            );
