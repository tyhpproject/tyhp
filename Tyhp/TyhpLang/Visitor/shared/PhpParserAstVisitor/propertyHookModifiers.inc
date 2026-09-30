// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpModifierListAst? VisitPropertyHookModifiers([NotNull] TyhpParser.PropertyHookModifiersContext context)
        {
            if (context.Modifiers != null)
            {
                return this.VisitNonEmptyMemberModifiers(context.Modifiers);
            }
            else if (context.ModifiersGrammarAddon != null)
            {
                return this.VisitPropertyHookModifiersGrammarAddon(context.ModifiersGrammarAddon);
            }
            return null;
        }
