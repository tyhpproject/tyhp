using System.ComponentModel;
using Tyhp.CLI;
using Tyhp.Extensions;

namespace Tyhp.Config
{
    static class DisplayHelp
    {
        public static void Execute()
        {
            // Use the same parse the action router uses so `--subject=DUMP-AST` resolves exactly
            // like `tyhp dump-ast --help` does.
            if (!ActionConfigProvider.TryParseAction(Project.Singleton?.Subject, out Tyhp.Config.Action subjectAction)) {
                subjectAction = Tyhp.Config.Action.invalid;
            }

            switch (subjectAction) {
                case Tyhp.Config.Action.invalid:
                    DisplayHelp.GeneralHelp();
                    break;
                case Tyhp.Config.Action.help:
                    DisplayHelp.HelpHelp();
                    break;
                case Tyhp.Config.Action.version:
                    DisplayHelp.VersionHelp();
                    break;
                case Tyhp.Config.Action.explain:
                    DisplayHelp.ExplainHelp();
                    break;
                case Tyhp.Config.Action.init:
                    DisplayHelp.InitHelp();
                    break;
                case Tyhp.Config.Action.lint:
                    DisplayHelp.LintHelp();
                    break;
                case Tyhp.Config.Action.tokenize:
                    DisplayHelp.TokenizeHelp();
                    break;
                case Tyhp.Config.Action.dump_ast:
                    DisplayHelp.DumpAstHelp();
                    break;
                case Tyhp.Config.Action.symbol_tree:
                    DisplayHelp.SymbolTreeHelp();
                    break;
                case Tyhp.Config.Action.build:
                    DisplayHelp.BuildHelp();
                    break;
                case Tyhp.Config.Action.composer:
                    DisplayHelp.ComposerHelp();
                    break;
                case Tyhp.Config.Action.install:
                    DisplayHelp.InstallHelp();
                    break;
                case Tyhp.Config.Action.language_server:
                    DisplayHelp.LanguageServerHelp();
                    break;
                case Tyhp.Config.Action.xdebug_proxy:
                    DisplayHelp.XDebugProxyHelp();
                    break;
                case Tyhp.Config.Action.generate_tyhpdef:
                    DisplayHelp.GenerateTyhpdefHelp();
                    break;
                case Tyhp.Config.Action.overlay:
                    DisplayHelp.OverlayHelp();
                    break;
                case Tyhp.Config.Action.integrity_check:
                    DisplayHelp.IntegrityCheckHelp();
                    break;
                case Tyhp.Config.Action.clear_cache:
                    DisplayHelp.ClearCacheHelp();
                    break;
                // Every action is reachable via the universal `--help` alias, so an action without a
                // dedicated help method must still print something rather than exiting silently.
                default:
                    DisplayHelp.GeneralHelp();
                    break;
            }
        }

        /// <summary>
        /// Public actions shown in general / help-about-help listings. Internal actions
        /// (<see cref="Tyhp.Config.Action.invalid"/>, <see cref="Tyhp.Config.Action.debug"/>) are omitted.
        /// </summary>
        private static bool IsPublicHelpAction(Tyhp.Config.Action action)
            => action != Tyhp.Config.Action.invalid && action != Tyhp.Config.Action.debug;

        private static void GeneralHelp()
        {
            string executable = HelpFormatting.GetExecutableName();
            Message.Info("CLI_HelpSyntax", executable);
            HelpFormatting.Section("CLI_HelpActionsHeader");
            foreach (var a in Enum.GetValues<Tyhp.Config.Action>()) {
                if (!IsPublicHelpAction(a)) {
                    continue;
                }

                string helpStr = a.GetAttribute<DescriptionAttribute>()?.Description ?? "";

                Message.Display("CLI_HelpActionLine", a.ToString().PadRight(26), helpStr);
            }

            Message.Display("");
            Message.Info("CLI_HelpActionSpecific");
            Message.Info("CLI_HelpActionSpecificCommand", executable);
            Message.Info("CLI_HelpActionSpecificAlias");
            Message.Info("CLI_HelpActionSpecificAliasCommand", executable);
            Message.Info("CLI_HelpGeneralHelpAlias");
            Message.Info("CLI_HelpGeneralHelpAliasCommand", executable);
        }

