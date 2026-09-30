// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using System.Linq
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visit an individual resource declaration within a using block.
        /// Pattern-matches on the labeled alternative subclass types generated
        /// by ANTLR for the tyhpUsingResource rule.
        /// </summary>
        private TyhpUsingResourceAst VisitTyhpUsingResource(
            [NotNull] TyhpParser.TyhpUsingResourceContext context)
        {
            return context switch
            {
                TyhpParser.TyhpUsingResourceTypedContext typed =>
                    TyhpUsingResourceAst.Create(
                        this.VisitTypeExprWithoutStatic(typed.TypeExpr),
                        this.VisitSimpleVariable(typed.Variable),
                        this.VisitExpr(typed.Expr) as IExpression
                            ?? throw new InvalidOperationException(
                                $"Using resource expression at {typed.Expr?.Start?.Line}:{typed.Expr?.Start?.Column} did not produce an IExpression"),
                        typed, GetCurrentLanguageMode(context)),

                TyhpParser.TyhpUsingResourceInferredContext inferred =>
                    TyhpUsingResourceAst.Create(
                        null,
                        this.VisitSimpleVariable(inferred.Variable),
                        this.VisitExpr(inferred.Expr) as IExpression
                            ?? throw new InvalidOperationException(
                                $"Using resource expression at {inferred.Expr?.Start?.Line}:{inferred.Expr?.Start?.Column} did not produce an IExpression"),
                        inferred, GetCurrentLanguageMode(context)),

                TyhpParser.TyhpUsingResourceUnassignedContext unassigned =>
                    TyhpUsingResourceAst.Create(
                        null, null,
                        this.VisitExpr(unassigned.Expr) as IExpression
                            ?? throw new InvalidOperationException(
                                $"Using resource expression at {unassigned.Expr?.Start?.Line}:{unassigned.Expr?.Start?.Column} did not produce an IExpression"),
                        unassigned, GetCurrentLanguageMode(context)),

                _ => throw new InvalidOperationException(
                    $"Unexpected TyhpUsingResource alternative: {context.GetType().Name}")
            };
        }
