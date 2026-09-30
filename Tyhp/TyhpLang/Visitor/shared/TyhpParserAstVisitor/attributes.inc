// #class TyhpParserAstVisitor
// #using Tyhp.TyhpLang.Enum
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        private TyhpOperatorOverloadAst VisitExtensionOperatorMember(
            TyhpParser.AttributesContext? attributes,
            TyhpParser.TyhpExtensionOperatorOverloadContext? opOverload,
            ParserRuleContext owner)
        {
            var attributeList = attributes != null ? this.VisitAttributes(attributes) : null;
            if (opOverload == null)
            {
                this.ReportMissingRequired(owner, "tyhpExtensionOperatorOverload");
                return TyhpOperatorOverloadAst.CreateError(owner, GetCurrentLanguageMode(owner))
                    .WithAttributes(attributeList);
            }

            return this.VisitTyhpExtensionOperatorOverload(opOverload).WithAttributes(attributeList);
        }


        private PhpConstDeclListAst VisitObjectShapeClassConstList(
            ParserRuleContext context,
            TyhpParser.AttributesContext? attributes,
            TyhpParser.TyhpdefDeprecatedOrObsoleteContext? deprecatedOrObsoleteCtx,
            TyhpParser.ClassConstModifiersContext? modifiersCtx,
            TyhpParser.ClassConstListContext? constListCtx,
            ITypeExpression? type)
        {
            PhpConstDeclListAst constList;
            if (constListCtx != null)
            {
                constList = this.VisitClassConstList(
                    constListCtx,
                    modifiersCtx != null ? this.VisitClassConstModifiers(modifiersCtx) : null,
                    type);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpObjectShapeClassConsts.ConstList");
                constList = PhpConstDeclListAst.Create(null, context);
            }

            var deprecatedOrObsolete = deprecatedOrObsoleteCtx != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(deprecatedOrObsoleteCtx)
                : null;
            if (deprecatedOrObsolete != null)
            {
                constList.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            constList.AddGrammarAddon(
                "objectShapeMember",
                TokenValueAst.Create("objectShapeMember", TyhpParser.T_CONST, context));

            if (attributes != null)
            {
                constList.AddAttributes(this.VisitAttributes(attributes));
            }

            return constList;
        }
