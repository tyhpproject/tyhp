// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpModifierListAst VisitClassModifiers([NotNull] TyhpParser.ClassModifiersContext context)
        {
            var modifiers = (context._Items ?? Enumerable.Empty<TyhpParser.ClassModifierContext>())
                .Select(this.VisitClassModifier)
                .Select(x => FromToken((int)(x.ValueInt64 ?? 0)));
            return PhpModifierListAst.Create(modifiers, context);
        }
