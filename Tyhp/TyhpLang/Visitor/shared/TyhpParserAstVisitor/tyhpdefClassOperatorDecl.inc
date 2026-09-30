// #class TyhpParserAstVisitor
// #using System
// #using System.Collections.Generic
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Visits a tyhpdef class operator overload.
        /// Grammar: tyhpdefClassOperator T_SYM_SEMICOLON   #tyhpdefClassOperatorDecl
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpdefClassOperatorDecl([NotNull] TyhpParser.TyhpdefClassOperatorDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var opCtx = context.tyhpdefClassOperator();
            if (opCtx == null)
            {
                this.ReportMissingRequired(context, "tyhpdefClassOperatorDecl.tyhpdefClassOperator");
                return TyhpOperatorOverloadAst.CreateError(context, languageMode);
            }

            TokenValueAst op;
            if (opCtx.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(opCtx.Op);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpdefClassOperator.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            PhpParameterAst leftParam;
            if (opCtx.LeftParameter != null)
            {
                leftParam = this.VisitAttributedParameter(opCtx.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpdefClassOperator.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = opCtx.RightParameter != null
                ? this.VisitAttributedParameter(opCtx.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (opCtx.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(opCtx.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpdefClassOperator.ConvertReturnType");
            }

            return TyhpOperatorOverloadAst.Create(
                op, leftParam, rightParam, returnType, null, null,
                context, languageMode
            );
        }
