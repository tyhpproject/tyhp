namespace Tyhp.TyhpLang.Enum
{
    /// <summary>
    /// Specifies the variance of a generic type parameter.
    /// </summary>
    public enum TypeVariance
    {
        /// <summary>No variance (invariant).</summary>
        Invariant = 0,

        /// <summary>Covariant (out).</summary>
        Covariant,

        /// <summary>Contravariant (in).</summary>
        Contravariant,
    }
}
