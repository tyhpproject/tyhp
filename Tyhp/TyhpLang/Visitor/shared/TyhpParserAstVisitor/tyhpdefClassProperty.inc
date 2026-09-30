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
        /// Visits a tyhpdef class property declaration.
        /// Grammar: tyhpdefDeprecatedOrObsolete? Modifiers=propertyModifiers
        ///   TypeExpr=typeExprWithoutStatic PropertyList=tyhpdefPropertyList T_SYM_SEMICOLON
        /// Each tyhpdefProperty may take optional (T_COALESCE CoalesceExpr=expr).
        /// </summary>
        public override PhpPropertyDeclAst VisitTyhpdefClassProperty([NotNull] TyhpParser.TyhpdefClassPropertyContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassProperty.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            var propertyList = context.PropertyList != null
                ? this.VisitTyhpdefPropertyList(context.PropertyList)
                : TyhpdefPropertyListAst.Create(null, context);

            var languageMode = GetCurrentLanguageMode(context);
            var result = PhpPropertyDeclAst.Create(
                context.Modifiers != null ? this.VisitPropertyModifiers(context.Modifiers) : null,
                typeExpr,
                // Wrap tyhpdef property list as PhpPropertyListAst for compatibility.
                // CoalesceExpr (?? expected default) maps onto PhpPropertyAst.DefaultValue
                // the same way PHP `= expr` does, so binder/IDE already see it.
                PhpPropertyListAst.Create(
                    propertyList.GetAllNotNull()
                        .Select(p => PhpPropertyAst.Create(
                            p.VariableName,
                            p.CoalesceExpr,
                            null,
                            null,
                            context,
                            languageMode)),
                    context
                ),
                context,
                languageMode
            );

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }

        /// <summary>
        /// Visits a tyhpdef hooked class property (<c>#tyhpdefClassPropertyAccessors</c>).
        /// Mirrors <see cref="PhpParserAstVisitor.VisitClassPropertyAccessors"/>: one
        /// <see cref="PhpPropertyAst"/> whose <see cref="PhpPropertyAst.Hooks"/> is a
        /// <see cref="PhpPropertyHookListAst"/>. Each hook is bodyless
        /// (<c>PhpPropertyHookAst.Create(..., body: null, ...)</c>), matching PHP interface <c>get;</c>.
        /// </summary>
        public override PhpPropertyDeclAst VisitTyhpdefClassPropertyAccessors([NotNull] TyhpParser.TyhpdefClassPropertyAccessorsContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassPropertyAccessors.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            PhpPropertyAst hookedProperty;
            if (context.PropertyAccessors != null)
            {
                hookedProperty = this.VisitTyhpdefHookedProperty(context.PropertyAccessors);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassPropertyAccessors.PropertyAccessors");
                hookedProperty = PhpPropertyAst.Create("<error>", null, null, null, context, GetCurrentLanguageMode(context));
            }

            var result = PhpPropertyDeclAst.Create(
                context.Modifiers != null ? this.VisitPropertyModifiers(context.Modifiers) : null,
                typeExpr,
                PhpPropertyListAst.Create([hookedProperty], context),
                context,
                GetCurrentLanguageMode(context)
            );

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }

        /// <summary>
        /// Visits <c>tyhpdefHookedProperty</c>: one <c>$name</c>, optional <c>?? expr</c>
        /// expected default, plus a bodyless hook list.
        /// </summary>
        public override PhpPropertyAst VisitTyhpdefHookedProperty([NotNull] TyhpParser.TyhpdefHookedPropertyContext context)
        {
            var variableName = context.Variable?.Text;
            if (string.IsNullOrEmpty(variableName))
            {
                this.ReportMissingRequired(context, "tyhpdefHookedProperty.Variable");
                variableName = "<error>";
            }

            PhpPropertyHookListAst? hooks = null;
            if (context.Accessors != null)
            {
                hooks = this.VisitTyhpdefPropertyHookList(context.Accessors);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefHookedProperty.Accessors");
            }

            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;

            return PhpPropertyAst.Create(
                variableName,
                coalesceExpr,
                hooks,
                null,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits <c>tyhpdefPropertyHookList</c>, matching <see cref="PhpParserAstVisitor.VisitPropertyHookList"/>.
        /// </summary>
        public override PhpPropertyHookListAst VisitTyhpdefPropertyHookList([NotNull] TyhpParser.TyhpdefPropertyHookListContext context)
            => PhpPropertyHookListAst.Create(
                context._Items.Select(this.VisitTyhpdefPropertyHook),
                context
            );

        /// <summary>
        /// Visits a bodyless tyhpdef property hook (<c>get;</c> / <c>&amp;get;</c> / <c>set;</c>).
        /// Mirrors PHP interface <c>get;</c>: <c>body: null</c>, <c>ReturnsRef</c> from <c>returnsRef</c>,
        /// modifiers and attributes attached the same way as <see cref="PhpParserAstVisitor.VisitPropertyHook"/>.
        /// </summary>
        public override PhpPropertyHookAst VisitTyhpdefPropertyHook([NotNull] TyhpParser.TyhpdefPropertyHookContext context)
        {
            string? hookName = null;
            if (context.Identifier != null)
            {
                hookName = this.VisitIdentifier(context.Identifier).ValueString;
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefPropertyHook.Identifier");
            }

            return PhpPropertyHookAst.Create(
                hookName,
                context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null,
                context.Modifiers != null ? this.VisitPropertyHookModifiers(context.Modifiers) : null,
                parameters: null,
                body: null,
                context,
                languageMode: GetCurrentLanguageMode(context),
                isExpressionBody: false
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }
