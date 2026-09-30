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
        /// Visits a tyhpdef import class constant.
        /// Grammar: tyhpdefDeprecatedOrObsolete? Modifiers=methodModifiers T_CONST
        ///   TypeExpr=typeExprWithoutStatic ConstList=tyhpdefImportClassConstList T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportConstDeclListAst VisitTyhpdefImportClassConst([NotNull] TyhpParser.TyhpdefImportClassConstContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var modifiers = context.Modifiers != null
                ? this.VisitMethodModifiers(context.Modifiers)
                : null;

            ITypeExpression? typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassConst.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            TyhpdefImportConstDeclListAst constList;
            if (context.ConstList != null)
            {
                constList = this.VisitTyhpdefImportClassConstList(context.ConstList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassConst.ConstList");
                constList = TyhpdefImportConstDeclListAst.Create(null, context);
            }

            if (modifiers != null) constList.AddGrammarAddon("modifiers", modifiers);
            if (typeExpr != null) constList.AddGrammarAddon("typeExpr", typeExpr);
            if (deprecatedOrObsolete != null) constList.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            if (context.Attributes != null)
            {
                constList.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return constList;
        }

        /// <summary>
        /// Visits a tyhpdef import class const declaration.
        /// Grammar: tyhpdefImportClassConstDecl
        ///   : Identifier=tyhpdefIdentifierWithOptionalAlias (T_COALESCE CoalesceExpr=expr)?
        /// </summary>
        public override TyhpdefImportConstDeclAst VisitTyhpdefImportClassConstDecl([NotNull] TyhpParser.TyhpdefImportClassConstDeclContext context)
        {
            var aliasedIdentifier = this.VisitTyhpdefIdentifierWithOptionalAlias(context.Identifier);
            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;

            return TyhpdefImportConstDeclAst.Create(
                aliasedIdentifier,
                coalesceExpr,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef import class const list.
        /// Grammar: tyhpdefImportClassConstList
        ///   : Items+=tyhpdefImportClassConstDecl (T_SYM_COMMA Items+=tyhpdefImportClassConstDecl)*
        /// </summary>
        public override TyhpdefImportConstDeclListAst VisitTyhpdefImportClassConstList([NotNull] TyhpParser.TyhpdefImportClassConstListContext context)
            => TyhpdefImportConstDeclListAst.Create(
                context._Items.Select(this.VisitTyhpdefImportClassConstDecl),
                context
            );
