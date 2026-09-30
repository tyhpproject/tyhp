// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpModifierListAst? VisitOptionalCppModifiers([NotNull] TyhpParser.OptionalCppModifiersContext context)
            => context.nonEmptyMemberModifiers() != null ? this.VisitNonEmptyMemberModifiers(context.nonEmptyMemberModifiers()) : null;
