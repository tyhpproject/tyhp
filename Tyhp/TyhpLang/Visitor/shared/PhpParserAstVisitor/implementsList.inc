// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpClassNameListAst VisitImplementsList([NotNull] TyhpParser.ImplementsListContext context)
            => context.ClassNameList != null ? this.VisitClassNameList(context.ClassNameList) : PhpClassNameListAst.Create([], context);
