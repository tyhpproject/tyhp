using System.Diagnostics;
using System.Security.Cryptography;

namespace Tyhp.CLI
{
    /// <summary>
    /// Downloads the official Composer installer and its SHA-384 signature.
    /// Tests inject a fake so <c>dotnet test</c> never contacts getcomposer.org.
    /// </summary>
    internal interface IComposerInstallerTransport
    {
        Task<byte[]?> DownloadInstallerAsync(CancellationToken cancellationToken);

        Task<string?> DownloadInstallerSignatureAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Runs <c>php composer-setup.php --install-dir … --filename …</c>.
    /// </summary>
    internal interface IComposerSetupRunner
    {
        int Run(
            string phpPath,
            string setupScriptPath,
            string installDir,
            string fileName,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Official Composer installer URLs and SHA-384 verification. Never skip the checksum.
    /// </summary>
    internal static class ComposerInstallerChecksum
    {
        /// <summary>Documented installer script from https://getcomposer.org/download/.</summary>
        public const string InstallerUrl = "https://getcomposer.org/installer";

        /// <summary>Published SHA-384 of the installer (hex).</summary>
        public const string SignatureUrl = "https://composer.github.io/installer.sig";

        public static string Sha384Hex(ReadOnlySpan<byte> bytes)
            => Convert.ToHexString(SHA384.HashData(bytes)).ToLowerInvariant();

        public static bool Matches(byte[] installer, string? signatureHex)
        {
            if (installer.Length == 0 || string.IsNullOrWhiteSpace(signatureHex))
            {
                return false;
            }

            return string.Equals(
                Sha384Hex(installer),
                signatureHex.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ComposerInstallerTransport : IComposerInstallerTransport
    {
        internal const string UserAgent = "Tyhp-install-composer";

        private readonly HttpClient _http;

        public ComposerInstallerTransport(HttpClient? http = null)
        {
            this._http = http ?? CreateDefaultClient();
        }

        public static HttpClient CreateDefaultClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
            })
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        public async Task<byte[]?> DownloadInstallerAsync(CancellationToken cancellationToken)
        {
            return await this.GetBytesAsync(
                new Uri(ComposerInstallerChecksum.InstallerUrl),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<string?> DownloadInstallerSignatureAsync(CancellationToken cancellationToken)
        {
            return await this.GetStringAsync(
                new Uri(ComposerInstallerChecksum.SignatureUrl),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<byte[]?> GetBytesAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await this._http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return null;
            }
        }

        private async Task<string?> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await this._http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return null;
            }
        }
    }

    internal sealed class ComposerSetupRunner : IComposerSetupRunner
    {
        public int Run(
            string phpPath,
            string setupScriptPath,
            string installDir,
            string fileName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = phpPath,
                WorkingDirectory = installDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(setupScriptPath);
            startInfo.ArgumentList.Add("--install-dir=" + installDir);
            startInfo.ArgumentList.Add("--filename=" + fileName);

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return 1;
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                process.WaitForExit();
                _ = stdoutTask.GetAwaiter().GetResult();
                _ = stderrTask.GetAwaiter().GetResult();
                return process.ExitCode;
            }
            catch (Exception ex) when (ex is IOException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                return 1;
            }
        }
    }
}
