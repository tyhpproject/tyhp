using System.Text;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        /// <summary>
        /// Renders a <see cref="CallableWrapperAst"/>: text parts verbatim, types through the
        /// checked-type speller, expressions through <see cref="BuildExpression"/>.
        /// </summary>
        private string BuildCallableWrapper(CallableWrapperAst wrapper)
        {
            var text = new StringBuilder();
            foreach (var part in wrapper.Parts)
            {
                if (part.Text is not null)
                {
                    text.Append(part.Text);
                    continue;
                }

                if (part.Type is not null)
                {
                    var spelled = this.BuildCheckedTypeExpression(part.Type);
                    if (!string.IsNullOrWhiteSpace(spelled))
                    {
                        text.Append(part.BeforeType).Append(spelled).Append(part.AfterType);
                    }

                    continue;
                }

                if (part.Expression is null)
                {
                    continue;
                }

                var rendered = this.BuildExpression(part.Expression);
                if (part.IsDefaultValue && wrapper.SelfClassFqn is not null)
                {
                    // A default copied from the callee is evaluated outside its class.
                    rendered = System.Text.RegularExpressions.Regex.Replace(
                        rendered,
                        @"(?<![\w\\$])self::",
                        wrapper.SelfClassFqn + "::");
                }

                text.Append(rendered);
            }

            return text.ToString();
        }
    }
}
