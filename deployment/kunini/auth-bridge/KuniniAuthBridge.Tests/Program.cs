// SPDX-License-Identifier: GPL-2.0-only

using Jellyfin.Plugin.KuniniAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Net;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var id = Guid.Parse("cd56b8c8-8282-48db-a010-df725f14231d");
Environment.SetEnvironmentVariable("KUNINI_AUTH_RECOVERY_ADDRESSES", "172.30.0.1");
var validHeaders = new HeaderDictionary
{
    [Constants.UserHeader] = "dkunin",
    [Constants.UserIdHeader] = id.ToString()
};
Assert(KuniniIdentity.TryRead(validHeaders, out var validIdentity), "valid identity must parse");
Assert(validIdentity?.JellyfinUsername == "kunini-dkunin-cd56b8c8828248dba010df725f14231d", "account mapping must be stable");

var missingId = new HeaderDictionary { [Constants.UserHeader] = "dkunin" };
Assert(!KuniniIdentity.TryRead(missingId, out _), "missing id must fail closed");

var duplicatedUser = new HeaderDictionary
{
    [Constants.UserHeader] = new StringValues(["dkunin", "kate"]),
    [Constants.UserIdHeader] = id.ToString()
};
Assert(!KuniniIdentity.TryRead(duplicatedUser, out _), "duplicate identity headers must fail closed");

var invalidUsername = new HeaderDictionary
{
    [Constants.UserHeader] = "../../admin",
    [Constants.UserIdHeader] = id.ToString()
};
Assert(!KuniniIdentity.TryRead(invalidUsername, out _), "invalid username must fail closed");

var invalidId = new HeaderDictionary
{
    [Constants.UserHeader] = "dkunin",
    [Constants.UserIdHeader] = "not-a-uuid"
};
Assert(!KuniniIdentity.TryRead(invalidId, out _), "invalid stable id must fail closed");

var matchingRequest = new DefaultHttpContext();
matchingRequest.Request.Headers[Constants.UserHeader] = "dkunin";
matchingRequest.Request.Headers[Constants.UserIdHeader] = id.ToString();
Assert(KuniniRequestBinding.IsAllowed(matchingRequest.Request, validIdentity?.JellyfinUsername), "matching token must be allowed");
Assert(!KuniniRequestBinding.IsAllowed(matchingRequest.Request, "kunini-kate-other"), "another user's token must be rejected");

var loopbackRequest = new DefaultHttpContext();
loopbackRequest.Connection.RemoteIpAddress = IPAddress.Loopback;
loopbackRequest.Request.Host = new HostString("127.0.0.1", 8096);
Assert(KuniniRequestBinding.IsAllowed(loopbackRequest.Request, "dkunin"), "direct loopback recovery must remain available");

var dockerRecoveryRequest = new DefaultHttpContext();
dockerRecoveryRequest.Connection.RemoteIpAddress = IPAddress.Parse("172.30.0.1");
dockerRecoveryRequest.Request.Host = new HostString("127.0.0.1", 8096);
Assert(KuniniRequestBinding.IsAllowed(dockerRecoveryRequest.Request, "dkunin"), "explicit Docker gateway recovery must be allowed");

var untrustedRecoveryRequest = new DefaultHttpContext();
untrustedRecoveryRequest.Connection.RemoteIpAddress = IPAddress.Parse("172.30.0.9");
untrustedRecoveryRequest.Request.Host = new HostString("127.0.0.1", 8096);
Assert(!KuniniRequestBinding.IsAllowed(untrustedRecoveryRequest.Request, "dkunin"), "unlisted bridge source must be rejected");

var proxiedRequestWithoutIdentity = new DefaultHttpContext();
proxiedRequestWithoutIdentity.Connection.RemoteIpAddress = IPAddress.Loopback;
proxiedRequestWithoutIdentity.Request.Host = new HostString("media.kunini.ru");
Assert(!KuniniRequestBinding.IsAllowed(proxiedRequestWithoutIdentity.Request, "dkunin"), "proxied request without identity must fail closed");

Console.WriteLine("Kunini auth bridge tests passed: 11");
