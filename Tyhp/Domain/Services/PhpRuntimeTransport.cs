using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// HTTPS download helper for managed PHP artifacts and php.net manuals.
    /// </summary>
    internal interface IPhpRuntimeTransport
    {
        Task<string?> GetStringAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null);

        Task<byte[]?> GetBytesAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null);

        Task<bool> DownloadToFileAsync(
            Uri url,
            string destinationPath,
            string? expectedSha256,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null);
    }

    internal sealed class PhpRuntimeTransport : IPhpRuntimeTransport
    {
        internal const string UserAgent = "Tyhp-generate_tyhpdef";

        private readonly HttpClient _http;

        public PhpRuntimeTransport(HttpClient? http = null)
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

        public async Task<string?> GetStringAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            using var response = await this.SendAsync(url, headers, cancellationToken).ConfigureAwait(false);
            if (response is null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]?> GetBytesAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            using var response = await this.SendAsync(url, headers, cancellationToken).ConfigureAwait(false);
            if (response is null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> DownloadToFileAsync(
            Uri url,
            string destinationPath,
            string? expectedSha256,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            var bytes = await this.GetBytesAsync(url, cancellationToken, headers).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new PhpRuntimeChecksumException(actual, expectedSha256.Trim());
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var tmp = destinationPath + ".download";
            await File.WriteAllBytesAsync(tmp, bytes, cancellationToken).ConfigureAwait(false);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tmp, destinationPath);
            return true;
        }

        private async Task<HttpResponseMessage?> SendAsync(
            Uri url,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    if (string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Authorization = AuthenticationHeaderValue.Parse(value);
                    }
                    else if (string.Equals(key, "Accept", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Accept.ParseAdd(value);
                    }
                    else
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            try
            {
                return await this._http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return null;
            }
        }
    }

    internal sealed class PhpRuntimeChecksumException : Exception
    {
        public PhpRuntimeChecksumException(string actual, string expected)
            : base($"checksum mismatch (expected {expected}, got {actual})")
        {
            this.Actual = actual;
            this.Expected = expected;
        }

        public string Actual { get; }

        public string Expected { get; }
    }
}
