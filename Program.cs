// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tyhp.CLI;
using Tyhp.Config;

[assembly: Microsoft.Extensions.Localization.RootNamespace("Tyhp")]

// .NET's CommandLineConfigurationProvider has no notion of value-less boolean flags without switch
// mappings: a trailing `--clean` is dropped, and `--clean --verbose` makes `--clean` swallow
// `--verbose` as its value. Expand the known bare boolean flags to `--flag=true` so they overlay
// onto config as booleans, without letting a bare flag consume the following token.
// Then rewrite `--explain CODE` into the `explain` action, and `--help` into `help` /
// `help --subject=…`, before action parsing. Explain runs before help so
// `tyhp --explain TYHP4008 --help` becomes help-about-explain.
// Keep the pristine argv: actions that proxy another CLI (composer) forward these tokens verbatim,
// so they must not see the `--flag=true` spelling the expansion below introduces for the binder.
var rawArgs = args;
args = ActionConfigProvider.ExpandBareBooleanFlags(args);
args = ActionConfigProvider.RewriteExplainAlias(args);
args = ActionConfigProvider.RewriteHelpAlias(args);

if (ActionConfigProvider.ReadInitialActionFromArgs(args, rawArgs)) {
    args = args.Skip(1).ToArray();
}

// Composer's flags belong to Composer, not to Tyhp's configuration: the .NET command-line provider
// reads any value-less `--flag` as a key that consumes the next token, so an unrecognized Composer
// flag would otherwise swallow the Tyhp flag behind it (`--no-interaction --no-tyhpdef`).
var cliArgs = ComposerAction.SelectTyhpConfigArgs(ActionConfigProvider.InitialAction, args);

// Positional paths travel to configuration as `path:*` keys, never through the command-line binder,
// which would read `/tmp/demo` as the Windows-style switch `--tmp/demo` and eat the flag behind it.
cliArgs = ActionConfigProvider.SelectBinderArgs(cliArgs);

// Reject `-d=x` style short switches and missing `--tyhp-project` targets before the host build,
// which would otherwise throw and exit 134 with a raw .NET stack trace.
if (!CliStartup.TryValidateArgs(cliArgs) || !CliStartup.TryValidateProjectFile(cliArgs))
{
    return;
}

try
{
    await Host.CreateDefaultBuilder(cliArgs)
        .ConfigureServices(services =>
        {
            // services.AddTransient<IExampleTransientService, ExampleTransientService>();
            // services.AddScoped<IExampleScopedService, ExampleScopedService>();
            // services.AddSingleton<IExampleSingletonService, ExampleSingletonService>();
            // services.AddTransient<ServiceLifetimeReporter>();

            services.AddHostedService<TyhpHostedService>();
            services.AddLocalization(options => {
                options.ResourcesPath = "Resources";
            });

        }).ConfigureAppConfiguration((hostingContext, configuration) => {
            // here we pre-load the command line arguments to see if the user specifies
            // a different name for the project file
            configuration.Sources.Clear();
            configuration.AddCommandLine(cliArgs);
            var confRoot = configuration.Build();
            var loadProject = confRoot["tyhp-project"];

            // now we re-load the config so we can load the project file too.
            // Order: JSON defaults first, then command line (CLI wins for shared keys like quiet),
            // then ActionConfigSource last for *action / path:* / project-file metadata.
            configuration.Sources.Clear();
            configuration.AddJsonFile(loadProject ?? "./tyhp.json", optional: loadProject == null , reloadOnChange: true);
            configuration.AddCommandLine(cliArgs);
            configuration.Add(new ActionConfigSource(loadProject ?? "./tyhp.json"));
        })
        .ConfigureLogging(builder => {
            builder.AddConsole().AddSimpleConsole(options => {
                options.SingleLine = true;
                options.IncludeScopes = false;
            });
            builder.AddFilter("Microsoft", LogLevel.Error);
            builder.AddFilter("System", LogLevel.Warning);
        })
        .RunConsoleAsync();
}
catch (Exception ex) when (CliStartup.IsConfigurationFailure(ex))
{
    CliStartup.ReportConfigurationFailure(CliStartup.UnwrapConfigurationFailure(ex));
}
