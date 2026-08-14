using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Ast {
    public abstract class NodeListAst<TChild, TSelf> : Base2Ast where TChild : Interfaces.IBase2Ast where TSelf : NodeListAst<TChild, TSelf>, new()
    {
        protected virtual bool FilterNullChildren => true;

        public static TSelf Create(IEnumerable<TChild?>? children, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TSelf();
            result.Children = [.. (children ?? []).Where(x => !result.FilterNullChildren || x != null)];
            result.SetContext(context, languageMode);
            return result;
        }

        public static TSelf Create(IEnumerable<TChild?>? children, Base2Ast context)
        {
            var result = new TSelf();
            result.Children = [.. (children ?? []).Where(x => !result.FilterNullChildren || x != null)];
            result.SetContext(context);
            return result;
        }

        public static TSelf Wrap(TChild? item, ParserRuleContext context, string? languageMode = null)
        {
            if (item is TSelf nodeListAst) {
                nodeListAst.SetContext(context, languageMode);
                return nodeListAst;
            }

            return Create([item], context, languageMode);
        }

        public IEnumerable<TChild?> GetAll()
            => Children.OfType<TChild?>().Where(x => !FilterNullChildren || x != null);

        public IEnumerable<TChild> GetAllNotNull()
            => Children.OfType<TChild>().Where(x => x != null);
    }
}