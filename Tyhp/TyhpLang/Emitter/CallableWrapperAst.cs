using System.Collections.Generic;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// One fragment of a <see cref="CallableWrapperAst"/>: literal PHP text, a spelled checked
    /// type, or an AST expression the emitter renders.
    /// </summary>
    internal readonly record struct CallableWrapperPart
    {
        public string? Text { get; init; }

        public ICheckedType? Type { get; init; }

        public IExpression? Expression { get; init; }

        /// <summary>Type part only: text emitted before the spelled type, only when it is not empty.</summary>
        public string? BeforeType { get; init; }

        /// <summary>Type part only: text emitted after the spelled type, only when it is not empty.</summary>
        public string? AfterType { get; init; }

        /// <summary>Expression part only: the expression is a parameter default from another declaration.</summary>
        public bool IsDefaultValue { get; init; }

        public static CallableWrapperPart FromText(string text) => new() { Text = text };

        public static CallableWrapperPart FromType(
            ICheckedType type,
            string? before = null,
            string? after = null) =>
            new() { Type = type, BeforeType = before, AfterType = after };

        public static CallableWrapperPart FromExpression(IExpression expression, bool isDefault = false) =>
            new() { Expression = expression, IsDefaultValue = isDefault };
    }

    /// <summary>
    /// Emitter-synthesized expression made of PHP text, spelled types, and AST expressions. It
    /// replaces a first-class callable whose parameters accept implicit conversions (a wrapper
    /// closure) and also carries the inline conversion expressions inside that wrapper. It is
    /// never produced by the parser and never cached.
    /// </summary>
    internal sealed class CallableWrapperAst : Base2Ast, IExpression, IDereferenceableBase
    {
        public IReadOnlyList<CallableWrapperPart> Parts { get; }

        /// <summary>Fully-qualified emitted name of the enclosing class, used to resolve <c>self::</c> in copied defaults.</summary>
        public string? SelfClassFqn { get; }

        private CallableWrapperAst(IReadOnlyList<CallableWrapperPart> parts, string? selfClassFqn)
        {
            this.Parts = parts;
            this.SelfClassFqn = selfClassFqn;
        }

        internal static CallableWrapperAst Create(
            IReadOnlyList<CallableWrapperPart> parts,
            Base2Ast context,
            string? selfClassFqn = null)
        {
            var result = new CallableWrapperAst(parts, selfClassFqn);
            result.SetContext(context);
            return result;
        }
    }
}
