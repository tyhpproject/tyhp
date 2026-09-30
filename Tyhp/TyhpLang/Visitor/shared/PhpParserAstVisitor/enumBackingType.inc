// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override ITypeExpression? VisitEnumBackingType([NotNull] TyhpParser.EnumBackingTypeContext context)
            => context.TypeExpr != null ? this.VisitTypeExpr(context.TypeExpr) as ITypeExpression : null;
