// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TyhpCallableShapeParameterListAst VisitCallableShapeParameterList(
            [NotNull] TyhpParser.CallableShapeParameterListContext context)
            => TyhpCallableShapeParameterListAst.Create(
                (context._Items ?? []).Where(item => item != null).Select(this.VisitCallableShapeParameter!),
                context,
                GetCurrentLanguageMode(context));
