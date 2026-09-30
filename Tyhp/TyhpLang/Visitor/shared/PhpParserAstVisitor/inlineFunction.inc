// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpInlineFunctionAst VisitInlineFunction([NotNull] TyhpParser.InlineFunctionContext context)
            => PhpInlineFunctionAst.Create(
                TokenValueListAst.Create([
                    .. this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon())?.GetAll() ?? [],
                    .. new TokenValueAst?[] { context.IsStatic != null ? this.GetTokenValueAst(context, context.IsStatic) : null }.Where(x => x != null).Cast<TokenValueAst>(),
                ], context),
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()),
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitLexicalVars(context.LexicalVars),
                this.VisitInnerStatementList(context.StatementList),
                context
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null)
            .WithGrammarAddon("modifiers", this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon()))
            .WithGrammarAddon("identifier", this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()));
