// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override ITypeExpression VisitTypeWithoutStatic([NotNull] TyhpParser.TypeWithoutStaticContext context)
        {
            if (context.ArrayType != null)
            {
                return PhpBuiltinTypeAst.Create("array", context)
                    .WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(context.typeNameGrammarAddon()));
            }
            else if (context.CallableType != null)
            {
                var builtin = PhpBuiltinTypeAst.Create("callable", context);
                var addon = context.typeNameGrammarAddon();
                return addon != null
                    ? builtin.WithGrammarAddon("typeName", this.VisitTypeNameGrammarAddon(addon))
                    : builtin;
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
