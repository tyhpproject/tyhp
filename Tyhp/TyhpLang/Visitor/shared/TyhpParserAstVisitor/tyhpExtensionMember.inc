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
        /// Dispatches <c>tyhpExtensionMember</c> labeled alternatives (no unlabeled base
        /// rule visitor is generated since every alternative carries a label).
        /// Function / <c>fn</c> members use method-style names via
        /// <c>tyhpOptionalGenericIdentifierWithoutConstructor</c>.
        /// Copies member <c>#[…]</c> onto the function / operator the same way
        /// <c>VisitTyhpdefStandaloneExtensionFunctionDecl</c> does.
        /// </summary>
        private IExtensionMemberAst VisitTyhpExtensionMemberAsExtensionMember(TyhpParser.TyhpExtensionMemberContext context)
        {
            return context switch
            {
                TyhpParser.TyhpExtensionCallableMemberAltContext ctx =>
                    this.VisitExtensionCallableMember(ctx.Member, context),
                TyhpParser.TyhpExtensionOperatorMemberContext ctx => this.VisitTyhpExtensionOperatorMember(ctx),
                TyhpParser.TyhpExtensionTargetGroupMemberContext ctx =>
                    this.VisitExtensionTargetGroupMember(ctx.TargetGroup, context),
                _ => this.HandleUnexpectedAlternativeSpecial<IExtensionMemberAst>(
                    context,
                    "tyhpExtensionMember",
                    () => ErrorAst.Create(context, GetCurrentLanguageMode(context)))
            };
        }


        /// <summary>
        /// Grammar: #tyhpExtensionOperatorMember — attributes wrapping
        /// <c>tyhpExtensionOperatorOverload</c>.
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpExtensionOperatorMember(
            [NotNull] TyhpParser.TyhpExtensionOperatorMemberContext context)
        {
            var attributes = context.Attributes != null
                ? this.VisitAttributes(context.Attributes)
                : null;

            TyhpParser.TyhpExtensionOperatorOverloadContext? opOverload = context.tyhpExtensionOperatorOverload();
            if (opOverload == null)
            {
                this.ReportMissingRequired(context, "tyhpExtensionOperatorMember.tyhpExtensionOperatorOverload");
                return TyhpOperatorOverloadAst.CreateError(context, GetCurrentLanguageMode(context))
                    .WithAttributes(attributes);
            }

            return this.VisitTyhpExtensionOperatorOverload(opOverload).WithAttributes(attributes);
        }
