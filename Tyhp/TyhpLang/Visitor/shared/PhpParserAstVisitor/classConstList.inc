// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpConstDeclListAst VisitClassConstList([NotNull] TyhpParser.ClassConstListContext context)
            => this.VisitClassConstList(context, modifiers: null, type: null);


        protected PhpConstDeclListAst VisitClassConstList(
            [NotNull] TyhpParser.ClassConstListContext context,
            PhpModifierListAst? modifiers,
            ITypeExpression? type = null)
            => PhpConstDeclListAst.Create(
                context._Items.Select(item => this.VisitClassConstDecl(item, modifiers, type)),
                context
            );
