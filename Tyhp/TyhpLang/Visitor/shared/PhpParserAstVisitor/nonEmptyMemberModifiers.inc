// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpModifierListAst VisitNonEmptyMemberModifiers([NotNull] TyhpParser.NonEmptyMemberModifiersContext context)
        {
            var modifiers = (context._Items ?? Enumerable.Empty<TyhpParser.MemberModifierContext>())
                .Select(this.VisitMemberModifier)
                .Select(x => FromToken((int)(x.ValueInt64 ?? 0)));
            return PhpModifierListAst.Create(modifiers, context);
        }
