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
        /// Visits a single tyhpdef property (variable name plus optional expected default).
        /// Grammar: tyhpdefProperty
        ///   : Variable=T_VARIABLE (T_COALESCE CoalesceExpr=expr)?
        /// </summary>
        public override TyhpdefPropertyAst VisitTyhpdefProperty([NotNull] TyhpParser.TyhpdefPropertyContext context)
        {
            if (context.Variable == null)
            {
                this.ReportMissingRequired(context, "tyhpdefProperty.Variable");
                return TyhpdefPropertyAst.Create("<error>", null, context, GetCurrentLanguageMode(context));
            }

            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;

            return TyhpdefPropertyAst.Create(
                context.Variable.Text,
                coalesceExpr,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef property list.
        /// Grammar: tyhpdefPropertyList
        ///   : Items+=tyhpdefProperty (T_SYM_COMMA Items+=tyhpdefProperty)*
        /// </summary>
        public override TyhpdefPropertyListAst VisitTyhpdefPropertyList([NotNull] TyhpParser.TyhpdefPropertyListContext context)
            => TyhpdefPropertyListAst.Create(
                (context._Items ?? []).Where(item => item != null).Select(this.VisitTyhpdefProperty!),
                context
            );
