// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public virtual PhpTraitMemberRefAst VisitAbsoluteTraitMethodReferenceGrammarAddon(
            [NotNull] TyhpParser.AbsoluteTraitMethodReferenceGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "absoluteTraitMethodReferenceGrammarAddon", context.GetType().Name
            );
            return PhpTraitMemberRefAst.Create(
                PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context)),
                context,
                PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
