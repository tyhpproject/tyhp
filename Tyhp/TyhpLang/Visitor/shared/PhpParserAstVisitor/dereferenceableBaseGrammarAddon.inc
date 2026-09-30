// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override IDereferenceableBase VisitDereferenceableBaseGrammarAddon([NotNull] TyhpParser.DereferenceableBaseGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "dereferenceable-base-grammar-addon", context.GetType().Name
            );
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
