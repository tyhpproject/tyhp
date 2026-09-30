// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IExpression VisitMemberName([NotNull] TyhpParser.MemberNameContext context)
            => context switch {
                TyhpParser.MemberNameConstantContext memberNameConstantContext => this.VisitMemberNameConstant(memberNameConstantContext),
                TyhpParser.MemberNameInstanceContext memberNameInstanceContext => this.VisitMemberNameInstance(memberNameInstanceContext),
                _ => this.VisitMemberNameAlt(context),
            };


        public virtual IExpression VisitMemberNameAlt(TyhpParser.MemberNameContext context)
        {
            this.ReportUnexpectedAlternative(context, "member-name");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override IExpression VisitMemberNameConstant([NotNull] TyhpParser.MemberNameConstantContext context)
            => this.VisitMemberConstantName(context.Identifier);


        public override IExpression VisitMemberNameInstance([NotNull] TyhpParser.MemberNameInstanceContext context)
            => this.VisitMemberInstanceName(context.Identifier);
