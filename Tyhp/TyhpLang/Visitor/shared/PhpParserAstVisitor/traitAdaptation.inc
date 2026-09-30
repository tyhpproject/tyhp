// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override ITraitAdaptation VisitTraitAdaptation([NotNull] TyhpParser.TraitAdaptationContext context)
        {
            if (context.Precedence != null)
            {
                return this.VisitTraitPrecedence(context.Precedence);
            }
            else if (context.Alias != null)
            {
                return this.VisitTraitAlias(context.Alias);
            }
            this.ReportUnexpectedAlternative(context, "trait-adaptation");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
