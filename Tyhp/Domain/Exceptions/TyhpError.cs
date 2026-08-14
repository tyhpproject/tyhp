using Tyhp.Domain.Diagnostics;

namespace Tyhp.Domain.Exceptions
{
    public class TyhpError : Exception
    {
        public string FileName {get; protected set;}
        public int LineNumber {get; protected set;}
        public int Column {get; protected set;}
        public MessageCode MessageCode {get; protected set;}
        public object[] FormatParams {get; protected set;}


        public TyhpError(string fileName, int lineNumber, int column, int code, params object[] formatParams) : base(Tyhp.CLI.Message.LocalizeErrorCode(code, formatParams))
        {
            this.FileName = fileName;
            this.LineNumber = lineNumber;
            this.Column = column;
            this.FormatParams = formatParams;
            this.MessageCode = (MessageCode)code;
        }

        public TyhpError(string fileName, int lineNumber, int column, MessageCode code, params object[] formatParams) : this(fileName, lineNumber, column, ((int)code), formatParams)
        {

        }

        /// <summary>
        /// Converts this exception to a diagnostic for use with the diagnostic system.
        /// </summary>
        /// <returns>A diagnostic representing this error.</returns>
        public IDiagnostic ToDiagnostic()
        {
            return Diagnostic.Error(this.MessageCode, this.FileName, this.LineNumber, this.Column, this.FormatParams, null, null);
        }

        public void Display()
        {
            this.ToDiagnostic().Display();
        }


    }
}