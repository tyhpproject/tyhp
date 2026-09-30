// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override IDereferenceableBase VisitVariableClassName([NotNull] TyhpParser.VariableClassNameContext context)
            => this.VisitFullyDereferenceable(context.fullyDereferenceable());
