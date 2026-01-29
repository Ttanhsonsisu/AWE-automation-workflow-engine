namespace AWE.ServiceDefaults.Consts;

/// <summary>
/// Defines common HTTP endpoint paths shared across services.
/// </summary>
/// <remarks>
/// These endpoints are used for health checks and service liveness
/// monitoring by infrastructure components (e.g. orchestrators,
/// load balancers, monitoring systems).
/// </remarks>
public static class EndpointPath
{
    /// <summary>
    /// Health check endpoint.
    /// </summary>
    /// <remarks>
    /// Indicates overall service health, including dependencies.
    /// Typically used by monitoring and alerting systems.
    /// </remarks>
    public const string HEALTH_ENDPOINT_PATH = "/health";

    /// <summary>
    /// Liveness check endpoint.
    /// </summary>
    /// <remarks>
    /// Indicates whether the service process is running.
    /// Does not validate external dependencies.
    /// </remarks>
    public const string ALIVENESS_ENDPOINT_PATH = "/alive";
}

