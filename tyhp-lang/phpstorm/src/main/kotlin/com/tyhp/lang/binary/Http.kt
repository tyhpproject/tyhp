package com.tyhp.lang.binary

import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.nio.file.Path
import java.time.Duration

const val USER_AGENT = "tyhp-lang-phpstorm"

class HttpError(message: String, val statusCode: Int? = null) : RuntimeException(message)

fun githubApiHeaders(): Map<String, String> {
    val headers = linkedMapOf(
        "User-Agent" to USER_AGENT,
        "Accept" to "application/vnd.github+json",
        "X-GitHub-Api-Version" to "2022-11-28",
    )
    val token = (System.getenv("GITHUB_TOKEN") ?: System.getenv("GH_TOKEN")).orEmpty().trim()
    if (token.isNotEmpty()) {
        headers["Authorization"] = "Bearer $token"
    }
    return headers
}

fun downloadHeaders(): Map<String, String> {
    val headers = linkedMapOf(
        "User-Agent" to USER_AGENT,
        "Accept" to "application/octet-stream",
    )
    val token = (System.getenv("GITHUB_TOKEN") ?: System.getenv("GH_TOKEN")).orEmpty().trim()
    if (token.isNotEmpty()) {
        headers["Authorization"] = "Bearer $token"
    }
    return headers
}

private val client: HttpClient = HttpClient.newBuilder()
    .followRedirects(HttpClient.Redirect.NORMAL)
    .connectTimeout(Duration.ofSeconds(30))
    .build()

fun httpGetText(
    urlString: String,
    headers: Map<String, String> = githubApiHeaders(),
    timeoutMs: Long = 30_000,
): String {
    val request = request(urlString, headers, timeoutMs)
    val response = try {
        client.send(request, HttpResponse.BodyHandlers.ofString())
    } catch (err: Exception) {
        throw HttpError("Network error: ${err.message ?: err}")
    }
    val status = response.statusCode()
    if (status < 200 || status >= 300) {
        val hint = response.body().trim().take(240)
        throw HttpError(
            "HTTP $status fetching $urlString${if (hint.isNotEmpty()) ": $hint" else ""}",
            status,
        )
    }
    return response.body()
}

fun httpDownloadFile(
    urlString: String,
    destPath: Path,
    headers: Map<String, String> = downloadHeaders(),
    timeoutMs: Long = 180_000,
) {
    val request = request(urlString, headers, timeoutMs)
    val response = try {
        client.send(request, HttpResponse.BodyHandlers.ofFile(destPath))
    } catch (err: Exception) {
        throw HttpError("Network error: ${err.message ?: err}")
    }
    val status = response.statusCode()
    if (status < 200 || status >= 300) {
        throw HttpError("HTTP $status downloading $urlString", status)
    }
}

fun httpGetJsonText(urlString: String): String = httpGetText(urlString, githubApiHeaders())

private fun request(urlString: String, headers: Map<String, String>, timeoutMs: Long): HttpRequest {
    val uri = try {
        URI.create(urlString)
    } catch (_: Exception) {
        throw HttpError("Invalid URL: $urlString")
    }
    val builder = HttpRequest.newBuilder(uri)
        .GET()
        .timeout(Duration.ofMillis(timeoutMs))
    for ((key, value) in headers) {
        builder.header(key, value)
    }
    return builder.build()
}
