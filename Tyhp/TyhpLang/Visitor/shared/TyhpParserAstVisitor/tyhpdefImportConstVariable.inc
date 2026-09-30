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
        /// Visits a tyhpdef import const (dispatches to import const statement).
        /// Grammar: Statement=tyhpdefImportConstStatement   #tyhpdefImportConst
        /// </summary>
        public override TyhpdefImportConstAst VisitTyhpdefImportConst([NotNull] TyhpParser.TyhpdefImportConstContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportConst.Statement");
                return TyhpdefImportConstAst.Create(
                    ErrorAst.Create(context, GetCurrentLanguageMode(context)),
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    null, false, false, null,
                    context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefImportConstStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef import variable (dispatches to import variable statement).
        /// Grammar: Statement=tyhpdefImportVariableStatement   #tyhpdefImportVariable
        /// </summary>
        public override TyhpdefImportVariableAst VisitTyhpdefImportVariable([NotNull] TyhpParser.TyhpdefImportVariableContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportVariable.Statement");
                return TyhpdefImportVariableAst.Create(
                    ErrorAst.Create(context, GetCurrentLanguageMode(context)),
                    "<error>", null, null, false, false, null,
                    context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefImportVariableStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef constant import statement.
        /// Grammar: tyhpdefImportConstStatement
        ///   : tyhpdefDeprecatedOrObsolete? IsFallback=T_TYHP_FALLBACK? T_CONST TypeExpr=typeExprWithoutStatic
        ///       (AliasedIdentifier=tyhpdefIdentifierWithOptionalAlias | Identifier=name)
        ///       (T_COALESCE CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportConstAst VisitTyhpdefImportConstStatement([NotNull] TyhpParser.TyhpdefImportConstStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportConstStatement.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            IBase2Ast nameOrAlias;
            if (context.AliasedIdentifier != null)
            {
                nameOrAlias = this.VisitTyhpdefIdentifierWithOptionalAlias(context.AliasedIdentifier);
            }
            else if (context.Identifier != null)
            {
                nameOrAlias = this.VisitName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportConstStatement.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;
            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            var constDecl = TyhpdefImportConstAst.Create(
                typeExpr, nameOrAlias, coalesceExpr,
                isDeprecated, isObsolete, docComment,
                context, GetCurrentLanguageMode(context),
                isOmit: isOmit,
                isFallback: context.IsFallback != null);
            this.AttachOverlayAgainst(constDecl, context);
            return constDecl;
        }

        /// <summary>
        /// Visits a tyhpdef variable import statement.
        /// Grammar: tyhpdefImportVariableStatement
        ///   : tyhpdefDeprecatedOrObsolete? TypeExpr=typeExprWithoutStatic
        ///       Variable=T_VARIABLE (T_AS AliasedAs=T_VARIABLE)? (T_COALESCE
        ///       CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportVariableAst VisitTyhpdefImportVariableStatement([NotNull] TyhpParser.TyhpdefImportVariableStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportVariableStatement.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            if (context.Variable == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportVariableStatement.Variable");
                return TyhpdefImportVariableAst.Create(
                    typeExpr, "<error>", null, null,
                    isDeprecated, isObsolete, null,
                    context, GetCurrentLanguageMode(context));
            }

            var variableName = context.Variable.Text;
            var aliasedAs = context.AliasedAs?.Text;
            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;
            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            var variableDecl = TyhpdefImportVariableAst.Create(
                typeExpr, variableName, aliasedAs, coalesceExpr,
                isDeprecated, isObsolete, docComment,
                context, GetCurrentLanguageMode(context),
                isOmit: isOmit);
            this.AttachOverlayAgainst(variableDecl, context);
            return variableDecl;
        }
