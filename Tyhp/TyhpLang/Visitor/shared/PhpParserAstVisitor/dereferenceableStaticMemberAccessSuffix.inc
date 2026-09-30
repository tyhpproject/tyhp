// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpStaticMemberAccessAst VisitDereferenceableStaticMemberAccessSuffix([NotNull] TyhpParser.DereferenceableStaticMemberAccessSuffixContext context)
            => PhpStaticMemberAccessAst.Create(
                this.VisitMemberInstanceName(context.Identifier),
                context
            );
