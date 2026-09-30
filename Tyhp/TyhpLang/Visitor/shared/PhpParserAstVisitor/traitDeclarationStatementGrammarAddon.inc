// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpObjectTypeDeclAst VisitTraitDeclarationStatementGrammarAddon([NotNull] TyhpParser.TraitDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "trait-declaration-statement-grammar-addon", context.GetType().Name
            );
            return PhpObjectTypeDeclAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
