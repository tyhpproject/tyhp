// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Tree
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableAst VisitNewVariableSimple([NotNull] TyhpParser.NewVariableSimpleContext context)
            => this.VisitSimpleVariable(context.Variable);


        public override PhpMemberAccessAst VisitNewVariableStaticProperty([NotNull] TyhpParser.NewVariableStaticPropertyContext context)
            => PhpMemberAccessAst.Create(
                context.ClassName != null
                    ? this.VisitClassName(context.ClassName)
                    : this.VisitNewVariable(context.NewVariable) as IExpression,
                PhpNameAst.Create("::", TyhpParser.T_DOUBLE_COLON, context),
                this.VisitSimpleVariable(context.Identifier),
                context
            );


        public override PhpMemberAccessAst VisitNewVariableArrayIndex([NotNull] TyhpParser.NewVariableArrayIndexContext context)
            => PhpMemberAccessAst.Create(
                this.VisitNewVariable(context.NewVariable),
                PhpNameAst.Create(context.TokenValue, context),
                this.VisitOptionalExpr(context.OptionalExpr),
                context
            );


        public override PhpMemberAccessAst VisitNewVariableProperty([NotNull] TyhpParser.NewVariablePropertyContext context)
            => PhpMemberAccessAst.Create(
                this.VisitNewVariable(context.NewVariable),
                PhpNameAst.Create(context.TokenValue, context),
                this.VisitMemberName(context.MemberName),
                context
            );


        public Ast.Interfaces.IExpression VisitNewVariable([NotNull] TyhpParser.NewVariableContext context)
            => context switch {
                TyhpParser.NewVariableSimpleContext newVariableSimpleContext => this.VisitNewVariableSimple(newVariableSimpleContext),
                TyhpParser.NewVariableArrayIndexContext newVariableArrayIndexContext => this.VisitNewVariableArrayIndex(newVariableArrayIndexContext),
                TyhpParser.NewVariablePropertyContext newVariablePropertyContext => this.VisitNewVariableProperty(newVariablePropertyContext),
                TyhpParser.NewVariableStaticPropertyContext newVariableStaticPropertyContext => this.VisitNewVariableStaticProperty(newVariableStaticPropertyContext),
                _ => this.VisitNewVariableAlt(context),
            };


        public virtual Ast.Interfaces.IExpression VisitNewVariableAlt(TyhpParser.NewVariableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-variable");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
