// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits <see cref="TyhpParser.tyhpExtensionFunctionList"/>.
        /// </summary>
        public override TyhpExtensionFunctionListAst VisitTyhpExtensionFunctionList([NotNull] TyhpParser.TyhpExtensionFunctionListContext context)
            => TyhpExtensionFunctionListAst.Create(
                (context.tyhpExtensionMember() ?? Enumerable.Empty<TyhpParser.TyhpExtensionMemberContext>())
                    .Select(this.VisitTyhpExtensionMemberAsExtensionMember),
                context
            );
