using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public class WebhookSignatureVerifier(IEnumerable<IWebhookSignatureVerificationStrategy> strategies) : IWebhookSignatureVerifier
{
    private readonly IReadOnlyList<IWebhookSignatureVerificationStrategy> _strategies = strategies.ToList();

    public bool Verify(string? secretToken, IHeaderDictionary headers, string payload)
    {
        if (string.IsNullOrWhiteSpace(secretToken))
        {
            return true;
        }

        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandle(headers))
            {
                return strategy.Verify(secretToken, headers, payload);
            }
        }

        return false;
    }
}
