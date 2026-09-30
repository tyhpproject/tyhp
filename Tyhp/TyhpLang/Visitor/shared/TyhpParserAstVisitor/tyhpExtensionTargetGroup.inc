// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits a nested <c>extends Type { }</c> group as an extension member.
        /// </summary>
        public override TyhpExtensionDeclAst VisitTyhpExtensionTargetGroup(
            [NotNull] TyhpParser.TyhpExtensionTargetGroupContext context)
            => this.CreateExtensionTargetGroup(
                context,
                context.GenericParameters,
                context.TargetType,
                context.FindDocComment,
                context.FunctionList != null
                    ? this.VisitTyhpExtensionGroupMemberList(context.FunctionList)
                    : null);


        private IExtensionMemberAst VisitExtensionTargetGroupMember(
            TyhpParser.TyhpExtensionTargetGroupContext? group,
            ParserRuleContext owner)
        {
            if (group == null)
            {
                this.ReportMissingRequired(owner, "tyhpExtensionTargetGroup");
                return TyhpExtensionDeclAst.CreateError(owner, GetCurrentLanguageMode(owner));
            }

            return this.VisitTyhpExtensionTargetGroup(group);
        }
