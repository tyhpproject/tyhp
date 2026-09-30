// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits a list of struct property declarations.
        ///
        /// Grammar:
        ///   tyhpStructPropertyList
        ///     : Items+=tyhpStructProperty*
        ///     ;
        /// </summary>
        public override TyhpStructPropertyListAst VisitTyhpStructPropertyList([NotNull] TyhpParser.TyhpStructPropertyListContext context)
            => TyhpStructPropertyListAst.Create(
                (context._Items ?? Enumerable.Empty<TyhpParser.TyhpStructPropertyContext>())
                    .Select(this.VisitTyhpStructProperty),
                context
            );
