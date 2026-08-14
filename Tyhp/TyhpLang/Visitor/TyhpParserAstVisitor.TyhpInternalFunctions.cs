namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Override the internal functions grammar addon to dispatch to
        /// Tyhp compile-time construct handlers.
        ///
        /// Grammar (TyhpParser.g4):
        ///   internalFunctionsGrammarAddon
        ///       : T_TYHP_VARIABLE_EXISTS T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionVariableExists
        ///       | T_TYHP_TYPEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        ///           {this.isLanguageMode("tyhp")}?    #tyhpInternalFunctionTypeof
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
        /// Returns the type name of a value as a string at compile time.
        ///
        /// Grammar:
        ///   #tyhpInternalFunctionTypeof
        ///       : T_TYHP_TYPEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        /// </summary>
        public override TyhpTypeofAst VisitTyhpInternalFunctionTypeof(
            [NotNull] TyhpParser.TyhpInternalFunctionTypeofContext context)
            => TyhpTypeofAst.Create(this.VisitExpr(context.Expr), context);

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
        /// Maps a PHP cast token type to the builtin type name used by the emitter's
        /// BuildDefaultExpression to select the zero value.
        /// </summary>
        private static string CastTokenTypeToTypeName(int tokenType) => tokenType switch
        {
            TyhpParser.T_INT_CAST => "int",
            TyhpParser.T_STRING_CAST => "string",
            TyhpParser.T_BOOL_CAST => "bool",
            TyhpParser.T_ARRAY_CAST => "array",
            TyhpParser.T_DOUBLE_CAST => "float",
            TyhpParser.T_OBJECT_CAST => "object",
            TyhpParser.T_DECIMAL_CAST => "decimal",
            _ => "mixed",
        };

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
    }
}
