namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Tracks variables that share the same reference storage (&amp;$var aliasing).
    /// </summary>
    public sealed class ReferenceGroup
    {
        public HashSet<string> MemberVariables { get; } = new(StringComparer.Ordinal);

        public void AddMember(string variableName) => MemberVariables.Add(variableName);

        public void PropagateTypeChange(
            string assignedVariable,
            ICheckedType newType,
            Dictionary<string, VariableState> variables)
        {
            foreach (var member in MemberVariables)
            {
                if (!variables.TryGetValue(member, out var memberState))
                {
                    continue;
                }

                var current = memberState.EffectiveType;
                memberState.NarrowedType = CheckedTypes.AreTypesEqual(current, newType)
                    ? memberState.NarrowedType ?? newType
                    : CheckedTypes.UnionTypes(current, newType);
                memberState.IsDefinitelyAssigned = true;
                memberState.IsPossiblyUndefined = false;
            }
        }
    }
}
