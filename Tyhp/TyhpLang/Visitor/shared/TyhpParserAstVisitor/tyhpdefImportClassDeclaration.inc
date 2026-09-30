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
        /// Dispatches to class declaration statement.
        /// Grammar: Statement=tyhpdefImportClassDeclarationStatement   #tyhpdefImportClassDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportClassDecl([NotNull] TyhpParser.TyhpdefImportClassDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "class");
            }

            return this.VisitTyhpdefImportClassDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef class import declaration.
        /// Grammar: tyhpdefImportClassDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? Modifiers=classModifiers? T_CLASS
        ///       Identifier=tyhpdefClassNameWithOptionalAlias Extends=extendsFrom
        ///       Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        ///       StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportClassDeclarationStatement([NotNull] TyhpParser.TyhpdefImportClassDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

            var modifiers = context.Modifiers != null
                ? this.VisitClassModifiers(context.Modifiers)
                : null;

            PhpNameAst identifier;
            if (context.Identifier != null)
            {
                identifier = this.VisitTyhpdefClassNameWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassDeclarationStatement.Identifier");
                identifier = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var statementList = VisitTyhpdefObjectBodyOrHeaderOnly(
                context.StatementList,
                context.T_SYM_SEMICOLON(),
                context);

            var classDecl = TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("class", TyhpParser.T_CLASS, context),
                modifiers,
                identifier,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context),
                isPartial: context.IsPartial != null,
                isOmit: isOmit);
            this.AttachOverlayAgainst(classDecl, context);
            return classDecl;
        }
