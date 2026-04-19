using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public interface IWebhookSignatureVerificationStrategy
{
    bool CanHandle(IHeaderDictionary headers);
    bool Verify(string secretToken, IHeaderDictionary headers, string payload);
}
