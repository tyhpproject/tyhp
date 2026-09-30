// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitMemberRefAst VisitTraitMethodReferenceGrammarAddon(
            [NotNull] TyhpParser.TraitMethodReferenceGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpTraitOperatorMethodReferenceContext ctx
                    => this.VisitTyhpTraitOperatorMethodReference(ctx),
                _ => base.VisitTraitMethodReferenceGrammarAddon(context)
            };


        /// <summary>
        /// Unqualified <c>operator *&lt;string&gt;</c> (and <c>operator *</c>) inside
        /// <c>use extension { … }</c> adaptations.
        /// </summary>
        public override PhpTraitMemberRefAst VisitTyhpTraitOperatorMethodReference(
            [NotNull] TyhpParser.TyhpTraitOperatorMethodReferenceContext context)
            => this.BuildOperatorTraitMemberRef(
                traitName: null,
                op: context.Op,
                targetType: context.TargetType,
                context);
