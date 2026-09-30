// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        public override ITypeExpression VisitTypeWithoutStaticGrammarAddon(
            TyhpParser.TypeWithoutStaticGrammarAddonContext? context)
        {
            // Callers should null-check the GrammarAddon child; keep a defensive guard so a stray
            // null context cannot NRE on ScalarType (same truncated-`?` recovery class).
            if (context == null)
            {
                return ErrorAst.Create(
                    "Missing type after error recovery",
                    Domain.Exceptions.MessageCode.VisitorMissingRequiredNode,
                    0,
                    0);
            }

            if (context.ScalarType != null)
            {
                return VisitTyhpScalarType(context.ScalarType);
            }

            if (context.StructShape != null)
            {
                return VisitTyhpStructShape(context.StructShape);
            }

            if (context.GroupedType != null)
            {
                return VisitGroupedType(context.GroupedType);
            }

            return base.VisitTypeWithoutStaticGrammarAddon(context);
        }
