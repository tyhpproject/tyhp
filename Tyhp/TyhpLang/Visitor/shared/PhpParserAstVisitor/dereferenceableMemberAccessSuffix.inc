// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpInstanceMemberAccessAst VisitDereferenceableMemberAccessSuffix([NotNull] TyhpParser.DereferenceableMemberAccessSuffixContext context)
            => PhpInstanceMemberAccessAst.Create(
                this.GetTokenValueAst(context, context.TokenValue),
                this.VisitMemberName(context.MemberName),
                context
            );
