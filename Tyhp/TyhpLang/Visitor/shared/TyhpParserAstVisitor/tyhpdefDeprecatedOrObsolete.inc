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
        /// Visits a tyhpdef deprecated, obsolete, or omit marker.
        /// Grammar: tyhpdefDeprecatedOrObsolete
        ///   : TokenValue=(T_TYHPDEF_DEPRECATED|T_TYHPDEF_OBSOLETE|T_TYHPDEF_OMIT)
        /// </summary>
        public override TokenValueAst VisitTyhpdefDeprecatedOrObsolete([NotNull] TyhpParser.TyhpdefDeprecatedOrObsoleteContext context)
            => this.GetTokenValueAst(context, context.TokenValue);
