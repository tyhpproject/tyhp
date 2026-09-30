// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        private IExtensionMemberAst VisitTyhpExtensionGroupMemberAsExtensionMember(
            TyhpParser.TyhpExtensionGroupMemberContext context)
        {
            return context switch
            {
                TyhpParser.TyhpExtensionGroupCallableMemberContext ctx =>
                    this.VisitExtensionCallableMember(ctx.Member, context),
                TyhpParser.TyhpExtensionGroupOperatorMemberContext ctx =>
                    this.VisitExtensionOperatorMember(ctx.Attributes, ctx.OperatorOverload, context),
                _ => this.HandleUnexpectedAlternativeSpecial<IExtensionMemberAst>(
                    context,
                    "tyhpExtensionGroupMember",
                    () => ErrorAst.Create(context, GetCurrentLanguageMode(context)))
            };
        }
