using Antlr4.Runtime;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a parse-time or visitor-time error in the AST tree.
    /// This node type allows the compiler to continue processing after encountering
    /// errors, collecting multiple diagnostics rather than crashing on the first error.
    /// Implements all major AST interfaces to allow error recovery to continue parsing.
    /// </summary>
    public class ErrorAst : Base2Ast,
        IExpression,
        IStatement,
        ITopStatement,
        IClassMember,
        IExtensionMemberAst,
        ISrcElement,
        IAttributedStatement,
        IClassNameReference,
        IDereferenceableBase,
        IDereferenceableSuffix,
        IForeachVariable,
        IScalar,
        ITraitAdaptation,
        ITypeExpression
    {
        /// <summary>
        /// The error message describing what went wrong.
        /// This is stored in ValueString for serialization.
        /// </summary>
        public string ErrorMessage
        {
            get => this.ValueString ?? string.Empty;
            protected set => this.ValueString = value;
        }

        /// <summary>
        /// The message code for this error.
        /// This is stored in ValueInt64 for serialization.
        /// </summary>
        public MessageCode Code
        {
            get => this.ValueInt64.HasValue ? (MessageCode)this.ValueInt64.Value : MessageCode.VisitorUnknownError;
            protected set => this.ValueInt64 = (long)value;
        }

        /// <summary>
        /// The parser rule context where the error occurred (if available).
        /// Note: This is not serialized and will be null after deserialization.
        /// </summary>
        public ParserRuleContext? Context { get; protected set; }

        /// <summary>
        /// Private constructor - use Create() factory method instead.
        /// </summary>
        protected ErrorAst()
        {
            this.ErrorMessage = string.Empty;
            this.Code = MessageCode.VisitorUnknownError;
        }

        /// <summary>
        /// Creates an ErrorAst node from a parser rule context.
        /// This is the plan-required signature for Phase 5 visitor error recovery.
        /// </summary>
        /// <param name="context">The parser rule context where the error occurred.</param>
        /// <param name="languageMode">The language mode (optional).</param>
        /// <returns>A new ErrorAst instance with position information extracted from the context.</returns>
        public static ErrorAst Create(
            ParserRuleContext context,
            string? languageMode = null)
        {
            var errorAst = new ErrorAst
            {
                ErrorMessage = $"Parse error at {context.GetType().Name}",
                Code = MessageCode.VisitorUnknownError,
                Context = context
            };

            errorAst.SetContext(context, languageMode);

            return errorAst;
        }

        /// <summary>
        /// Creates an ErrorAst node from a parser rule context.
        /// </summary>
        /// <param name="context">The parser rule context where the error occurred.</param>
        /// <param name="errorMessage">The error message describing what went wrong.</param>
        /// <param name="code">The message code for this error.</param>
        /// <param name="languageMode">The language mode (optional).</param>
        /// <returns>A new ErrorAst instance with position information extracted from the context.</returns>
        public static ErrorAst Create(
            ParserRuleContext context,
            string errorMessage,
            MessageCode code = MessageCode.VisitorUnknownError,
            string? languageMode = null)
        {
            var errorAst = new ErrorAst
            {
                ErrorMessage = errorMessage,
                Code = code,
                Context = context
            };

            errorAst.SetContext(context, languageMode);

            return errorAst;
        }

        /// <summary>
        /// Creates an ErrorAst node with explicit position information.
        /// </summary>
        /// <param name="errorMessage">The error message describing what went wrong.</param>
        /// <param name="code">The message code for this error.</param>
        /// <param name="line">The line number where the error occurred (1-indexed).</param>
        /// <param name="column">The column position where the error occurred (0-indexed).</param>
        /// <param name="startIndex">The start index in the input stream.</param>
        /// <param name="languageMode">The language mode (optional).</param>
        /// <returns>A new ErrorAst instance.</returns>
        public static ErrorAst Create(
            string errorMessage,
            MessageCode code,
            int line,
            int column,
            int startIndex = -1,
            string? languageMode = null)
        {
            return new ErrorAst
            {
                ErrorMessage = errorMessage,
                Code = code,
                Line = line,
                Column = column,
                StartIndex = startIndex,
                LanguageMode = languageMode
            };
        }

        /// <summary>
        /// ErrorAst nodes are never valid for semantic analysis - they represent errors.
        /// </summary>
        /// <returns>Always returns false.</returns>
        public override bool IsValid()
        {
            return false;
        }
    }
}
