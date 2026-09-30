// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Override the internal functions grammar addon to dispatch to
        /// Tyhp compile-time construct handlers.
        ///
        /// Grammar (TyhpParser.g4):
        ///   internalFunctionsGrammarAddon
        ///       : T_TYHP_VARIABLE_EXISTS T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionVariableExists
        ///       | T_TYHP_TYPEOF T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionTypeof
        ///       | T_TYHP_TYPEOF BuiltinCast=(T_DOUBLE_CAST|…)
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionTypeofBuiltinCast
        ///       | T_DEFAULT T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionDefault
        ///       | T_TYHP_NAMEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionNameof
        ///       ;
        /// </summary>
        public override IStatement VisitInternalFunctionsGrammarAddon(
            [NotNull] TyhpParser.InternalFunctionsGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpInternalFunctionVariableExistsContext variableExistsCtx
                    => this.VisitTyhpInternalFunctionVariableExists(variableExistsCtx),
                TyhpParser.TyhpInternalFunctionTypeofContext typeofCtx
                    => this.VisitTyhpInternalFunctionTypeof(typeofCtx),
                TyhpParser.TyhpInternalFunctionTypeofBuiltinCastContext typeofCastCtx
                    => this.VisitTyhpInternalFunctionTypeofBuiltinCast(typeofCastCtx),
                TyhpParser.TyhpInternalFunctionDefaultContext defaultCtx
                    => this.VisitTyhpInternalFunctionDefault(defaultCtx),
                TyhpParser.TyhpInternalFunctionDefaultBuiltinCastContext defaultCastCtx
                    => this.VisitTyhpInternalFunctionDefaultBuiltinCast(defaultCastCtx),
                TyhpParser.TyhpInternalFunctionNameofContext nameofCtx
                    => this.VisitTyhpInternalFunctionNameof(nameofCtx),
                _ => base.VisitInternalFunctionsGrammarAddon(context),
            };


        /// <summary>
        /// Visit the variable_exists() compile-time construct.
        /// Checks whether a variable is defined.
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionVariableExists
        ///       : T_TYHP_VARIABLE_EXISTS T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        /// </summary>
        public override TyhpVariableExistsAst VisitTyhpInternalFunctionVariableExists(
            [NotNull] TyhpParser.TyhpInternalFunctionVariableExistsContext context)
            => TyhpVariableExistsAst.Create(this.VisitExpr(context.Expr), context);


        /// <summary>
        /// Visit the typeof() compile-time construct.
        /// Materializes a runtime <c>\Tyhp\Type</c> value for the given type expression.
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionTypeof
        ///       : T_TYHP_TYPEOF T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        /// </summary>
        public override TyhpTypeofAst VisitTyhpInternalFunctionTypeof(
            [NotNull] TyhpParser.TyhpInternalFunctionTypeofContext context)
            => TyhpTypeofAst.Create(this.VisitTypeExpr(context.TypeExpr), context);


        /// <summary>
        /// Visit typeof() when the type is written as a PHP cast token, e.g. <c>typeof(int)</c>.
        /// The PHP lexer tokenizes <c>(int)</c> as a single T_INT_CAST (parens included), so it
        /// cannot match the <c>typeExpr</c> form above. Maps the cast token to a PhpBuiltinTypeAst
        /// the same way default() does.
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionTypeofBuiltinCast
        ///       : T_TYHP_TYPEOF BuiltinCast=(T_DOUBLE_CAST|T_OBJECT_CAST|T_INT_CAST|
        ///           T_STRING_CAST|T_BOOL_CAST|T_ARRAY_CAST|T_DECIMAL_CAST)
        /// </summary>
        public override TyhpTypeofAst VisitTyhpInternalFunctionTypeofBuiltinCast(
            [NotNull] TyhpParser.TyhpInternalFunctionTypeofBuiltinCastContext context)
        {
            var typeName = CastTokenTypeToTypeName(context.BuiltinCast.Type);
            var builtinType = PhpBuiltinTypeAst.Create(typeName, context);
            return TyhpTypeofAst.Create(builtinType, context);
        }


        /// <summary>
        /// Visit the default() compile-time construct.
        /// Returns the default value for a given type.
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionDefault
        ///       : T_DEFAULT T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        /// </summary>
        public override TyhpDefaultAst VisitTyhpInternalFunctionDefault(
            [NotNull] TyhpParser.TyhpInternalFunctionDefaultContext context)
            => TyhpDefaultAst.Create(this.VisitTypeExpr(context.TypeExpr), context);


        /// <summary>
        /// Visit the default() compile-time construct when the type is written as a PHP cast token,
        /// e.g. <c>default(int)</c>. The PHP lexer tokenizes <c>(int)</c> as a single T_INT_CAST
        /// (parens included), so it cannot match the <c>typeExpr</c> form above. This alternative
        /// maps the cast token to the matching builtin type name and builds a TyhpDefaultAst whose
        /// TypeExpression is a PhpBuiltinTypeAst, so the emitter's BuildDefaultExpression produces
        /// the right zero value (0 / '' / false / [] / ...).
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionDefaultBuiltinCast
        ///       : T_DEFAULT BuiltinCast=(T_DOUBLE_CAST|T_OBJECT_CAST|T_INT_CAST|
        ///           T_STRING_CAST|T_BOOL_CAST|T_ARRAY_CAST|T_DECIMAL_CAST)
        /// </summary>
        public override TyhpDefaultAst VisitTyhpInternalFunctionDefaultBuiltinCast(
            [NotNull] TyhpParser.TyhpInternalFunctionDefaultBuiltinCastContext context)
        {
            var typeName = CastTokenTypeToTypeName(context.BuiltinCast.Type);
            var builtinType = PhpBuiltinTypeAst.Create(typeName, context);
            return TyhpDefaultAst.Create(builtinType, context);
        }


        /// <summary>
        /// Visit the nameof() compile-time construct.
        /// Returns the string name of a symbol at compile time.
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionNameof
        ///       : T_TYHP_NAMEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        /// </summary>
        public override TyhpNameofAst VisitTyhpInternalFunctionNameof(
            [NotNull] TyhpParser.TyhpInternalFunctionNameofContext context)
            => TyhpNameofAst.Create(this.VisitExpr(context.Expr), context);
