// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpTypeExpressionAst VisitTypeExprWithoutStatic([NotNull] TyhpParser.TypeExprWithoutStaticContext context)
        {
            if (context.CallableType != null)
            {
                return PhpTypeExpressionAst.Create(
                    PhpTypeExpressionListAst.Create(
                        [this.VisitCallableType(context.CallableType) as ITypeExpression],
                        context),
                    PhpTypeKind.Simple,
                    context.IsNullable != null,
                    false,
                    context
                );
            }
            else if (context.IsNullable != null && context.BaseType != null)
            {
                return this.VisitNullablePrefixedType(this.VisitTypeWithoutStatic(context.BaseType), context);
            }
            else if (context.UnionType != null)
            {
                return this.VisitUnionTypeWithoutStatic(context.UnionType);
            }
            else if (context.IntersectionType != null)
            {
                return this.VisitIntersectionTypeWithoutStatic(context.IntersectionType);
            }
            else if (context.BaseType != null)
            {
                return PhpTypeExpressionAst.Create(
                    PhpTypeExpressionListAst.Create([this.VisitTypeWithoutStatic(context.BaseType)], context),
                    PhpTypeKind.Simple,
                    false,
                    false,
                    context
                );
            }
            else if (context.typeExprWithoutStaticGrammarAddon() is { } grammarAddon)
            {
                return this.VisitTypeExprWithoutStaticGrammarAddon(grammarAddon);
            }
            else
            {
                // Same truncated-union/intersection recovery shape as VisitTypeExpr above.
                this.ReportUnexpectedAlternative(context, "typeExprWithoutStatic");
                return PhpTypeExpressionAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }
