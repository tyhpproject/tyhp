// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpParameterAst VisitAttributedCtorParameter([NotNull] TyhpParser.AttributedCtorParameterContext context)
        {
            var parameter = this.VisitParameter(context.Parameter);
            return PhpParameterAst.Create(
                parameter.Name,
                parameter.Type,
                parameter.IsRef,
                parameter.IsVariadic,
                parameter.DefaultValue,
                this.VisitOptionalCppModifiers(context.Modifiers),
                this.VisitOptionalPropertyHookList(context.Accessors),
                context
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }
