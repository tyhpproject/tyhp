namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;

    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override ITypeExpression? VisitOptionalTypeWithoutStatic([NotNull] TyhpParser.OptionalTypeWithoutStaticContext context)
            => context.TypeExpr != null ? this.VisitTypeExprWithoutStatic(context.TypeExpr) : null;

        public override PhpTypeExpressionAst VisitTypeExpr([NotNull] TyhpParser.TypeExprContext context)
        {
            if (context.IsNullable != null && context.BaseType != null)
            {
                return PhpTypeExpressionAst.Create(
                    PhpTypeExpressionListAst.Create([this.VisitType(context.BaseType)], context),
                    PhpTypeKind.Simple,
                    true,
                    false,
                    context
                );
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

        public override PhpTypeExpressionAst VisitTypeExprGrammarAddon([NotNull] TyhpParser.TypeExprGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "typeExprGrammarAddon");
            return PhpTypeExpressionAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override ITypeExpression VisitType([NotNull] TyhpParser.TypeContext context)
        {
            if (context.BaseType != null)
            {
                return this.VisitTypeWithoutStatic(context.BaseType);
            }
            else if (context.StaticType != null)
            {
                return PhpBuiltinTypeAst.Create("static", context)
                    .WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(context.typeNameGrammarAddon()));
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "type");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }

        public override ITypeExpression VisitUnionTypeElement([NotNull] TyhpParser.UnionTypeElementContext context)
        {
            if (context.BaseType != null)
            {
                return this.VisitType(context.BaseType);
            }
            else if (context.IntersectionType != null)
            {
                return this.VisitIntersectionType(context.IntersectionType);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "unionTypeElement");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }

        public override PhpTypeExpressionAst VisitUnionType([NotNull] TyhpParser.UnionTypeContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitUnionTypeElement),
                    context
                ),
                PhpTypeKind.Union,
                false,
                false,
                context
            );

        public override PhpTypeExpressionAst VisitIntersectionType([NotNull] TyhpParser.IntersectionTypeContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitType),
                    context
                ),
                PhpTypeKind.Intersection,
                false,
                false,
                context
            );

        public override PhpTypeExpressionAst VisitTypeExprWithoutStatic([NotNull] TyhpParser.TypeExprWithoutStaticContext context)
        {
            if (context.IsNullable != null && context.BaseType != null)
            {
                return PhpTypeExpressionAst.Create(
                    PhpTypeExpressionListAst.Create([this.VisitTypeWithoutStatic(context.BaseType)], context),
                    PhpTypeKind.Simple,
                    true,
                    false,
                    context
                );
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

        public override PhpTypeExpressionAst VisitTypeExprWithoutStaticGrammarAddon([NotNull] TyhpParser.TypeExprWithoutStaticGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "typeExprWithoutStaticGrammarAddon");
            return PhpTypeExpressionAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override ITypeExpression VisitTypeWithoutStatic([NotNull] TyhpParser.TypeWithoutStaticContext context)
        {
            if (context.ArrayType != null)
            {
                return PhpBuiltinTypeAst.Create("array", context)
                    .WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(context.typeNameGrammarAddon()));
            }
            else if (context.CallableType != null)
            {
                return PhpBuiltinTypeAst.Create("callable", context)
                    .WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(context.typeNameGrammarAddon()));
            }
            else if (context.Identifier != null)
            {
                return PhpNamedTypeAst.Create(this.VisitName(context.Identifier), context)
                    .WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(context.typeNameGrammarAddon()));
            }
            else if (context.typeWithoutStaticGrammarAddon() is { } grammarAddon)
            {
                return this.VisitTypeWithoutStaticGrammarAddon(grammarAddon);
            }
            else
            {
                // Truncated nullable types (`?` alone in a parameter/return slot) leave every
                // field above null, including the GrammarAddon child. Visiting null would NRE in
                // Tyhp's ScalarType override (TYHP1003); recover with a typed error AST instead.
                this.ReportUnexpectedAlternative(context, "typeWithoutStatic");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }

        public override ITypeExpression VisitTypeWithoutStaticGrammarAddon([NotNull] TyhpParser.TypeWithoutStaticGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "typeWithoutStaticGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IBase2Ast? VisitTypeNameGrammarAddon([NotNull] TyhpParser.TypeNameGrammarAddonContext context)
            => null;

        public override Ast.Interfaces.ITypeExpression VisitUnionTypeWithoutStaticElement([NotNull] TyhpParser.UnionTypeWithoutStaticElementContext context)
        {
            if (context.BaseType != null)
            {
                return this.VisitTypeWithoutStatic(context.BaseType);
            }
            else if (context.IntersectionType != null)
            {
                return this.VisitIntersectionTypeWithoutStatic(context.IntersectionType);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "unionTypeWithoutStaticElement");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }

        public override PhpTypeExpressionAst VisitUnionTypeWithoutStatic([NotNull] TyhpParser.UnionTypeWithoutStaticContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitUnionTypeWithoutStaticElement),
                    context
                ),
                PhpTypeKind.Union,
                false,
                false,
                context
            );

        public override PhpTypeExpressionAst VisitIntersectionTypeWithoutStatic([NotNull] TyhpParser.IntersectionTypeWithoutStaticContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitTypeWithoutStatic),
                    context
                ),
                PhpTypeKind.Intersection,
                false,
                false,
                context
            );

        public ITypeExpression? VisitReturnType([NotNull] TyhpParser.ReturnTypeContext context)
        {
            // Error recovery can leave returnType null on the parent; callers should guard, but
            // also tolerate a null context here so malformed tyhpdef cannot NRE mid-visit.
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.ReturnTypeTypeContext returnTypeTypeContext => this.VisitReturnTypeType(returnTypeTypeContext),
                TyhpParser.ReturnTypeGrammarAddonHandlerContext returnTypeGrammarAddonHandlerContext => this.VisitReturnTypeGrammarAddonHandler(returnTypeGrammarAddonHandlerContext),
                _ => this.VisitReturnTypeAlternate(context),
            };
        }

        public virtual ITypeExpression? VisitReturnTypeAlternate([NotNull] TyhpParser.ReturnTypeContext context)
        {
            this.ReportUnexpectedAlternative(context, "returnTypeAlternate");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override ITypeExpression? VisitReturnTypeType([NotNull] TyhpParser.ReturnTypeTypeContext context)
            => context.TypeExpr != null ? this.VisitTypeExpr(context.TypeExpr) : null;

        public override ITypeExpression VisitReturnTypeGrammarAddonHandler([NotNull] TyhpParser.ReturnTypeGrammarAddonHandlerContext context)
        {
            var addon = context.returnTypeGrammarAddon();
            if (addon == null)
            {
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return this.VisitReturnTypeGrammarAddon(addon);
        }

        public ITypeExpression VisitReturnTypeGrammarAddon([NotNull] TyhpParser.ReturnTypeGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "returnTypeGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
    }
}