namespace DMO.Web.Endpoints.Core;

/// <summary>
/// Minimal technical runtime surface.
/// </summary>
/// <remarks>
/// <para>
/// This is a liveness/startup endpoint only. It exists so the skeleton can be verified as
/// mechanically running, and it deliberately exposes no product information: no account,
/// no Template, no Module, no industrial data.
/// </para>
/// <para>
/// It reports <c>status</c> and <c>environment</c> only. It does not touch the database, so
/// it never becomes an implicit database health or configuration probe.
/// </para>
/// </remarks>
public static class TechnicalEndpoints
{
    /// <summary>Path of the technical startup endpoint.</summary>
    public const string HealthPath = "/health";

    /// <summary>Maps the technical endpoints onto the application.</summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapTechnicalEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(HealthPath, (IHostEnvironment environment) => Results.Ok(new HealthResponse(
            Status: "ok",
            Environment: environment.EnvironmentName)));

        return app;
    }

    /// <summary>Payload returned by the technical startup endpoint.</summary>
    /// <param name="Status">Fixed liveness indicator.</param>
    /// <param name="Environment">Host environment name, for operator diagnostics.</param>
    public sealed record HealthResponse(string Status, string Environment);
}
