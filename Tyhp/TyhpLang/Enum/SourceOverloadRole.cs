namespace Tyhp.TyhpLang.Enum
{
    /// <summary>
    /// Role of a function or method symbol inside a <c>.tyhp</c> overload set
    /// (bodyless signatures followed by one declaration).
    /// </summary>
    public enum SourceOverloadRole
    {
        /// <summary>Not part of a source overload set (tyhpdef overloads also use this).</summary>
        None = 0,

        /// <summary>
        /// The registered declaration that has the body. Callers never see its own signature;
        /// they see the <c>Overloads</c> only.
        /// </summary>
        Implementation,

        /// <summary>
        /// The registered declaration of an abstract/interface set — the final signature, which is
        /// also callable. Callers see the <c>Overloads</c> in order, then this signature.
        /// </summary>
        LastSignature,

        /// <summary>A bodyless overload signature attached to a registered declaration.</summary>
        Signature,
    }
}
