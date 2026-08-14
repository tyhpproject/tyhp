namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override PhpParameterListAst? VisitParameterList([NotNull] TyhpParser.ParameterListContext context)
        {
            // Error recovery can invoke this with a null labeled child parent; never NRE.
            if (context == null)
            {
                return null;
            }

            return context.ParameterList != null ? this.VisitNonEmptyParameterList(context.ParameterList) : null;
        }

        public override PhpParameterListAst? VisitCtorParameterList([NotNull] TyhpParser.CtorParameterListContext context)
            => context.ParameterList != null ? this.VisitNonEmptyCtorParameterList(context.ParameterList) : null;

        public override PhpParameterListAst VisitNonEmptyParameterList([NotNull] TyhpParser.NonEmptyParameterListContext context)
            => PhpParameterListAst.Create(
                context._Items.Select(this.VisitAttributedParameter),
                context
            );

        public override PhpParameterListAst VisitNonEmptyCtorParameterList([NotNull] TyhpParser.NonEmptyCtorParameterListContext context)
            => PhpParameterListAst.Create(
                context._Items.Select(this.VisitAttributedCtorParameter),
                context
            );

        public override PhpParameterAst VisitAttributedParameter([NotNull] TyhpParser.AttributedParameterContext context)
            => this.VisitParameter(context.Parameter)
                .WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);

        public override PhpParameterAst VisitAttributedCtorParameter([NotNull] TyhpParser.AttributedCtorParameterContext context)
        {
            var parameter = this.VisitParameter(context.Parameter);
            return PhpParameterAst.Create(
                parameter.Name,
                parameter.Type,
                parameter.IsRef,
                parameter.IsVariadic,
                parameter.DefaultValue,
                this.VisitOptionalCppModifiers(context.Modifiers),
                this.VisitOptionalPropertyHookList(context.Accessors),
                context
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }

        public override PhpModifierListAst? VisitOptionalCppModifiers([NotNull] TyhpParser.OptionalCppModifiersContext context)
            => context.nonEmptyMemberModifiers() != null ? this.VisitNonEmptyMemberModifiers(context.nonEmptyMemberModifiers()) : null;

        public override PhpParameterAst VisitParameter([NotNull] TyhpParser.ParameterContext context)
        {
            // Truncated parameter lists (e.g. `operator }` / `extension operator }`) can leave a
            // ParameterContext with a null Variable after ANTLR recovery. Visiting Variable.Text
            // unconditionally aborts with TYHP1003 instead of a normal parse diagnostic.
            if (context.Variable == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename,
                    context.Start?.Line ?? 0,
                    context.Start?.Column ?? 0,
                    "parameter.Variable");
                return PhpParameterAst.Create(
                    "<error>",
                    null,
                    false,
                    false,
                    null,
                    null,
                    null,
                    context);
            }

            return PhpParameterAst.Create(
                context.Variable.Text,
                context.TypeExpr != null
                    ? this.VisitParameterTypeExpressionGrammarAddon(context.TypeExpr)
                    : null,
                context.IsRef?.TokenValue != null,
                context.IsVariadic?.TokenValue != null,
                context.ValueExpr != null ? this.VisitExpr(context.ValueExpr) : null,
                null,
                null,
                context
            );
        }

        public override ITypeExpression? VisitParameterTypeExpressionGrammarAddon([NotNull] TyhpParser.ParameterTypeExpressionGrammarAddonContext context)
        {
            if (context.optionalTypeWithoutStatic() is { } optionalType)
            {
                return this.VisitOptionalTypeWithoutStatic(optionalType);
            }

            return null;
        }

        public override PhpArgumentListAst? VisitArgumentList([NotNull] TyhpParser.ArgumentListContext context)
        {
            if (context.ArgumentList != null)
            {
                return this.VisitNonEmptyArgumentList(context.ArgumentList);
            }
            else if (context.Ellipsis != null)
            {
                // Variadic argument list with ellipsis only
                return PhpArgumentListAst.Create(
                    [PhpArgumentAst.Create(null, null, true, context)],
                    context
                );
            }
            
            return null;
        }

        public override PhpArgumentListAst VisitNonEmptyArgumentList([NotNull] TyhpParser.NonEmptyArgumentListContext context)
            => PhpArgumentListAst.Create(
                context._Items.Select(this.VisitArgument),
                context
            );

        public override PhpArgumentAst VisitArgument([NotNull] TyhpParser.ArgumentContext context)
        {
            if (context.IsVariadic?.TokenValue != null && context.Expr != null)
            {
                // Variadic argument: ...expr
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    null,
                    true,
                    context
                );
            }
            else if (context.Identifier != null && context.Expr != null)
            {
                // Named argument: name: expr
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    this.VisitIdentifier(context.Identifier),
                    false,
                    context
                );
            }
            else if (context.Expr != null)
            {
                // Regular argument
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    context
                );
            }

            return PhpArgumentAst.Create(UnexpectedNodeAst.Create(context), context, GetCurrentLanguageMode(context));
        }

        public override PhpArgumentListAst VisitCloneArgumentList([NotNull] TyhpParser.CloneArgumentListContext context)
        {
            if (context.ArgumentList != null)
            {
                return this.VisitNonEmptyCloneArgumentList(context.ArgumentList);
            }

            if (context.Expr != null)
            {
                // clone($x,) — trailing comma disambiguates from unary clone($x)
                return PhpArgumentListAst.Create(
                    [PhpArgumentAst.Create(this.VisitExpr(context.Expr), context)],
                    context
                );
            }

            // clone()
            return PhpArgumentListAst.Create([], context);
        }

        public override PhpArgumentListAst VisitNonEmptyCloneArgumentList([NotNull] TyhpParser.NonEmptyCloneArgumentListContext context)
        {
            var args = new List<PhpArgumentAst>();

            if (context.FirstExpr != null && context.FirstArg != null)
            {
                args.Add(PhpArgumentAst.Create(this.VisitExpr(context.FirstExpr), context));
                args.Add(this.VisitArgument(context.FirstArg));
            }
            else if (context.FirstNoExpr != null)
            {
                args.Add(this.VisitCloneArgumentNoExpr(context.FirstNoExpr));
            }

            foreach (var rest in context._Rest)
            {
                args.Add(this.VisitArgument(rest));
            }

            return PhpArgumentListAst.Create(args, context);
        }

        public override PhpArgumentAst VisitCloneArgumentNoExpr([NotNull] TyhpParser.CloneArgumentNoExprContext context)
        {
            if (context.Identifier != null && context.Expr != null)
            {
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    this.VisitIdentifier(context.Identifier),
                    false,
                    context
                );
            }

            if (context.IsVariadic != null && context.Expr != null)
            {
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    null,
                    true,
                    context
                );
            }

            // First-class callable: clone(...)
            return PhpArgumentAst.Create(null, null, true, context);
        }

        public override PhpVariableListAst VisitGlobalVarList([NotNull] TyhpParser.GlobalVarListContext context)
            => PhpVariableListAst.Create(
                context._Items.Select(this.VisitGlobalVar),
                context
            );

        public override PhpVariableAst VisitGlobalVar([NotNull] TyhpParser.GlobalVarContext context)
            => this.VisitSimpleVariable(context.Variable);

        public override PhpVariableListAst VisitStaticVarList([NotNull] TyhpParser.StaticVarListContext context)
            => PhpVariableListAst.Create(
                context._Items.Select(this.VisitStaticVar),
                context
            );

        public override PhpVariableAst VisitStaticVar([NotNull] TyhpParser.StaticVarContext context)
            => PhpVariableAst.Create(
                this.GetTokenValueAst(context, context.Variable),
                null,
                false,
                context.Expr != null ? this.VisitExpr(context.Expr) : null,
                context
            );
    }
}