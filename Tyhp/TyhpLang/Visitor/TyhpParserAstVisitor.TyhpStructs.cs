namespace Tyhp.TyhpLang.Visitor
{
    using System.Linq;
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Visits a named struct declaration statement.
        ///
        /// Grammar:
        ///   tyhpStructDeclarationStatement
        ///     : T_TYHP_STRUCT Identifier=T_STRING
        ///         GenericParameters=tyhpGenericParameterDeclarations?
        ///         (T_EXTENDS Extends=className)?
        ///         FindDocComment=T_OPEN_CURLY_BRACE PropertyList=tyhpStructPropertyList
        ///         T_CLOSE_CURLY_BRACE
        ///     ;
        /// </summary>
        public override TyhpStructDeclAst VisitTyhpStructDeclarationStatement([NotNull] TyhpParser.TyhpStructDeclarationStatementContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            IBase2Ast? genericParameters = context.GenericParameters != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericParameters)
                : null;

            // Truncated `struct Foo` at EOF leaves PropertyList (and sometimes Identifier) null.
            string? name;
            if (context.Identifier != null)
            {
                name = context.Identifier.Text;
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpStructDeclarationStatement.Identifier");
                name = "<error>";
            }

            TyhpStructPropertyListAst propertyList;
            if (context.PropertyList != null)
            {
                propertyList = this.VisitTyhpStructPropertyList(context.PropertyList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpStructDeclarationStatement.PropertyList");
                propertyList = TyhpStructPropertyListAst.Create(null, context);
            }

            return TyhpStructDeclAst.Create(
                name,
                propertyList,
                context.Extends != null ? this.VisitClassName(context.Extends) : null,
                docComment,
                context
            ).WithGrammarAddon("identifier", genericParameters);
        }

        /// <summary>
        /// Visits an anonymous struct declaration (used with `new struct { ... }`).
        ///
        /// Grammar:
        ///   tyhpAnonymousStruct
        ///     : T_TYHP_STRUCT (T_EXTENDS Extends=className)? (T_OPEN_ROUND_BRACE
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

        /// <summary>
        /// Visits a single struct property declaration.
        ///
        /// Grammar:
        ///   tyhpStructProperty
        ///     : TypeExpr=typeExprWithoutStatic
        ///         ((AliasOfString=T_CONSTANT_ENCAPSED_STRING | AliasOfInt=T_LNUMBER) T_AS)?
        ///         Property=property T_SYM_SEMICOLON
        ///     ;
        /// </summary>
        public override TyhpStructPropertyAst VisitTyhpStructProperty([NotNull] TyhpParser.TyhpStructPropertyContext context)
        {
            string? aliasOf = null;
            var aliasIsNumeric = false;
            if (context.AliasOfString != null)
            {
                aliasOf = context.AliasOfString.Text;
            }
            else if (context.AliasOfInt != null)
            {
                aliasOf = context.AliasOfInt.Text;
                aliasIsNumeric = true;
            }

            return TyhpStructPropertyAst.Create(
                this.VisitTypeExprWithoutStatic(context.TypeExpr),
                this.VisitProperty(context.Property),
                aliasOf,
                aliasIsNumeric,
                context
            );
        }

        /// <summary>
        /// Visits a list of struct property declarations.
        ///
        /// Grammar:
        ///   tyhpStructPropertyList
        ///     : Items+=tyhpStructProperty*
        ///     ;
        /// </summary>
        public override TyhpStructPropertyListAst VisitTyhpStructPropertyList([NotNull] TyhpParser.TyhpStructPropertyListContext context)
            => TyhpStructPropertyListAst.Create(
                (context._Items ?? Enumerable.Empty<TyhpParser.TyhpStructPropertyContext>())
                    .Select(this.VisitTyhpStructProperty),
                context
            );
    }
}
