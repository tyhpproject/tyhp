// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Visits an anonymous struct declaration (used with `new struct { ... }`).
        ///
        /// Grammar:
        ///   tyhpAnonymousStruct
        ///     : StructKw=T_STRING {this.isStructKeyword($StructKw)}?
        ///         (T_EXTENDS Extends=className)? (T_OPEN_ROUND_BRACE
        ///         T_CLOSE_ROUND_BRACE)? FindDocComment=T_OPEN_CURLY_BRACE
        ///         PropertyList=tyhpStructPropertyList T_CLOSE_CURLY_BRACE
        ///     ;
        /// </summary>
        public override TyhpStructDeclAst VisitTyhpAnonymousStruct([NotNull] TyhpParser.TyhpAnonymousStructContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            TyhpStructPropertyListAst propertyList;
            if (context.PropertyList != null)
            {
                propertyList = this.VisitTyhpStructPropertyList(context.PropertyList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpAnonymousStruct.PropertyList");
                propertyList = TyhpStructPropertyListAst.Create(null, context);
            }

            return TyhpStructDeclAst.Create(
                null,
                propertyList,
                context.Extends != null ? this.VisitClassName(context.Extends) : null,
                docComment,
                context
            );
        }
