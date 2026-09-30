// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public IAttributedStatement VisitAttributedStatement([NotNull] TyhpParser.AttributedStatementContext context)
            => context switch {
                TyhpParser.FunctionDeclStatementContext funcCtx => this.VisitFunctionDeclStatement(funcCtx),
                TyhpParser.ClassDeclStatementContext classCtx => this.VisitClassDeclStatement(classCtx),
                TyhpParser.TraitDeclStatementContext traitCtx => this.VisitTraitDeclStatement(traitCtx),
                TyhpParser.InterfaceDeclStatementContext interfaceCtx => this.VisitInterfaceDeclStatement(interfaceCtx),
                TyhpParser.EnumDeclStatementContext enumCtx => this.VisitEnumDeclStatement(enumCtx),
                _ => VisitAttributedStatementAlt(context)
            };


        public virtual IAttributedStatement VisitAttributedStatementAlt([NotNull] TyhpParser.AttributedStatementContext context)
            => (this.Visit(context) as IAttributedStatement) ?? UnexpectedNodeAst.Create(context);


        public override PhpFunctionDeclAst VisitFunctionDeclStatement([NotNull] TyhpParser.FunctionDeclStatementContext context)
            => this.VisitFunctionDeclarationStatement(context.Statement);


        public override PhpObjectTypeDeclAst VisitClassDeclStatement([NotNull] TyhpParser.ClassDeclStatementContext context)
            => this.VisitClassDeclarationStatement(context.Statement);


        public override PhpObjectTypeDeclAst VisitTraitDeclStatement([NotNull] TyhpParser.TraitDeclStatementContext context)
            => this.VisitTraitDeclarationStatement(context.Statement);


        public override PhpObjectTypeDeclAst VisitInterfaceDeclStatement([NotNull] TyhpParser.InterfaceDeclStatementContext context)
            => this.VisitInterfaceDeclarationStatement(context.Statement);


        public override PhpObjectTypeDeclAst VisitEnumDeclStatement([NotNull] TyhpParser.EnumDeclStatementContext context)
            => this.VisitEnumDeclarationStatement(context.Statement);
