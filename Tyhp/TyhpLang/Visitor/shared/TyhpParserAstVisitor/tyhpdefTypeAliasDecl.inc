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

        public override TyhpTypeAliasAst VisitTyhpdefTypeAliasDecl([NotNull] TyhpParser.TyhpdefTypeAliasDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefTypeAliasDecl.Statement");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpTypeAlias(context.Statement)
                .WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }
