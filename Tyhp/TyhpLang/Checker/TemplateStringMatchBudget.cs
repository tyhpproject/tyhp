namespace Tyhp.TyhpLang.Checker
{
    /// <summary>Shared step budget for template-string membership and subtyping checks.</summary>
    internal sealed class TemplateStringMatchBudget
    {
        private int _steps;

        public TemplateStringMatchBudget(int maxSteps) => MaxSteps = maxSteps > 0 ? maxSteps : 256;

        public int MaxSteps { get; }

        public bool ExceededLimit { get; private set; }

        public void MarkExceeded() => ExceededLimit = true;

        public bool TryConsumeStep()
        {
            if (ExceededLimit)
            {
                return false;
            }

            _steps++;
            if (_steps > MaxSteps)
            {
                ExceededLimit = true;
                return false;
            }

            return true;
        }
    }
}
