// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpModifierListAst VisitPropertyModifiers([NotNull] TyhpParser.PropertyModifiersContext context)
        {
            if (context.Modifiers != null)
            {
                return this.VisitNonEmptyMemberModifiers(context.Modifiers);
            }
            else if (context.IsVar != null)
            {
                return PhpModifierListAst.Create([PhpModifier.Var], context);
            }
            else if (context.ModifiersGrammarAddon != null)
            {
                return this.VisitPropertyModifiersGrammarAddon(context.ModifiersGrammarAddon);
            }
            this.ReportUnexpectedAlternative(context, "property-modifiers");
            return PhpModifierListAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
