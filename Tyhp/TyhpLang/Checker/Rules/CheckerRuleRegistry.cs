using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Collects checker rules and dispatches them by AST node runtime type.
    /// </summary>
    public sealed class CheckerRuleRegistry
    {
        private readonly IReadOnlyList<ICheckerRule> _rules;
        private readonly Dictionary<Type, List<ICheckerRule>> _rulesByNodeType;

        public CheckerRuleRegistry(IEnumerable<ICheckerRule>? rules = null)
        {
            _rules = (rules ?? []).ToList();
            _rulesByNodeType = BuildIndex(_rules);
        }

        public IReadOnlyList<ICheckerRule> RegisteredRules => _rules;

        public bool Dispatch(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            var nodeType = node.GetType();
            if (!_rulesByNodeType.TryGetValue(nodeType, out var applicableRules))
            {
                return false;
            }

            var suppressTraversal = false;
            foreach (var rule in applicableRules)
            {
                if (!rule.Handles(node))
                {
                    continue;
                }

                rule.Check(node, state, context, diagnostics);
                if (rule.SuppressChildTraversal(node))
                {
                    suppressTraversal = true;
                }
            }

            return suppressTraversal;
        }

        private static Dictionary<Type, List<ICheckerRule>> BuildIndex(IReadOnlyList<ICheckerRule> rules)
        {
            var index = new Dictionary<Type, List<ICheckerRule>>();

            foreach (var rule in rules)
            {
                foreach (var handledType in rule.HandledNodeTypes)
                {
                    if (!index.TryGetValue(handledType, out var list))
                    {
                        list = [];
                        index[handledType] = list;
                    }

                    list.Add(rule);
                }
            }

            return index;
        }
    }
}
