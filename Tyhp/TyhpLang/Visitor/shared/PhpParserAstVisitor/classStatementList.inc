// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpClassBodyAst VisitClassStatementList([NotNull] TyhpParser.ClassStatementListContext context)
            => PhpClassBodyAst.Create(
                context._Items?.Select(this.VisitClassStatement),
                context
            );


        /// <summary>
        /// Visits a class/trait/interface/enum body, or reports
        /// <see cref="MessageCode.VisitorMissingRequiredNode"/> and returns an empty body when
        /// ANTLR error recovery left <c>StatementList</c> null (e.g. a reserved keyword used as a
        /// type name: <c>class Struct { … }</c>).
        /// </summary>
        protected PhpClassBodyAst VisitClassStatementListOrEmpty(
            TyhpParser.ClassStatementListContext? statementList,
            ParserRuleContext parentContext)
        {
            if (statementList is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename,
                    parentContext.Start?.Line ?? 0,
                    parentContext.Start?.Column ?? 0,
                    "classStatementList");
                return PhpClassBodyAst.Create(null, parentContext);
            }

            return this.VisitClassStatementList(statementList);
        }
