// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpParameterAst VisitAttributedParameter([NotNull] TyhpParser.AttributedParameterContext context)
            => this.VisitParameter(context.Parameter)
                .WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
