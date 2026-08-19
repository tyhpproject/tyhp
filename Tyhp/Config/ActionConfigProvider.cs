using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Tyhp.Config
{
    public class ActionConfigProvider : IConfigurationProvider
    {
        private static string? initialAction;
        private static List<string> explicitPaths = new();
        private static List<string> remainingArgs = new();
        public static string? RawInitialAction {get; private set;}
        public static IReadOnlyList<string> ExplicitPaths => explicitPaths;

        /// <summary>
        /// The action <see cref="ReadInitialActionFromArgs"/> parsed from argv, or
        /// <see cref="Action.invalid"/> when no verb matched.
        /// </summary>
        internal static Action InitialAction
            => TryParseAction(initialAction, out Action action) ? action : Action.invalid;

        /// <summary>
        /// Argv tokens after the action verb (flags and positionals), spelled the way the user typed
        /// them. Used by actions that proxy an external CLI (e.g. <c>composer</c>) so the proxied
        /// tool receives its own flags unchanged by Tyhp config parsing.
        /// </summary>
        public static IReadOnlyList<string> RemainingArgs => remainingArgs;
        private readonly string key = "*action";
        private readonly string rawActionKey = "*raw_action";
        private readonly string tyhpProjectFilePathKey = "*project_file_path";
        private readonly string explicitPathKeyPrefix = "path:";
        private string actionString = Action.invalid.ToString();
        private string? rawActionString;
        private string TyhpProjectFilePath {get; set;}

        public ActionConfigProvider(string tyhpProjectFilePath)
        {
            this.TyhpProjectFilePath = tyhpProjectFilePath;
        }

        /// <summary>
        /// Parses the action verb from argv and records the post-verb tokens
        /// (<see cref="ExplicitPaths"/>, <see cref="RemainingArgs"/>).
        /// </summary>
        /// <param name="args">
        /// Argv after <see cref="ExpandBareBooleanFlags"/> and <see cref="RewriteHelpAlias"/>, i.e. the
        /// tokens the configuration binder sees.
        /// </param>
        /// <param name="rawArgs">
        /// The pristine argv, before either rewrite. Supplies <see cref="RemainingArgs"/> when the verb
        /// survived the rewrites; omit it to derive them from <paramref name="args"/>.
        /// </param>
        public static bool ReadInitialActionFromArgs(IEnumerable <string> args, IEnumerable<string>? rawArgs = null)
        {
            var argsList = args.ToList();
            var postActionArgs = argsList.Count > 1
                ? argsList.Skip(1).ToList()
                : new List<string>();
            explicitPaths = ExtractPositionalPaths(postActionArgs);
            remainingArgs = SelectProxyArgs(argsList, rawArgs, postActionArgs);

            if (argsList.Any()) {
                string? actionText = argsList.FirstOrDefault();
                ActionConfigProvider.RawInitialAction = actionText;
                if (TryParseAction(actionText, out Action argsAction)) {
                    ActionConfigProvider.initialAction = argsAction.ToString();
                    return true;
                }
            }

            ActionConfigProvider.initialAction = Action.invalid.ToString();
            return false;
        }

        /// <summary>
        /// Picks the argv that <see cref="RemainingArgs"/> exposes: the tokens the user actually typed
        /// when they are still recognizable, otherwise the rewritten ones.
        /// </summary>
        /// <remarks>
        /// <see cref="ExpandBareBooleanFlags"/> rewrites a bare <c>--dry-run</c> into
        /// <c>--dry-run=true</c> for the .NET command-line provider, and Symfony Console (which Composer
        /// is built on) rejects a value on a value-less option, so proxied argv has to come from the
        /// unrewritten tokens. When <see cref="RewriteHelpAlias"/> replaced the verb
        /// (<c>tyhp composer --help</c> becomes <c>help --subject=composer</c>) the raw tokens belong to a
        /// different action, so the rewritten ones are used instead.
        /// </remarks>
        private static List<string> SelectProxyArgs(
            List<string> argsList,
            IEnumerable<string>? rawArgs,
            List<string> postActionArgs)
        {
            if (rawArgs == null) {
                return postActionArgs;
            }

            var rawArgsList = rawArgs.ToList();
            if (rawArgsList.Count == 0
                || argsList.Count == 0
                || !String.Equals(rawArgsList[0], argsList[0], StringComparison.Ordinal))
            {
                return postActionArgs;
            }

            return rawArgsList.Skip(1).ToList();
        }

        // Command verbs are declared with underscores on the Action enum (e.g. dump_ast,
        // generate_tyhpdef). Accept the friendlier hyphenated form (dump-ast) on the CLI by
        // normalizing hyphens to underscores before enum parsing.
        private static string NormalizeActionText(string actionText)
            => actionText.Replace('-', '_');

        /// <summary>
        /// Parses a CLI token into an <see cref="Action"/>, accepting hyphenated and differently
        /// cased spellings.
        /// </summary>
        /// <remarks>
        /// <c>Enum.TryParse</c> also accepts the underlying numeric value, which would make
        /// <c>tyhp 2</c> run whichever action happens to sit at ordinal 2. Command verbs are always
        /// names, so tokens that do not start with a letter are rejected outright.
        /// </remarks>
        internal static bool TryParseAction(string? actionText, out Action action)
        {
            action = Action.invalid;

            if (String.IsNullOrWhiteSpace(actionText) || !Char.IsLetter(actionText[0]))
            {
                return false;
            }

            return Enum.TryParse(NormalizeActionText(actionText), true, out action)
                && Enum.IsDefined(action);
        }

        /// <summary>
        /// Bare boolean CLI flags that may be followed by an explicit <c>true</c>/<c>false</c>
        /// token. Used by <see cref="ExpandBareBooleanFlags"/> and positional-path extraction.
        /// </summary>
        internal static readonly HashSet<string> BareBooleanFlags = new(StringComparer.OrdinalIgnoreCase)
        {
            "--clean",
            "--verbose",
            "--dry-run",
            "--strict",
            "--watch",
            "--no-cache",
            "--fix",
            "--quiet",
            "--help",
            "--json",
            // Documented by Story 13 Phase 3 help (init / composer / language_server).
            "--yes",
            "--no-tyhpdef",
            "--stdio",
        };

        /// <summary>
        /// Long options that always take a value (inline <c>--flag=value</c> or space-separated
        /// <c>--flag value</c>). Used by <see cref="ExtractPositionalPaths"/> so a space-separated
        /// value is not also collected as an explicit path.
        /// </summary>
        /// <remarks>
        /// Keep in sync with documented value-taking options across actions (and with
        /// <c>ComposerAction</c>'s Tyhp-owned value flags for the globals). After
        /// <see cref="ExpandBareBooleanFlags"/>, any remaining <c>--flag</c> without <c>=</c> is
        /// also treated as value-taking by <see cref="SelectBinderArgs"/>; this set is the
        /// allowlist for path extraction so an unknown bare token after a typo'd switch is not
        /// silently swallowed when expansion was skipped.
        /// </remarks>
        internal static readonly HashSet<string> ValueTakingFlags = new(StringComparer.OrdinalIgnoreCase)
        {
            // Global
            "--tyhp-project",
            "--locale",
            "--pid-file",
            // help / explain
            "--subject",
            "--code",
            "--explain",
            // init / generate_tyhpdef
            "--template",
            "--src",
            "--output",
            "--namespace",
            "--php-version",
            "--ext-name",
            "--composer-package",
            // lint / build
            "--include",
            "--exclude",
            "--format",
            "--file",
            "--max-fix-iterations",
            "--cache-dir",
            // tokenize / dump_ast
            "--mode",
            "--out",
            // language_server
            "--tcp",
            "--pipe",
            // xdebug_proxy
            "--ide-port",
            "--xdebug-port",
            "--sourcemap-dir",
            "--ide-key",
            "--log-level",
        };

        /// <summary>
        /// Single-dash aliases for entries in <see cref="BareBooleanFlags"/>, resolved to their long
        /// spelling during expansion.
        /// </summary>
        /// <remarks>
        /// .NET's <c>CommandLineConfigurationProvider</c> throws on a single-dash switch that carries
        /// a value (<c>-y=true</c>) unless a switch mapping is registered, so a short alias has to
        /// become its long form before <c>--flag=true</c> expansion instead of being expanded in place.
        /// </remarks>
        internal static readonly Dictionary<string, string> ShortBooleanFlagAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["-y"] = "--yes",
                ["-q"] = "--quiet",
            };

        /// <summary>
        /// Expands value-less boolean flags to <c>--flag=true</c> so they do not swallow the
        /// next argv token, matching .NET <c>CommandLineConfigurationProvider</c> expectations.
        /// </summary>
        public static string[] ExpandBareBooleanFlags(string[] sourceArgs)
        {
            var expanded = new List<string>(sourceArgs.Length);
            for (var i = 0; i < sourceArgs.Length; i++)
            {
                var arg = sourceArgs[i];
                if (ShortBooleanFlagAliases.TryGetValue(arg, out var longFormFlag))
                {
                    arg = longFormFlag;
                }

                if (!BareBooleanFlags.Contains(arg))
                {
                    expanded.Add(arg);
                    continue;
                }

                // Only treat the next token as this flag's value when it is an explicit boolean literal
                // (`--clean true` / `--clean false`); otherwise the flag is bare and must not consume it.
                var next = i + 1 < sourceArgs.Length ? sourceArgs[i + 1] : null;
                var nextIsExplicitBool = next != null
                    && (string.Equals(next, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(next, "false", StringComparison.OrdinalIgnoreCase));

                if (nextIsExplicitBool)
                {
                    expanded.Add(arg + "=" + next);
                    i++; // consume the boolean literal so it is not left as a bare token
                }
                else
                {
                    expanded.Add(arg + "=true");
                }
            }

            return expanded.ToArray();
        }

        /// <summary>
        /// Rewrites <c>--explain CODE</c> / <c>--explain=CODE</c> into the <c>explain</c> action.
        /// </summary>
        /// <remarks>
        /// Equivalence:
        /// <list type="bullet">
        /// <item><c>tyhp --explain TYHP4008</c> → <c>explain --code=TYHP4008</c></item>
        /// <item><c>tyhp --explain=4008</c> → <c>explain --code=4008</c></item>
        /// </list>
        /// Positional tokens other than the code are dropped; remaining flags are kept.
        /// Call after <see cref="ExpandBareBooleanFlags"/> and before <see cref="RewriteHelpAlias"/>
        /// so <c>--explain … --help</c> becomes help-about-explain.
        /// </remarks>
        public static string[] RewriteExplainAlias(string[] args)
        {
            string? code = null;
            var explainSeen = false;
            var passthroughFlags = new List<string>(args.Length);

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--explain=", StringComparison.OrdinalIgnoreCase))
                {
                    explainSeen = true;
                    code = arg["--explain=".Length..];
                    continue;
                }

                if (string.Equals(arg, "--explain", StringComparison.OrdinalIgnoreCase))
                {
                    explainSeen = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        code = args[++i];
                    }

                    continue;
                }

                if (!arg.StartsWith('-'))
                {
                    // Drop leftover positionals (e.g. a mistaken action verb before --explain).
                    continue;
                }

                passthroughFlags.Add(arg);
            }

            if (!explainSeen)
            {
                return args;
            }

            var rewritten = new List<string>(passthroughFlags.Count + 2)
            {
                Action.explain.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(code))
            {
                // Prefer the code from --explain over any user-supplied --code=.
                passthroughFlags.RemoveAll(IsCodeToken);
                rewritten.Add("--code=" + code);
            }

            rewritten.AddRange(passthroughFlags);
            return rewritten.ToArray();
        }

        /// <summary>
        /// Rewrites <c>--help</c> / <c>--help=true</c> into the existing <c>help</c> action so every
        /// command shares one help path. <c>--help=false</c> leaves args unchanged.
        /// </summary>
        /// <remarks>
        /// Equivalence:
        /// <list type="bullet">
        /// <item><c>tyhp --help</c> → <c>help</c></item>
        /// <item><c>tyhp lint --help</c> → <c>help --subject=lint</c></item>
        /// <item><c>tyhp help --help</c> → <c>help --subject=help</c></item>
        /// </list>
        /// Positional tokens are dropped (help consumes no paths) while the remaining flags are kept
        /// so global options such as <c>--quiet</c>, <c>--locale</c> and <c>--tyhp-project</c> still
        /// apply to the help run.
        /// Call after <see cref="ExpandBareBooleanFlags"/> and before <see cref="ReadInitialActionFromArgs"/>.
        /// </remarks>
        public static string[] RewriteHelpAlias(string[] args)
        {
            var helpRequested = false;
            var helpExplicitlyFalse = false;

            foreach (var arg in args)
            {
                if (IsHelpTrueToken(arg))
                {
                    helpRequested = true;
                }
                else if (IsHelpFalseToken(arg))
                {
                    helpExplicitlyFalse = true;
                }
            }

            if (!helpRequested || helpExplicitlyFalse)
            {
                return args;
            }

            string? firstPositional = null;
            var passthroughFlags = new List<string>(args.Length);

            foreach (var arg in args)
            {
                if (IsHelpTrueToken(arg))
                {
                    continue;
                }

                if (!arg.StartsWith('-'))
                {
                    firstPositional ??= arg;
                    continue;
                }

                passthroughFlags.Add(arg);
            }

            // Without a recognizable command this stays a bare `help`, so an unknown first token
            // yields general help rather than an invalid-action error.
            var rewritten = new List<string>(passthroughFlags.Count + 2) { Action.help.ToString() };

            if (TryParseAction(firstPositional, out Action subject) && subject != Action.invalid)
            {
                // The command becomes the help subject, so any user-supplied --subject is redundant
                // and must not override it (`tyhp help --subject=build --help` means help-on-help).
                passthroughFlags.RemoveAll(IsSubjectToken);
                rewritten.Add("--subject=" + subject.ToString());
            }

            rewritten.AddRange(passthroughFlags);
            return rewritten.ToArray();
        }

        /// <summary>
        /// Drops positional tokens from the argv handed to the configuration binder.
        /// </summary>
        /// <remarks>
        /// .NET's <c>CommandLineConfigurationProvider</c> reads a leading <c>/</c> as the Windows
        /// switch prefix, so a Unix absolute path (<c>tyhp init /tmp/demo --yes</c>) is parsed as the
        /// switch <c>--tmp/demo</c> and swallows the flag behind it as its value. Positional paths
        /// reach configuration through <see cref="ExplicitPaths"/> / the <c>path:N</c> keys, so the
        /// binder never needs them.
        /// Call after <see cref="ExpandBareBooleanFlags"/>: every bare boolean is in its
        /// <c>--flag=true</c> spelling by then, so a remaining <c>--flag</c> without <c>=</c> really
        /// does take the next token as its value and both are kept.
        /// </remarks>
        public static string[] SelectBinderArgs(string[] args)
        {
            var kept = new List<string>(args.Length);
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith('-'))
                {
                    continue;
                }

                kept.Add(arg);

                // A single-dash flag with no switch mapping is ignored by the provider and takes no
                // value, so only the `--flag value` form reaches past its own token.
                if (arg.StartsWith("--", StringComparison.Ordinal)
                    && !arg.Contains('=', StringComparison.Ordinal)
                    && i + 1 < args.Length)
                {
                    kept.Add(args[i + 1]);
                    i++;
                }
            }

            return kept.ToArray();
        }

        private static bool IsHelpTrueToken(string arg)
            => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--help=true", StringComparison.OrdinalIgnoreCase);

        private static bool IsHelpFalseToken(string arg)
            => string.Equals(arg, "--help=false", StringComparison.OrdinalIgnoreCase);

        private static bool IsSubjectToken(string arg)
            => string.Equals(arg, "--subject", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--subject=", StringComparison.OrdinalIgnoreCase);

        private static bool IsCodeToken(string arg)
            => string.Equals(arg, "--code", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--code=", StringComparison.OrdinalIgnoreCase);

        private static List<string> ExtractPositionalPaths(IEnumerable<string> args)
        {
            var paths = new List<string>();
            var argsList = args as IList<string> ?? args.ToList();

            for (var i = 0; i < argsList.Count; i++)
            {
                var arg = argsList[i];
                if (String.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (arg.StartsWith('-'))
                {
                    // A long option without `=` may own the next token. Skip that token so it is
                    // not also collected as a path (`--format json src/` → path is only `src/`).
                    if (!arg.Contains('=', StringComparison.Ordinal)
                        && arg.StartsWith("--", StringComparison.Ordinal)
                        && i + 1 < argsList.Count)
                    {
                        var next = argsList[i + 1];
                        if (BareBooleanFlags.Contains(arg))
                        {
                            // `--quiet true` must not treat `true` as a positional path; a bare
                            // boolean must not swallow a real path when expansion was skipped.
                            if (String.Equals(next, "true", StringComparison.OrdinalIgnoreCase)
                                || String.Equals(next, "false", StringComparison.OrdinalIgnoreCase))
                            {
                                i++;
                            }
                        }
                        else if (ValueTakingFlags.Contains(arg) && !next.StartsWith('-'))
                        {
                            i++;
                        }
                    }

                    continue;
                }

                paths.Add(arg);
            }

            return paths;
        }
        
        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            if (String.IsNullOrWhiteSpace(parentPath)) {
                var keys = new List<string>
                {
                    this.key,
                    this.rawActionKey,
                    this.tyhpProjectFilePathKey
                };

                for (int i = 0; i < explicitPaths.Count; i++)
                {
                    keys.Add(this.explicitPathKeyPrefix + i.ToString());
                }

                return keys;
            }
            
            return Array.Empty<string>();
        }

        public IChangeToken GetReloadToken()
        {
            return NullChangeToken.Singleton;
        }

        public void Load()
        {
            this.Set(this.key, ActionConfigProvider.initialAction ?? Action.invalid.ToString());
            this.Set(this.rawActionKey, ActionConfigProvider.RawInitialAction);
            this.Set(this.tyhpProjectFilePathKey, this.TyhpProjectFilePath);

            for (int i = 0; i < explicitPaths.Count; i++)
            {
                this.Set(this.explicitPathKeyPrefix + i.ToString(), explicitPaths[i]);
            }
        }

        public void Set(string key, string? value)
        {
            if (key == this.key) {
                this.actionString = TryParseAction(value, out Action tempVal)
                    ? tempVal.ToString()
                    : Action.invalid.ToString();
            } else if (key == this.rawActionKey) {
                this.rawActionString = value;
            } else if (key == this.tyhpProjectFilePathKey) {
                this.TyhpProjectFilePath = value ?? "";
            } else if (key.StartsWith(this.explicitPathKeyPrefix, StringComparison.Ordinal)
                && int.TryParse(key.AsSpan(this.explicitPathKeyPrefix.Length), out int pathIndex)
                && pathIndex >= 0
                && pathIndex < explicitPaths.Count)
            {
                explicitPaths[pathIndex] = value ?? "";
            }
        }

        public bool TryGet(string key, out string? value)
        {
            if (key == this.key) {
                value = this.actionString;
                return true;
            } else if (key == this.rawActionKey) {
                value = this.rawActionString;
                return true;
            } else if (key == this.tyhpProjectFilePathKey) {
                value = this.TyhpProjectFilePath;
                return true;
            } else if (key.StartsWith(this.explicitPathKeyPrefix, StringComparison.Ordinal)
                && int.TryParse(key.AsSpan(this.explicitPathKeyPrefix.Length), out int pathIndex)
                && pathIndex >= 0
                && pathIndex < explicitPaths.Count)
            {
                value = explicitPaths[pathIndex];
                return true;
            }

            value = null;
            return false;
        }
    }
}