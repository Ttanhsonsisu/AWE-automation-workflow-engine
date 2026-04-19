using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public interface IWebhookSignatureVerifier
{
    bool Verify(string? secretToken, IHeaderDictionary headers, string payload);
}
