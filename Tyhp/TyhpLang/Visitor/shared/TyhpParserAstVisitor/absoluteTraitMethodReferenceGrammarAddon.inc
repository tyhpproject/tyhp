// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitMemberRefAst VisitAbsoluteTraitMethodReferenceGrammarAddon(
            [NotNull] TyhpParser.AbsoluteTraitMethodReferenceGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpAbsoluteTraitOperatorMethodReferenceContext ctx
                    => this.VisitTyhpAbsoluteTraitOperatorMethodReference(ctx),
                _ => base.VisitAbsoluteTraitMethodReferenceGrammarAddon(context)
            };


        /// <summary>
        /// Qualified <c>Foo::operator *&lt;string&gt;</c> inside
        /// <c>use extension { … }</c> adaptations (<c>insteadof</c> / qualified hide).
        /// </summary>
        public override PhpTraitMemberRefAst VisitTyhpAbsoluteTraitOperatorMethodReference(
            [NotNull] TyhpParser.TyhpAbsoluteTraitOperatorMethodReferenceContext context)
        {
            var traitName = context.ClassName != null ? this.VisitClassName(context.ClassName) : null;
            return this.BuildOperatorTraitMemberRef(
                traitName,
                op: context.Op,
                targetType: context.TargetType,
                context);
        }
