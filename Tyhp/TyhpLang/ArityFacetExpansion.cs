namespace Tyhp.TyhpLang
{
    /// <summary>
    /// Shared arity-prefix expansion for optional trailing parameters (callable/Closure facets
    /// today; Story 27 <c>new&lt;…&gt;</c> constructable facets later).
    ///
    /// A signature whose last M non-variadic parameters have defaults yields M+1 valid call
    /// arities — one for each prefix from the required minimum up to the full non-variadic list.
    /// Variadic parameters are always optional and never generate infinite facets.
    /// </summary>
    public static class ArityFacetExpansion
    {
        /// <summary>
        /// Returns ascending prefix lengths from <c>requiredCount</c> to <c>totalCount</c>
        /// (inclusive), where those counts ignore variadic parameters.
        /// </summary>
        /// <param name="parameters">
        /// Ordered parameter flags. <c>HasDefault</c> is true when the parameter has a default
        /// value; <c>IsVariadic</c> marks a trailing <c>...$args</c> parameter.
        /// </param>
        public static IReadOnlyList<int> GetValidArityPrefixes(
            IReadOnlyList<(bool HasDefault, bool IsVariadic)> parameters)
        {
            var requiredCount = 0;
            var totalCount = 0;
            foreach (var (hasDefault, isVariadic) in parameters)
            {
                if (isVariadic)
                {
                    continue;
                }

                totalCount++;
                if (!hasDefault)
                {
                    requiredCount++;
                }
            }

            // Empty or variadic-only → a single zero-arg prefix.
            if (totalCount == 0)
            {
                return [0];
            }

            var prefixes = new int[totalCount - requiredCount + 1];
            for (var i = 0; i < prefixes.Length; i++)
            {
                prefixes[i] = requiredCount + i;
            }

            return prefixes;
        }
    }
}