        private static void HelpHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_HelpHelpDescription");
            HelpFormatting.Section("CLI_HelpHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_HelpHelpUsageSubject");
            HelpFormatting.Usage(executable, "CLI_HelpHelpUsageFlag");
            HelpFormatting.Usage(executable, "CLI_HelpHelpUsageGeneral");

            HelpFormatting.Section("CLI_HelpHelpOptionsHeader");
            HelpFormatting.Option("--subject=<action>", "CLI_HelpHelpOptionSubject");
            HelpFormatting.Option("--help", "CLI_HelpHelpOptionHelp");

            HelpFormatting.Section("CLI_HelpHelpActionsHeader");
            foreach (var a in Enum.GetValues<Tyhp.Config.Action>())
            {
                if (!IsPublicHelpAction(a))
                {
                    continue;
                }

                string helpStr = a.GetAttribute<DescriptionAttribute>()?.Description ?? "";
                Message.Display("CLI_HelpActionLine", a.ToString().PadRight(26), helpStr);
            }

            HelpFormatting.Section("CLI_HelpHelpExamplesHeader");
            HelpFormatting.Example($"{executable} help --subject=build", "CLI_HelpHelpExampleSubject");
            HelpFormatting.Example($"{executable} build --help", "CLI_HelpHelpExampleFlag");
            HelpFormatting.Example($"{executable} help --subject=init", "CLI_HelpHelpExampleInit");
            HelpFormatting.Example($"{executable} --help", "CLI_HelpHelpExampleGeneral");
        }

        private static void VersionHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_VersionHelpDescription");
            HelpFormatting.Section("CLI_VersionHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_VersionHelpUsage");

            HelpFormatting.Section("CLI_VersionHelpOptionsHeader");
            HelpFormatting.Option("--json", "CLI_VersionHelpOptionJson");
            HelpFormatting.Option("--help", "CLI_VersionHelpOptionHelp");

            HelpFormatting.Section("CLI_VersionHelpExamplesHeader");
            HelpFormatting.Example($"{executable} version", "CLI_VersionHelpExampleHuman");
            HelpFormatting.Example($"{executable} version --json", "CLI_VersionHelpExampleJson");
            HelpFormatting.Example($"{executable} version --help", "CLI_VersionHelpExampleHelp");

