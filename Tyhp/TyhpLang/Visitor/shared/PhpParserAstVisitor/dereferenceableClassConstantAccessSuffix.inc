// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpClassConstantAccessAst VisitDereferenceableClassConstantAccessSuffix([NotNull] TyhpParser.DereferenceableClassConstantAccessSuffixContext context)
            => PhpClassConstantAccessAst.Create(
                this.VisitMemberConstantName(context.Identifier),
                context
            );
