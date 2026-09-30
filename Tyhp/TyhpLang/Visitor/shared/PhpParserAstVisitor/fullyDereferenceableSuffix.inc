// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IDereferenceableSuffix VisitFullyDereferenceableSuffix([NotNull] TyhpParser.FullyDereferenceableSuffixContext context)
            => context switch {
                TyhpParser.DereferenceableSuffixArrayAccessContext dereferenceableSuffixArrayAccessContext => this.VisitDereferenceableSuffixArrayAccess(dereferenceableSuffixArrayAccessContext),
                TyhpParser.DereferenceableSuffixMemberAccessContext dereferenceableSuffixMemberAccessContext => this.VisitDereferenceableSuffixMemberAccess(dereferenceableSuffixMemberAccessContext),
                TyhpParser.DereferenceableSuffixStaticMemberAccessContext dereferenceableSuffixStaticMemberAccessContext => this.VisitDereferenceableSuffixStaticMemberAccess(dereferenceableSuffixStaticMemberAccessContext),
                TyhpParser.DereferenceableSuffixClassConstantAccessContext dereferenceableSuffixClassConstantAccessContext => this.VisitDereferenceableSuffixClassConstantAccess(dereferenceableSuffixClassConstantAccessContext),
                TyhpParser.DereferenceableSuffixCallAccessContext dereferenceableSuffixCallAccessContext => this.VisitDereferenceableSuffixCallAccess(dereferenceableSuffixCallAccessContext),
                TyhpParser.DereferenceableSuffixGrammarAddonHandlerContext dereferenceableSuffixGrammarAddonHandlerContext => this.VisitDereferenceableSuffixGrammarAddonHandler(dereferenceableSuffixGrammarAddonHandlerContext),
                _ => this.VisitFullyDereferenceableSuffixAlt(context)
            };


        public virtual IDereferenceableSuffix VisitFullyDereferenceableSuffixAlt(TyhpParser.FullyDereferenceableSuffixContext context)
        {
            this.ReportUnexpectedAlternative(context, "fully-dereferenceable-suffix");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpArrayAccessAst VisitDereferenceableSuffixArrayAccess([NotNull] TyhpParser.DereferenceableSuffixArrayAccessContext context)
            => this.VisitDereferenceableArrayAccessSuffix(context.SuffixArray);


        public override PhpInstanceMemberAccessAst VisitDereferenceableSuffixMemberAccess([NotNull] TyhpParser.DereferenceableSuffixMemberAccessContext context)
            => this.VisitDereferenceableMemberAccessSuffix(context.SuffixMember);


        public override PhpStaticMemberAccessAst VisitDereferenceableSuffixStaticMemberAccess([NotNull] TyhpParser.DereferenceableSuffixStaticMemberAccessContext context)
            => this.VisitDereferenceableStaticMemberAccessSuffix(context.SuffixStaticMember);


        public override PhpClassConstantAccessAst VisitDereferenceableSuffixClassConstantAccess([NotNull] TyhpParser.DereferenceableSuffixClassConstantAccessContext context)
            => this.VisitDereferenceableClassConstantAccessSuffix(context.SuffixClassConst);


        public override PhpCallAst VisitDereferenceableSuffixCallAccess([NotNull] TyhpParser.DereferenceableSuffixCallAccessContext context)
            => this.VisitCallArgumentList(context.ArgumentList);


        public override IDereferenceableSuffix VisitDereferenceableSuffixGrammarAddonHandler([NotNull] TyhpParser.DereferenceableSuffixGrammarAddonHandlerContext context)
            => this.VisitDereferenceableSuffixGrammarAddon(context.dereferenceableSuffixGrammarAddon());
