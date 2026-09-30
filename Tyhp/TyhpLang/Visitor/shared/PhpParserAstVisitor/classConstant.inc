// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpDereferenceableAst VisitClassConstant([NotNull] TyhpParser.ClassConstantContext context)
        {
            IDereferenceableBase prefix;
            if (context.ClassName != null)
            {
                prefix = this.VisitClassName(context.ClassName);
            }
            else if (context.Prefix != null)
            {
                prefix = this.VisitVariableClassName(context.Prefix);
            }
            else
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "class-constant-prefix", context.GetType().Name
                );
                prefix = ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return PhpDereferenceableAst.Create(
                prefix,
                this.VisitDereferenceableClassConstantAccessSuffix(context.Suffix),
                context
            );
        }
