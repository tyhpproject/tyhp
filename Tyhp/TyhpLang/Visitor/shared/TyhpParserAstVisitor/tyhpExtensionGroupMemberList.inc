// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TyhpExtensionFunctionListAst VisitTyhpExtensionGroupMemberList(
            [NotNull] TyhpParser.TyhpExtensionGroupMemberListContext context)
            => TyhpExtensionFunctionListAst.Create(
                (context.tyhpExtensionGroupMember() ?? [])
                    .Select(this.VisitTyhpExtensionGroupMemberAsExtensionMember),
                context);
