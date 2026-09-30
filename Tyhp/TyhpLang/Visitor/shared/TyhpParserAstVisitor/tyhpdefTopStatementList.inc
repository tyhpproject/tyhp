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
        /// Visits a list of tyhpdef top-level statements.
        /// Grammar: tyhpdefTopStatementList
        ///   : Items+=tyhpdefTopStatement*
        /// </summary>
        public override PhpTopStatementListAst VisitTyhpdefTopStatementList([NotNull] TyhpParser.TyhpdefTopStatementListContext context)
        {
            var result = PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            if (context._Items != null)
            {
                foreach (var item in context._Items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var topStatement = this.VisitTyhpdefTopStatement(item);
                    if (topStatement != null)
                    {
                        result.Add(topStatement);
                    }
                }
            }
            return result;
        }
