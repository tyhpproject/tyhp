using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpInlineOutputAst : Base2Ast, ISrcElement, IStatementList<ITopStatement>, IStatement
    {
        private const short IS_ECHO_FLAG = -1;

        public bool IsEcho
        {
            get => this.HasFlag(IS_ECHO_FLAG);
            set => this.SetFlag(IS_ECHO_FLAG, value);
        }

        public string Content => this.IsEcho ? "" : (ValueString ?? "");

        public PhpTopStatementListAst? TopStatementList {
            get {
                if (this.IsEcho) {
                    if (Children[0] is not PhpTopStatementListAst) {
                        Children[0] = PhpTopStatementListAst.Create(null, this);
                    }

                    return Children[0] as PhpTopStatementListAst ?? throw new InvalidOperationException("Children[0] is not a PhpTopStatementListAst");
                } else {
                    return null;
                }
            }
        }

        public static PhpInlineOutputAst Create(string content, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpInlineOutputAst
            {
                ValueString = content,
                IsEcho = false,
            };
            result.SetContext(context, languageMode);
            return result;
        }

        public static PhpInlineOutputAst Create(PhpTopStatementListAst topStatementList, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpInlineOutputAst
            {
                Children = [topStatementList],
                IsEcho = true,
            };
            result.SetContext(context, languageMode);
            return result;
        }

        public void Add(ITopStatement? child)
            => this.TopStatementList?.Add(child);

        public void AddRange(IEnumerable<ITopStatement?> children)
            => this.TopStatementList?.AddRange(children);

        public ITopStatement? ElementAt(int index)
            => this.TopStatementList?.ElementAt(index);

        public IEnumerable<ITopStatement?> GetAll()
            => this.TopStatementList?.GetAll() ?? [];

        public void InsertAt(int index, ITopStatement? child)
            => this.TopStatementList?.InsertAt(index, child);

        public void RemoveAt(int index)
            => this.TopStatementList?.RemoveAt(index);
    }
} 