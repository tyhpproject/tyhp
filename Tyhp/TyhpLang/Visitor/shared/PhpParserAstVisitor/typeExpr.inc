// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpTypeExpressionAst VisitTypeExpr([NotNull] TyhpParser.TypeExprContext context)
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
                return this.VisitNullablePrefixedType(this.VisitType(context.BaseType), context);
            }
            else if (context.UnionType != null)
            {
                return this.VisitUnionType(context.UnionType);
            }
            else if (context.IntersectionType != null)
            {
                return this.VisitIntersectionType(context.IntersectionType);
            }
            else if (context.BaseType != null)
            {
                return PhpTypeExpressionAst.Create(
                    PhpTypeExpressionListAst.Create([this.VisitType(context.BaseType)], context),
                    PhpTypeKind.Simple,
                    false,
                    false,
                    context
                );
            }
            else if (context.typeExprGrammarAddon() is { } grammarAddon)
            {
                return this.VisitTypeExprGrammarAddon(grammarAddon);
            }
            else
            {
                // Truncated unions/intersections (e.g. `Foo|` before a `{`) leave every field above
                // null, including the GrammarAddon child — this alternative only exists as an ANTLR
                // recovery fallback (typeExprGrammarAddon is unreachable in real Tyhp/PHP syntax; see
                // TyhpParser.g4). Report on `context` itself rather than visiting a null child.
                this.ReportUnexpectedAlternative(context, "typeExpr");
                return PhpTypeExpressionAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }


        /// <summary>
        /// <c>?T</c> stays a nullable simple type. A grouped union or intersection
        /// (<c>?(A|B)</c>, <c>?(A&amp;B)</c>) is that compound type with nullability
        /// set, so the prefix applies to the whole group.
        /// </summary>
        private PhpTypeExpressionAst VisitNullablePrefixedType(
            ITypeExpression inner,
            Antlr4.Runtime.ParserRuleContext context)
        {
            if (inner is PhpTypeExpressionAst grouped
                && grouped.TypeKind is PhpTypeKind.Union or PhpTypeKind.Intersection
                && grouped.Types is { } members)
            {
                return PhpTypeExpressionAst.Create(
                    PhpTypeExpressionListAst.Create(members.GetAllNotNull(), context),
                    grouped.TypeKind,
                    true,
                    grouped.IsStatic,
                    context);
            }

            return PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create([inner], context),
                PhpTypeKind.Simple,
                true,
                false,
                context);
        }
