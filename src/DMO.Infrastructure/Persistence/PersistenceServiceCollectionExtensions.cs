using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Application.Boquilhas;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Infrastructure.Persistence.Access;
using DMO.Infrastructure.Persistence.Boquilhas;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// Registers the P1-T03 persistence foundation: the persistence-backed account/authentication
/// lookups and the repository primitives.
/// </summary>
/// <remarks>
/// All registrations are scoped to the request and consume the single
/// <see cref="DmoDbContext"/>. <see cref="IAccountLookup"/> now resolves to
/// <see cref="PersistenceAccountLookup"/> (the P1-T02 <c>UnavailableAccountLookup</c> is
/// deleted); the USER authentication boundary consumes the narrow
/// <see cref="IUserAuthenticationLookup"/> contract, never EF directly.
/// </remarks>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the persistence-backed lookups and repositories.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPersistenceFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAccountLookup, PersistenceAccountLookup>();
        services.AddScoped<IUserAuthenticationLookup, UserAuthenticationLookup>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAdminAccountRepository, AdminAccountRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<ITemplateModuleRepository, TemplateModuleRepository>();

        // ---- P2-T04 domain core: canonical Tool + Job On production occurrence ----------------
        // Additive registrations only. The delete flow automatically includes every registered
        // dependency probe without the Job On module knowing a later module's domain.
        services.AddScoped<IToolRepository, ToolRepository>();
        services.AddScoped<IJobOnRepository, JobOnRepository>();
        services.AddScoped<IJobOnDependencyProbe, JobOnLineageDependencyProbe>();

        // ---- P2-T05 Controlo Create: Peso core + Definições settings -------------------------
        // Additive registrations only (P2-T05 contract §20/§21.6): the P2-T05 repositories, the
        // read-only cm-context traversal (DmoPesoContextRead), the Peso outputs read
        // (DmoPesoOutputRead — the jobon_id → peso_id seam of the outputs slice), the server-host
        // directory probe and the Peso delete-dependency probe (one additive line per contributing
        // module, P2-T04 §11.5 seam).
        services.AddScoped<IPesoRepository, PesoRepository>();
        services.AddScoped<IRepairerRepository, RepairerRepository>();
        services.AddScoped<IMachineRepairerAssignmentRepository, MachineRepairerAssignmentRepository>();
        services.AddScoped<IPdfDirectorySettingsRepository, PdfDirectorySettingsRepository>();
        services.AddScoped<IEmailListRepository, EmailListRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IGlassDensitySettingsRepository, GlassDensitySettingsRepository>();
        services.AddScoped<IPesoContextRead, DmoPesoContextRead>();
        services.AddScoped<IPesoOutputRead, DmoPesoOutputRead>();
        services.AddScoped<IProductionResumoRead, DmoProductionResumoRead>();
        services.AddScoped<IJobOnDependencyProbe, PesoJobOnDependencyProbe>();

        // ---- P2-T06 Controlo Approve: the review decision core --------------------------------
        // Additive registrations only (P2-T06 contract §8/§9): the review repository (pending/
        // history queries + the atomic decision write). The application service is registered in
        // DMO.Web/Program.cs with the shared read composition.
        services.AddScoped<IPesoReviewRepository, PesoReviewRepository>();

        // ---- Peso Comparação (optional child of the initial Peso): the Comparação repository -----
        // Additive registrations only: the Comparação aggregate lives in its OWN three tables
        // (header, compared-CM subjects, measurement rows); the initial Peso rows are never
        // written by it, so a Peso with no Comparação behaves exactly as today. The application
        // service is registered in DMO.Web/Program.cs.
        services.AddScoped<IComparacaoRepository, ComparacaoRepository>();

        // ---- P2-T07 Boquilhas: the aggregate + movement ledger + lifecycle ----------------------
        // Additive registrations only (P2-T07 contract §8.1/§27): the Boquilhas repository, the
        // narrow read-only bq-context traversal seam and the Boquilhas delete-dependency probe
        // (one additive line per contributing module, P2-T04 §11.5 seam). The application service
        // is registered in DMO.Web/Program.cs.
        services.AddScoped<IBoquilhasRepository, BoquilhasRepository>();
        services.AddScoped<IBoquilhasContextRead, DmoBoquilhasContextRead>();
        services.AddScoped<IJobOnDependencyProbe, BoquilhasDependencyProbe>();

        return services;
    }
}