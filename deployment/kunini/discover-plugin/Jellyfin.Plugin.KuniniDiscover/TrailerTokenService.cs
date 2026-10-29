// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Creates short-lived signed URLs for anonymous browser media requests.
/// </summary>
public sealed class TrailerTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a process-local signing key. Existing trailer URLs expire naturally after a restart.
    /// </summary>
    public TrailerTokenService()
        : this(RandomNumberGenerator.GetBytes(32), TimeProvider.System)
    {
    }

    internal TrailerTokenService(byte[] key, TimeProvider timeProvider)
    {
        _key = key.Length >= 32 ? key.ToArray() : throw new ArgumentException("Trailer signing key is too short.", nameof(key));
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Builds a signed same-origin path for one validated YouTube video identifier.
    /// </summary>
    /// <param name="videoId">The YouTube video identifier.</param>
    /// <returns>The signed path, or <see langword="null"/> for an invalid identifier.</returns>
    public string? CreatePath(string? videoId)
    {
        if (!IsValidVideoId(videoId))
        {
            return null;
        }

        var expires = _timeProvider.GetUtcNow().Add(TokenLifetime).ToUnixTimeSeconds();
        var signature = Sign(videoId!, expires);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"/KuniniDiscover/trailer/{videoId}?expires={expires}&signature={signature}");
    }

    /// <summary>
    /// Validates a signed trailer request without exposing a Jellyfin access token in the media URL.
    /// </summary>
    public bool IsValid(string? videoId, long expires, string? signature)
    {
        if (!IsValidVideoId(videoId)
            || string.IsNullOrWhiteSpace(signature)
            || expires < _timeProvider.GetUtcNow().ToUnixTimeSeconds())
        {
            return false;
        }

        var maximumExpiry = _timeProvider.GetUtcNow().Add(TokenLifetime).ToUnixTimeSeconds() + 5;
        if (expires > maximumExpiry)
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = WebEncoders.Base64UrlDecode(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = SignatureBytes(videoId!, expires);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    internal static bool IsValidVideoId(string? videoId)
        => videoId is { Length: 11 } && videoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private string Sign(string videoId, long expires)
        => WebEncoders.Base64UrlEncode(SignatureBytes(videoId, expires));

    private byte[] SignatureBytes(string videoId, long expires)
    {
        var payload = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{videoId}:{expires}"));
        return HMACSHA256.HashData(_key, payload);
    }
}
