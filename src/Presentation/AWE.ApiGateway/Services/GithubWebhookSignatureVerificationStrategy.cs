using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public class GithubWebhookSignatureVerificationStrategy : IWebhookSignatureVerificationStrategy
{
    private const string HeaderName = "X-Hub-Signature-256";
    private const string Prefix = "sha256=";

    public bool CanHandle(IHeaderDictionary headers)
        => headers.ContainsKey(HeaderName);

    public bool Verify(string secretToken, IHeaderDictionary headers, string payload)
    {
        if (!headers.TryGetValue(HeaderName, out var signatureHeader))
        {
            return false;
        }

        var signature = signatureHeader.ToString().Trim();
        if (!signature.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var receivedHex = signature[Prefix.Length..].Trim();
        var expectedHex = ComputeHmacSha256(secretToken, payload);
        return FixedTimeEqualsText(expectedHex, receivedHex);
    }

    private static string ComputeHmacSha256(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEqualsText(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
