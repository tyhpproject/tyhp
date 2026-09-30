// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IExpression VisitMemberInstanceName([NotNull] TyhpParser.MemberInstanceNameContext context)
            => context switch {
                TyhpParser.MemberNameSimpleContext memberNameSimpleContext => this.VisitMemberNameSimple(memberNameSimpleContext),
                _ => this.VisitMemberInstanceNameAlt(context),
            };


        public virtual IExpression VisitMemberInstanceNameAlt(TyhpParser.MemberInstanceNameContext context)
        {
            this.ReportUnexpectedAlternative(context, "member-instance-name");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override IExpression VisitMemberNameSimple([NotNull] TyhpParser.MemberNameSimpleContext context)
            => this.VisitSimpleVariable(context.Identifier)
                .WithGrammarAddon("memberName", this.VisitMemberNameIdentifierGrammarAddon(context.memberNameIdentifierGrammarAddon()));
