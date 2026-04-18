using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public class GenericWebhookSignatureVerificationStrategy : IWebhookSignatureVerificationStrategy
{
    private const string HeaderName = "X-Signature";
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

        if (signature.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var expectedHex = ComputeHmacSha256(secretToken, payload);
            var receivedHex = signature[Prefix.Length..].Trim();
            return FixedTimeEqualsText(expectedHex, receivedHex);
        }

        return FixedTimeEqualsText(secretToken, signature);
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
