// namespace Tyhp.TyhpLang
// {
//     public class TyhpCompiler
//     {
//         public static Tyhp.TyhpLang.Ast.TyhpFileAst CompileFile(string content, string filename = "_") {
//             var lexer = new Tyhp.TyhpLang.Parser.TyhpLexer(new Antlr4.Runtime.AntlrInputStream(content));
//             var tokens = new Antlr4.Runtime.CommonTokenStream(lexer);
//             var parser = new Tyhp.TyhpLang.Parser.TyhpParser(tokens);

//             // parser.AddErrorListener(new Antlr4.Runtime.DiagnosticErrorListener());
//             parser.AddErrorListener(Antlr4.Runtime.ConsoleErrorListener<Antlr4.Runtime.IToken>.Instance);
//             var errors = new Tyhp.TyhpLang.Parser.ErrorListener();
//             parser.AddErrorListener(errors);
//             parser.ErrorHandler = new Tyhp.TyhpLang.Parser.ErrorStrategy();
//             parser.BuildParseTree = true;

//             var tree = parser.main();

//             if (errors.Errors.Any()) {
//                 foreach (var error in errors.Errors) {
//                     Console.WriteLine(filename + "@" + error.Line.ToString() + ":" + error.Column.ToString() + " => (" + parser.Vocabulary.GetSymbolicName(error.Token.Type) + ") " + error.Message);
//                 }
//                 //throw new Exception("PARSER ERRORS!");
//             }

//             // foreach (var t in tokens.GetTokens()) {
//             //     Console.WriteLine(Tyhp.TyhpLang.Parser.TyhpParser.DefaultVocabulary.GetSymbolicName(t.Type) + ": " + t.Text);
//             // }
//             var visitor = new Tyhp.TyhpLang.Visitor.TyhpParserAstVisitor(tokens);
//             return visitor.VisitTyhpFile(tree);
//         }
//     }
// }