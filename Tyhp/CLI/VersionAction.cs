using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tyhp.Domain.Diagnostics;

namespace Tyhp.CLI
{
    /// <summary>
    /// Displays Tyhp compiler version information (human-readable or JSON).
    /// </summary>
    public class VersionAction : ActionRunnerBase
    {
        private readonly Config.Project _project;

        public VersionAction(Config.Project project)
        {
            this._project = project;
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            var tyhpVersion = new Message.VersionHelper().GetAssemblyVersion();
            var dotnetVersion = RuntimeInformation.FrameworkDescription;
            var antlrVersion = GetAntlrRuntimeVersion();
            var osDescription = RuntimeInformation.OSDescription;

            if (this._project.JsonOutput)
            {
                var payload = new Dictionary<string, string>
                {
                    ["tyhp"] = tyhpVersion,
                    ["dotnet"] = dotnetVersion,
                    ["antlr"] = antlrVersion,
                    ["os"] = osDescription,
                };

                Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
            }
            else
            {
                Message.Info("CLI_VersionTyhp", tyhpVersion);
                Message.Info("CLI_VersionDotNet", dotnetVersion);
                Message.Info("CLI_VersionAntlr", antlrVersion);
                Message.Info("CLI_VersionOs", osDescription);
            }

            Environment.ExitCode = (int)Tyhp.Domain.Enums.ExitCode.Success;
            return null;
        }

        private static string GetAntlrRuntimeVersion()
        {
            try
            {
                var assembly = typeof(Antlr4.Runtime.Lexer).Assembly;
                var version = assembly.GetName().Version?.ToString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }

                var informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                return string.IsNullOrWhiteSpace(informational) ? "unknown" : informational;
            }
            catch (Exception ex) when (ex is TypeLoadException
                or FileNotFoundException
                or FileLoadException
                or BadImageFormatException
                or ReflectionTypeLoadException)
            {
                return "unknown";
            }
        }
    }
}
