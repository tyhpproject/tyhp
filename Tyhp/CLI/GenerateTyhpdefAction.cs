using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;

namespace Tyhp.CLI
{
    public class GenerateTyhpdefAction : ActionRunnerBase
    {
        protected CancellationToken? CancelToken {get; set;}

        public GenerateTyhpdefAction()
        {
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.CancelToken = cancellationToken;
            // Task.Run(this.RunAsync);

            // TODO: allow parsing PHP file(s) directly in the future
            var extName = Config.Project.Singleton?.GetExtName();
            if (String.IsNullOrWhiteSpace(extName)) {
                Message.Error("CLI_ExtNameRequired");
                return null;
            }

            // Message.Display("Generating Tyhpdef files for the PHP \"{0}\" extension.", extName);

            // Task.Run(async() => await this.RunAsync(extName));
            
            // if (Tyhpdef.AllKeyed.ContainsKey("__php_ext_" + extName)) {
            //     Console.WriteLine(Tyhpdef.AllKeyed["__php_ext_" + extName]);
            // } else {
            //     Message.Error("Invalid PHP extension: " + extName);
            // }

            // Generate tyhpdef action does not produce a compilation result
            return null;
        }

        public async Task RunAsync(string extName)
        {
           

            await Task.CompletedTask;
        }

    }

}