            HelpFormatting.Section("CLI_VersionHelpSampleHeader");
            Message.Display("CLI_VersionHelpSampleLine1");
            Message.Display("CLI_VersionHelpSampleLine2");
            Message.Display("CLI_VersionHelpSampleLine3");
            Message.Display("CLI_VersionHelpSampleLine4");
        }

        private static void ExplainHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_ExplainHelpDescription");
            HelpFormatting.Section("CLI_ExplainHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_ExplainHelpUsageVerb");
            HelpFormatting.Usage(executable, "CLI_ExplainHelpUsageFlag");
            HelpFormatting.Usage(executable, "CLI_ExplainHelpUsageCodeFlag");

            HelpFormatting.Section("CLI_ExplainHelpOptionsHeader");
            HelpFormatting.Option("--code=<TYHP####>", "CLI_ExplainHelpOptionCode");
            HelpFormatting.Option("--explain <TYHP####>", "CLI_ExplainHelpOptionExplainAlias");
            HelpFormatting.Option("--help", "CLI_ExplainHelpOptionHelp");

            HelpFormatting.Section("CLI_ExplainHelpExamplesHeader");
            HelpFormatting.Example($"{executable} --explain TYHP4008", "CLI_ExplainHelpExampleFlag");
            HelpFormatting.Example($"{executable} explain 4008", "CLI_ExplainHelpExampleBare");
            HelpFormatting.Example($"{executable} explain --code=TYHP3003", "CLI_ExplainHelpExampleCode");
            HelpFormatting.Example($"{executable} explain --help", "CLI_ExplainHelpExampleHelp");
        }

        private static void InitHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_InitHelpDescription");
            HelpFormatting.Section("CLI_InitHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_InitHelpUsage");

            HelpFormatting.Section("CLI_InitHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_InitHelpOptionHelp");
            HelpFormatting.Option("--yes / -y", "CLI_InitHelpOptionYes");
            // Only basic is delivered in Story 13; laravel/symfony are planned/future.
            HelpFormatting.Option("--template=<basic>", "CLI_InitHelpOptionTemplate");
            HelpFormatting.Option("--src=<path>", "CLI_InitHelpOptionSrc");
            HelpFormatting.Option("--output=<path>", "CLI_InitHelpOptionOutput");
            HelpFormatting.Option("--namespace=<prefix>", "CLI_InitHelpOptionNamespace");
            HelpFormatting.Option("--php-version=<version>", "CLI_InitHelpOptionPhpVersion");

            HelpFormatting.Section("CLI_InitHelpExamplesHeader");
            HelpFormatting.Example($"{executable} init", "CLI_InitHelpExampleDefault");
            HelpFormatting.Example($"{executable} init --help", "CLI_InitHelpExampleHelp");
            HelpFormatting.Example($"{executable} init ./my-project --yes", "CLI_InitHelpExampleYes");
            HelpFormatting.Example($"{executable} init --template=basic", "CLI_InitHelpExampleTemplate");
        }

        private static void LintHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_LintHelpDescription");
            HelpFormatting.Section("CLI_LintHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_LintHelpUsage");

            HelpFormatting.Section("CLI_LintHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_LintHelpOptionHelp");
            HelpFormatting.Option("--include=<glob>", "CLI_LintHelpOptionInclude");
            HelpFormatting.Option("--exclude=<glob>", "CLI_LintHelpOptionExclude");
            HelpFormatting.Option("--quiet", "CLI_LintHelpOptionQuiet");
            HelpFormatting.Option("--format=<text|json|sarif>", "CLI_LintHelpOptionFormat");
            HelpFormatting.Option("--file=<path>", "CLI_LintHelpOptionFile");
            // PLACEHOLDER_STORY_12: auto-fix mode is experimental / stub-backed for some codes
            HelpFormatting.Option("--fix", "CLI_LintHelpOptionFix");
            HelpFormatting.Option("--max-fix-iterations=<n>", "CLI_LintHelpOptionMaxFixIterations");
            HelpFormatting.Option("--strict", "CLI_LintHelpOptionStrict");
            HelpFormatting.Option("--cache-dir=<path>", "CLI_LintHelpOptionCacheDir");
            HelpFormatting.Option("--no-cache", "CLI_LintHelpOptionNoCache");
            HelpFormatting.Option("--suppress-warnings=<TYHP####,…>", "CLI_LintHelpOptionSuppressWarnings");

            HelpFormatting.Section("CLI_LintHelpFormatsHeader");
            HelpFormatting.Option("text", "CLI_LintHelpFormatText");
            HelpFormatting.Option("json", "CLI_LintHelpFormatJson");
            HelpFormatting.Option("sarif", "CLI_LintHelpFormatSarif");

            HelpFormatting.Section("CLI_LintHelpExitCodesHeader");
            Message.Display("CLI_LintHelpExitCode0");
            Message.Display("CLI_LintHelpExitCode1");
            Message.Display("CLI_LintHelpExitCode4");
            Message.Display("CLI_LintHelpExitCode5");

            HelpFormatting.Section("CLI_LintHelpExamplesHeader");
            HelpFormatting.Example($"{executable} lint", "CLI_LintHelpExampleAll");
            HelpFormatting.Example($"{executable} lint --help", "CLI_LintHelpExampleHelp");
            HelpFormatting.Example($"{executable} lint src/Models", "CLI_LintHelpExamplePaths");
            HelpFormatting.Example($"{executable} lint --file=src/MyClass.tyhp", "CLI_LintHelpExampleFile");
            HelpFormatting.Example($"{executable} lint --format=json", "CLI_LintHelpExampleJson");
            HelpFormatting.Example($"{executable} lint --format=sarif", "CLI_LintHelpExampleSarif");
        }

        private static void TokenizeHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_TokenizeHelpDescription");
            HelpFormatting.Section("CLI_TokenizeHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_TokenizeHelpUsage");

            HelpFormatting.Section("CLI_TokenizeHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_TokenizeHelpOptionHelp");
            HelpFormatting.Option("--mode=<php|tyhp|tyhpdef>", "CLI_TokenizeHelpOptionMode");
            HelpFormatting.Option("--out=<file.json>", "CLI_TokenizeHelpOptionOut");
            HelpFormatting.Option("--quiet / -q", "CLI_TokenizeHelpOptionQuiet");

            HelpFormatting.Section("CLI_TokenizeHelpExamplesHeader");
            HelpFormatting.Example($"{executable} tokenize src/index.tyhp", "CLI_TokenizeHelpExampleFile");
            HelpFormatting.Example($"{executable} tokenize --mode=tyhpdef", "CLI_TokenizeHelpExampleMode");
            HelpFormatting.Example($"{executable} tokenize --help", "CLI_TokenizeHelpExampleHelp");
        }

        private static void DumpAstHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_DumpAstHelpDescription");
            HelpFormatting.Section("CLI_DumpAstHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_DumpAstHelpUsage");

            HelpFormatting.Section("CLI_DumpAstHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_DumpAstHelpOptionHelp");
            HelpFormatting.Option("--mode=<php|tyhp|tyhpdef>", "CLI_DumpAstHelpOptionMode");
            HelpFormatting.Option("--out=<file.json>", "CLI_DumpAstHelpOptionOut");
            HelpFormatting.Option("--quiet / -q", "CLI_DumpAstHelpOptionQuiet");

            HelpFormatting.Section("CLI_DumpAstHelpExamplesHeader");
            HelpFormatting.Example($"{executable} dump_ast src/index.tyhp", "CLI_DumpAstHelpExampleFile");
            HelpFormatting.Example($"{executable} dump_ast --mode=tyhp", "CLI_DumpAstHelpExampleMode");
            HelpFormatting.Example($"{executable} dump_ast --help", "CLI_DumpAstHelpExampleHelp");
        }

        private static void SymbolTreeHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_SymbolTreeHelpDescription");
            HelpFormatting.Section("CLI_SymbolTreeHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_SymbolTreeHelpUsage");

            HelpFormatting.Section("CLI_SymbolTreeHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_SymbolTreeHelpOptionHelp");
            HelpFormatting.Option("--filter=<substring>", "CLI_SymbolTreeHelpOptionFilter");
            HelpFormatting.Option("--out=<file.json>", "CLI_SymbolTreeHelpOptionOut");
            HelpFormatting.Option("--quiet / -q", "CLI_SymbolTreeHelpOptionQuiet");

            HelpFormatting.Section("CLI_SymbolTreeHelpExamplesHeader");
            HelpFormatting.Example($"{executable} symbol_tree", "CLI_SymbolTreeHelpExampleProject");
            HelpFormatting.Example($"{executable} symbol_tree --filter=strtoupper", "CLI_SymbolTreeHelpExampleFilter");
            HelpFormatting.Example($"{executable} symbol_tree --out=symbols.json", "CLI_SymbolTreeHelpExampleOut");
            HelpFormatting.Example($"{executable} symbol_tree --help", "CLI_SymbolTreeHelpExampleHelp");
        }

        private static void IntegrityCheckHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_IntegrityCheckHelpDescription");
            HelpFormatting.Section("CLI_IntegrityCheckHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_IntegrityCheckHelpUsage");

            HelpFormatting.Section("CLI_IntegrityCheckHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_IntegrityCheckHelpOptionHelp");
            HelpFormatting.Option("--verbose", "CLI_IntegrityCheckHelpOptionVerbose");
            HelpFormatting.Option("--quiet / -q", "CLI_IntegrityCheckHelpOptionQuiet");

            HelpFormatting.Section("CLI_IntegrityCheckHelpExamplesHeader");
            HelpFormatting.Example($"{executable} integrity_check", "CLI_IntegrityCheckHelpExampleDefault");
            HelpFormatting.Example($"{executable} integrity_check --verbose", "CLI_IntegrityCheckHelpExampleVerbose");
            HelpFormatting.Example($"{executable} integrity_check --help", "CLI_IntegrityCheckHelpExampleHelp");
        }

        private static void ClearCacheHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_ClearCacheHelpDescription");
            HelpFormatting.Section("CLI_ClearCacheHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_ClearCacheHelpUsage");

            HelpFormatting.Section("CLI_ClearCacheHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_ClearCacheHelpOptionHelp");
            HelpFormatting.Option("--quiet / -q", "CLI_ClearCacheHelpOptionQuiet");

            HelpFormatting.Section("CLI_ClearCacheHelpExamplesHeader");
            HelpFormatting.Example($"{executable} clear_cache", "CLI_ClearCacheHelpExampleDefault");
            HelpFormatting.Example($"{executable} clear_cache --help", "CLI_ClearCacheHelpExampleHelp");
        }

        private static void BuildHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_BuildHelpDescription");
            HelpFormatting.Section("CLI_BuildHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_BuildHelpUsage");

            HelpFormatting.Section("CLI_BuildHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_BuildHelpOptionHelp");
            HelpFormatting.Option("--include=<glob>", "CLI_BuildHelpOptionInclude");
            HelpFormatting.Option("--exclude=<glob>", "CLI_BuildHelpOptionExclude");
            HelpFormatting.Option("--quiet", "CLI_BuildHelpOptionQuiet");
            // PLACEHOLDER_STORY_10: watch mode
            HelpFormatting.Option("--watch", "CLI_BuildHelpOptionWatch");
            HelpFormatting.Option("--clean", "CLI_BuildHelpOptionClean");
            HelpFormatting.Option("--verbose", "CLI_BuildHelpOptionVerbose");
            HelpFormatting.Option("--dry-run", "CLI_BuildHelpOptionDryRun");
            HelpFormatting.Option("--strict", "CLI_BuildHelpOptionStrict");
            HelpFormatting.Option("--fix", "CLI_BuildHelpOptionFix");
            HelpFormatting.Option("--cache-dir=<path>", "CLI_BuildHelpOptionCacheDir");
            HelpFormatting.Option("--no-cache", "CLI_BuildHelpOptionNoCache");
            HelpFormatting.Option("--suppress-warnings=<TYHP####,…>", "CLI_BuildHelpOptionSuppressWarnings");

            HelpFormatting.Section("CLI_BuildHelpConfigHeader");
            HelpFormatting.Paragraph("CLI_BuildHelpConfigIntro");
            HelpFormatting.Paragraph("CLI_BuildHelpConfigInterpolation");
            HelpFormatting.Option("include", "CLI_BuildHelpConfigInclude");
            HelpFormatting.Option("exclude", "CLI_BuildHelpConfigExclude");
            HelpFormatting.Option("suppressWarnings", "CLI_BuildHelpConfigSuppressWarnings");
            HelpFormatting.Option("output.path", "CLI_BuildHelpConfigOutputPath");
            HelpFormatting.Option("output.publishPath", "CLI_BuildHelpConfigOutputPublishPath");
            HelpFormatting.Option("output.publishClean", "CLI_BuildHelpConfigOutputPublishClean");
            HelpFormatting.Option("output.publishContent", "CLI_BuildHelpConfigOutputPublishContent");
            HelpFormatting.Option("output.namespacePrefix", "CLI_BuildHelpConfigOutputNamespacePrefix");
            HelpFormatting.Option("output.comments", "CLI_BuildHelpConfigOutputComments");
            HelpFormatting.Option("output.phpVersion", "CLI_BuildHelpConfigOutputPhpVersion");
            HelpFormatting.Option("output.strictTypes", "CLI_BuildHelpConfigOutputStrictTypes");
            HelpFormatting.Option("psr4", "CLI_BuildHelpConfigPsr4");
            HelpFormatting.Option("checker.*", "CLI_BuildHelpConfigChecker");
            HelpFormatting.Option("build.*", "CLI_BuildHelpConfigBuild");
            HelpFormatting.Option("build.strictMode", "CLI_BuildHelpConfigStrictMode");

            HelpFormatting.Section("CLI_BuildHelpExamplesHeader");
            HelpFormatting.Example($"{executable} build", "CLI_BuildHelpExampleDefault");
            HelpFormatting.Example($"{executable} build --help", "CLI_BuildHelpExampleHelp");
            HelpFormatting.Example($"{executable} build --include=\"src/**/*.tyhp\"", "CLI_BuildHelpExampleInclude");
            HelpFormatting.Example($"{executable} build --dry-run", "CLI_BuildHelpExampleDryRun");
            HelpFormatting.Example($"{executable} build --clean --verbose", "CLI_BuildHelpExampleCleanVerbose");
        }

        internal static void OverlayHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_OverlayHelpDescription");
            HelpFormatting.Section("CLI_OverlayHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_OverlayHelpUsageCreate");
            HelpFormatting.Usage(executable, "CLI_OverlayHelpUsageStamp");
            HelpFormatting.Usage(executable, "CLI_OverlayHelpUsageStampFqn");

            HelpFormatting.Section("CLI_OverlayHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_OverlayHelpOptionHelp");
            HelpFormatting.Option("--strict", "CLI_OverlayHelpOptionStrict");

            HelpFormatting.Section("CLI_OverlayHelpExamplesHeader");
            HelpFormatting.Example($"{executable} overlay create \\Iterator", "CLI_OverlayHelpExampleCreate");
            HelpFormatting.Example($"{executable} overlay stamp", "CLI_OverlayHelpExampleStamp");
            HelpFormatting.Example($"{executable} overlay stamp \\Iterator", "CLI_OverlayHelpExampleStampFqn");
        }

        /// <summary>
        /// Composer action help. Also invoked by <see cref="CLI.ComposerAction"/> when no
        /// Composer subcommand is given.
        /// </summary>
        internal static void ComposerHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_ComposerHelpDescription");
            HelpFormatting.Section("CLI_ComposerHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_ComposerHelpUsage");
            HelpFormatting.Usage(executable, "CLI_ComposerHelpUsageSync");

            HelpFormatting.Section("CLI_ComposerHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_ComposerHelpOptionHelp");
            HelpFormatting.Option("--no-tyhpdef", "CLI_ComposerHelpOptionNoTyhpdef");
            Message.Display("");
            HelpFormatting.Paragraph("CLI_ComposerHelpProxiedNote");
            HelpFormatting.Paragraph("CLI_ComposerHelpSyncNote");

            HelpFormatting.Section("CLI_ComposerHelpExamplesHeader");
            HelpFormatting.Example($"{executable} composer --help", "CLI_ComposerHelpExampleHelp");
            HelpFormatting.Example($"{executable} composer sync", "CLI_ComposerHelpExampleSync");
            HelpFormatting.Example($"{executable} composer require guzzlehttp/guzzle", "CLI_ComposerHelpExampleRequire");
            HelpFormatting.Example($"{executable} composer install", "CLI_ComposerHelpExampleInstall");
        }

        /// <summary>
        /// Install action help. Also invoked by <see cref="CLI.InstallAction"/> when no target is given.
        /// </summary>
        internal static void InstallHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_InstallHelpDescription");
            HelpFormatting.Section("CLI_InstallHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_InstallHelpUsage");
            HelpFormatting.Usage(executable, "CLI_InstallHelpUsageComposer");
            HelpFormatting.Usage(executable, "CLI_InstallHelpUsageComposerGlobal");
            HelpFormatting.Usage(executable, "CLI_InstallHelpUsageComposerLocal");

            HelpFormatting.Section("CLI_InstallHelpTargetsHeader");
            HelpFormatting.Option("composer", "CLI_InstallHelpTargetComposer");
            Message.Display("");
            HelpFormatting.Paragraph("CLI_InstallHelpKnownTargetsNote");
            HelpFormatting.Paragraph("CLI_InstallHelpVsComposerNote");

            HelpFormatting.Section("CLI_InstallHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_InstallHelpOptionHelp");
            HelpFormatting.Option("--local", "CLI_InstallHelpOptionLocal");
            HelpFormatting.Option("--global", "CLI_InstallHelpOptionGlobal");
            HelpFormatting.Option("--force", "CLI_InstallHelpOptionForce");

            HelpFormatting.Section("CLI_InstallHelpExamplesHeader");
            HelpFormatting.Example($"{executable} install composer", "CLI_InstallHelpExampleComposer");
            HelpFormatting.Example($"{executable} install composer --global", "CLI_InstallHelpExampleGlobal");
            HelpFormatting.Example($"{executable} install composer --force", "CLI_InstallHelpExampleForce");
            HelpFormatting.Example($"{executable} install --help", "CLI_InstallHelpExampleHelp");
        }

        /// <summary>
        /// Language server help. Also invoked by tests via InternalsVisibleTo.
        /// </summary>
        internal static void LanguageServerHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_LanguageServerHelpDescription");
            HelpFormatting.Section("CLI_LanguageServerHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_LanguageServerHelpUsage");

            HelpFormatting.Section("CLI_LanguageServerHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_LanguageServerHelpOptionHelp");
            HelpFormatting.Option("--stdio", "CLI_LanguageServerHelpOptionStdio");
            // PLACEHOLDER_STORY_30: tcp transport
            HelpFormatting.Option("--tcp=<port>", "CLI_LanguageServerHelpOptionTcp");
            // PLACEHOLDER_STORY_30: named-pipe transport
            HelpFormatting.Option("--pipe=<name>", "CLI_LanguageServerHelpOptionPipe");
            HelpFormatting.Option("--pid-file=<path>", "CLI_LanguageServerHelpOptionPidFile");

            HelpFormatting.Section("CLI_LanguageServerHelpExamplesHeader");
            HelpFormatting.Example($"{executable} language_server", "CLI_LanguageServerHelpExampleStdio");
            HelpFormatting.Example($"{executable} language_server --help", "CLI_LanguageServerHelpExampleHelp");
        }

        /// <summary>
        /// XDebug proxy help. Also invoked by tests via InternalsVisibleTo.
        /// </summary>
        internal static void XDebugProxyHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_XDebugProxyHelpDescription");
            HelpFormatting.Section("CLI_XDebugProxyHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_XDebugProxyHelpUsage");

            HelpFormatting.Section("CLI_XDebugProxyHelpOptionsHeader");
            HelpFormatting.Option("--ide-port=<port>", "CLI_XDebugProxyHelpOptionIdePort");
            HelpFormatting.Option("--xdebug-port=<port>", "CLI_XDebugProxyHelpOptionXdebugPort");
            HelpFormatting.Option("--sourcemap-dir=<path>", "CLI_XDebugProxyHelpOptionSourcemapDir");
            HelpFormatting.Option("--ide-key=<key>", "CLI_XDebugProxyHelpOptionIdeKey");
            HelpFormatting.Option("--log-level=<debug|info|warn|error>", "CLI_XDebugProxyHelpOptionLogLevel");
            HelpFormatting.Option("--pid-file=<path>", "CLI_XDebugProxyHelpOptionPidFile");
            HelpFormatting.Option("--help", "CLI_XDebugProxyHelpOptionHelp");

            HelpFormatting.Section("CLI_XDebugProxyHelpXdebugConfigHeader");
            HelpFormatting.Paragraph("CLI_XDebugProxyHelpXdebugConfigIntro");
            DisplayPreformatted("CLI_XDebugProxyHelpXdebugIniExample");

            HelpFormatting.Section("CLI_XDebugProxyHelpTyhpJsonHeader");
            HelpFormatting.Paragraph("CLI_XDebugProxyHelpTyhpJsonIntro");
            DisplayPreformatted("CLI_XDebugProxyHelpTyhpJsonExample");

            HelpFormatting.Section("CLI_XDebugProxyHelpLaunchJsonHeader");
            HelpFormatting.Paragraph("CLI_XDebugProxyHelpLaunchJsonIntro");
            DisplayPreformatted("CLI_XDebugProxyHelpLaunchJsonExample");

            HelpFormatting.Section("CLI_XDebugProxyHelpExamplesHeader");
            HelpFormatting.Example(
                $"{executable} xdebug_proxy --sourcemap-dir=./build/",
                "CLI_XDebugProxyHelpExampleStart");
            HelpFormatting.Example($"{executable} xdebug_proxy --help", "CLI_XDebugProxyHelpExampleHelp");
        }

        /// <summary>
        /// Prints a resx value that may contain braces (JSON/ini samples) without String.Format.
        /// </summary>
        private static void DisplayPreformatted(string key)
        {
            string text = Message.LocalizeRaw(key);
            foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                Message.Display("CLI_HelpWrappedLine", line);
            }
        }

        private static void GenerateTyhpdefHelp()
        {
            string executable = HelpFormatting.GetExecutableName();

            HelpFormatting.Paragraph("CLI_GenerateTyhpdefHelpDescription");
            HelpFormatting.Paragraph("CLI_GenerateTyhpdefHelpNotesManaged");
            HelpFormatting.Paragraph("CLI_GenerateTyhpdefHelpNotesTracks");
            HelpFormatting.Paragraph("CLI_GenerateTyhpdefHelpNotesVerify");
            HelpFormatting.Paragraph("CLI_GenerateTyhpdefHelpNotesDocs");
            HelpFormatting.Paragraph("CLI_GenerateTyhpdefHelpNotesVendor");
            HelpFormatting.Section("CLI_GenerateTyhpdefHelpUsageHeader");
            HelpFormatting.Usage(executable, "CLI_GenerateTyhpdefHelpUsage");

            HelpFormatting.Section("CLI_GenerateTyhpdefHelpOptionsHeader");
            HelpFormatting.Option("--help", "CLI_GenerateTyhpdefHelpOptionHelp");
            HelpFormatting.Option("--ext-name=<name>", "CLI_GenerateTyhpdefHelpOptionExtName");
            HelpFormatting.Option("--package-path=<path>", "CLI_GenerateTyhpdefHelpOptionPackagePath");
            HelpFormatting.Option("--source=<glob>", "CLI_GenerateTyhpdefHelpOptionSource");
            HelpFormatting.Option("--vendor[=<dir>]", "CLI_GenerateTyhpdefHelpOptionVendor");
            HelpFormatting.Option("--output=<path>", "CLI_GenerateTyhpdefHelpOptionOutput");
            HelpFormatting.Option("--output-file=<path>", "CLI_GenerateTyhpdefHelpOptionOutputFile");
            HelpFormatting.Option("--split=<mode>", "CLI_GenerateTyhpdefHelpOptionSplit");
            HelpFormatting.Option("--php=<path>", "CLI_GenerateTyhpdefHelpOptionPhp");
            HelpFormatting.Option("--php-version=<version>", "CLI_GenerateTyhpdefHelpOptionPhpVersion");
            HelpFormatting.Option("--php-targets=<list>", "CLI_GenerateTyhpdefHelpOptionPhpTargets");
            HelpFormatting.Option("--php-runtime-dir=<dir>", "CLI_GenerateTyhpdefHelpOptionPhpRuntimeDir");
            HelpFormatting.Option("--include-dev", "CLI_GenerateTyhpdefHelpOptionIncludeDev");
            HelpFormatting.Option("--locale=<locale>", "CLI_GenerateTyhpdefHelpOptionLocale");
            HelpFormatting.Option("--no-docs", "CLI_GenerateTyhpdefHelpOptionNoDocs");
            HelpFormatting.Option("--include-internal", "CLI_GenerateTyhpdefHelpOptionIncludeInternal");
            HelpFormatting.Option("--include-deprecated", "CLI_GenerateTyhpdefHelpOptionIncludeDeprecated");
            HelpFormatting.Option("--no-deprecated", "CLI_GenerateTyhpdefHelpOptionNoDeprecated");
            HelpFormatting.Option("--overwrite", "CLI_GenerateTyhpdefHelpOptionOverwrite");
            HelpFormatting.Option("--require-stubs", "CLI_GenerateTyhpdefHelpOptionRequireStubs");
            HelpFormatting.Option("--no-php", "CLI_GenerateTyhpdefHelpOptionNoPhp");
            HelpFormatting.Option("--no-php-runtime-update", "CLI_GenerateTyhpdefHelpOptionNoPhpRuntimeUpdate");
            HelpFormatting.Option("--refresh-snapshots", "CLI_GenerateTyhpdefHelpOptionRefreshSnapshots");
            HelpFormatting.Option("--validate=<path>", "CLI_GenerateTyhpdefHelpOptionValidate");
            HelpFormatting.Option("--audit-stubs=<path>", "CLI_GenerateTyhpdefHelpOptionAuditStubs");
            HelpFormatting.Option("--out=<file>", "CLI_GenerateTyhpdefHelpOptionAuditOut");
            HelpFormatting.Option("--verify", "CLI_GenerateTyhpdefHelpOptionVerify");

            HelpFormatting.Section("CLI_GenerateTyhpdefHelpExamplesHeader");
            HelpFormatting.Example($"{executable} generate_tyhpdef --ext-name=curl", "CLI_GenerateTyhpdefHelpExampleExt");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --ext-name=curl --php-version=8.3",
                "CLI_GenerateTyhpdefHelpExampleExtVersion");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --ext-name=curl --php-targets=8.2,8.3,8.4,8.5",
                "CLI_GenerateTyhpdefHelpExamplePhpTargets");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --ext-name=myext --php=/opt/php/bin/php",
                "CLI_GenerateTyhpdefHelpExamplePhp");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --package-path=./vendor/guzzlehttp/guzzle --output=./tyhpdef/",
                "CLI_GenerateTyhpdefHelpExamplePackage");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --vendor",
                "CLI_GenerateTyhpdefHelpExampleVendor");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --source=./lib/**/*.php --split=namespace",
                "CLI_GenerateTyhpdefHelpExampleSourceSplit");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --validate ./tyhpdef/",
                "CLI_GenerateTyhpdefHelpExampleValidate");
            HelpFormatting.Example(
                $"{executable} generate_tyhpdef --audit-stubs=../tyhp-runtime-src/packages/php/_tyhpdef --out=stub-audit.md",
                "CLI_GenerateTyhpdefHelpExampleAuditStubs");
            HelpFormatting.Example($"{executable} generate_tyhpdef --help", "CLI_GenerateTyhpdefHelpExampleHelp");
        }
    }
}
