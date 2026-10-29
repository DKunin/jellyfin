// SPDX-License-Identifier: GPL-2.0-only

using System.Net;
using System.Net.Http.Headers;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Opens trailer streams from the isolated internal SOCKS proxy sidecar.
/// </summary>
public sealed class TrailerProxyClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrailerProxyClient"/> class.
    /// </summary>
    public TrailerProxyClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Opens a proxied trailer response without buffering the media body.
    /// </summary>
    public async Task<HttpResponseMessage> OpenAsync(
        string videoId,
        string? range,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"stream/{Uri.EscapeDataString(videoId)}");
        if (!string.IsNullOrWhiteSpace(range))
        {
            request.Headers.Range = RangeHeaderValue.Parse(range);
        }

        return await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static bool IsExpectedStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable;
}
