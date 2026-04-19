using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public class StripeWebhookSignatureVerificationStrategy : IWebhookSignatureVerificationStrategy
{
    private const string HeaderName = "Stripe-Signature";

    public bool CanHandle(IHeaderDictionary headers)
        => headers.ContainsKey(HeaderName);

    public bool Verify(string secretToken, IHeaderDictionary headers, string payload)
    {
        if (!headers.TryGetValue(HeaderName, out var headerValue))
        {
            return false;
        }

        var parsed = ParseStripeSignatureHeader(headerValue.ToString());
        if (parsed.Timestamp is null || string.IsNullOrWhiteSpace(parsed.V1Signature))
        {
            return false;
        }

        var signedPayload = $"{parsed.Timestamp}.{payload}";
        var expectedV1 = ComputeHmacSha256(secretToken, signedPayload);
        return FixedTimeEqualsText(expectedV1, parsed.V1Signature);
    }

    private static (string? Timestamp, string? V1Signature) ParseStripeSignatureHeader(string header)
    {
        string? timestamp = null;
        string? v1 = null;

        var segments = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var kv = segment.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                continue;
            }

            if (string.Equals(kv[0], "t", StringComparison.OrdinalIgnoreCase))
            {
                timestamp = kv[1];
            }
            else if (string.Equals(kv[0], "v1", StringComparison.OrdinalIgnoreCase))
            {
                v1 = kv[1];
            }
        }

        return (timestamp, v1);
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
