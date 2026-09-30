// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableAst VisitSimpleVariableGrammarAddon([NotNull] TyhpParser.SimpleVariableGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "simple-variable-grammar-addon", context.GetType().Name
            );
            return PhpVariableAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
