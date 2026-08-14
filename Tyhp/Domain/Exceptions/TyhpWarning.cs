using Tyhp.Domain.Diagnostics;

namespace Tyhp.Domain.Exceptions
{
    public class TyhpWarning : Exception
    {
        public string FileName {get; protected set;}
        public int LineNumber {get; protected set;}
        public int Column {get; protected set;}
        public MessageCode MessageCode {get; protected set;}
        public object[] FormatParams {get; protected set;}


        public TyhpWarning(string fileName, int lineNumber, int column, int code, params object[] formatParams) : base(Tyhp.CLI.Message.LocalizeWarningCode(code, formatParams))
        {
            this.FileName = fileName;
            this.LineNumber = lineNumber;
            this.Column = column;
            this.FormatParams = formatParams;
            this.MessageCode = (MessageCode)code;
        }

        public TyhpWarning(string fileName, int lineNumber, int column, MessageCode code, params object[] formatParams) : this(fileName, lineNumber, column, ((int)code), formatParams)
        {

        }

        /// <summary>
        /// Converts this exception to a diagnostic for use with the diagnostic system.
        /// </summary>
        /// <returns>A diagnostic representing this warning.</returns>
        public IDiagnostic ToDiagnostic()
        {
            return Diagnostic.Warning(this.MessageCode, this.FileName, this.LineNumber, this.Column, this.FormatParams, null, null);
        }

        public void Display()
        {
            this.ToDiagnostic().Display();
        }
    }
}