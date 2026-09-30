// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IExpression VisitMemberConstantName([NotNull] TyhpParser.MemberConstantNameContext context)
            => context switch {
                TyhpParser.MemberNameIdentifierContext memberNameIdentifierContext => this.VisitMemberNameIdentifier(memberNameIdentifierContext),
                TyhpParser.MemberNameExprContext memberNameExprContext => this.VisitMemberNameExpr(memberNameExprContext),
                _ => this.VisitMemberConstantNameAlt(context),
            };


        public virtual IExpression VisitMemberConstantNameAlt(TyhpParser.MemberConstantNameContext context)
        {
            this.ReportUnexpectedAlternative(context, "member-constant-name");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override IExpression VisitMemberNameIdentifier([NotNull] TyhpParser.MemberNameIdentifierContext context)
            => this.VisitIdentifier(context.Identifier)
                .WithGrammarAddon("memberName", this.VisitMemberNameIdentifierGrammarAddon(context.memberNameIdentifierGrammarAddon()));


        public override IExpression VisitMemberNameExpr([NotNull] TyhpParser.MemberNameExprContext context)
            => this.VisitExpr(context.Expr)
                .WithGrammarAddon("memberName", this.VisitMemberNameIdentifierGrammarAddon(context.memberNameIdentifierGrammarAddon()));
