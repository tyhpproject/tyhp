// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst VisitReservedNonModifiersWithoutConstructor([NotNull] TyhpParser.ReservedNonModifiersWithoutConstructorContext context)
        {
            var result = context.RNM != null ?
                this.VisitReservedNonModifiersBase(context.RNM) :
                this.VisitReservedNonModifiersGrammarAddon(context.RNMGrammarAddon);
            if (result == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "reserved-non-modifiers-without-constructor", context.GetType().Name
                );
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return result;
        }
