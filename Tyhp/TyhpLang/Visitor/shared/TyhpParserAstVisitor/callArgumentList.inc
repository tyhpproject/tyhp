// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Attaches Tyhp generic type arguments from <c>functionCallGrammarAddon</c> to the call AST.
        /// The base <see cref="PhpParserAstVisitor.VisitFunctionCallGrammarAddon"/> return type
        /// (<see cref="PhpArgumentListAst"/>) cannot represent a type-argument list, so this override
        /// adds them as a separate grammar addon on <see cref="PhpCallAst"/>.
        /// </summary>
        public override PhpCallAst VisitCallArgumentList([NotNull] TyhpParser.CallArgumentListContext context)
        {
            var call = base.VisitCallArgumentList(context);
            var genericAddon = context.functionCallGrammarAddon();
            if (genericAddon?.GenericArguments != null)
            {
                call.AddGrammarAddon(
                    "genericTypeArguments",
                    this.VisitTyhpGenericTypeArguments(genericAddon.GenericArguments));
            }

            return call;
        }
