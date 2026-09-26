using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Controlo.Settings;

/// <summary>
/// In-memory <see cref="IGlassDensitySettingsRepository"/> used by the unit-level fakes: it
/// mirrors the contracted store semantics (per-processo rows, version-guarded updates, nothing
/// written on staleness) and defaults to the provenance-backed initial operational values
/// (NNPB 2.4027 / PS 2.4231 g/cm³, version 1 — the sibling-settings convention).
/// </summary>
/// <remarks>
/// The fixture densities of the older formula rows (MES/WDL) are deliberately kept as the
/// arbitrary fixed values those rows prove with (2.50/2.52 etc.) — this store's default is the
/// AUTHORITATIVE bootstrap, and a test arranges any other current value through
/// <see cref="SetCurrent"/> exactly like the Definições surface would.</remarks>
internal sealed class FakeGlassDensitySettingsRepository : IGlassDensitySettingsRepository
{
    private readonly Dictionary<string, GlassDensitySetting> _settings = new(StringComparer.Ordinal)
    {
        ["NNPB"] = Seed("NNPB", 2.4027m),
        ["PS"] = Seed("PS", 2.4231m),
    };

    /// <summary>Creates the store with the authoritative bootstrap values.</summary>
    public FakeGlassDensitySettingsRepository()
    {
    }

    /// <summary>Creates the store with test-owned fixture values (per-processo).</summary>
    public FakeGlassDensitySettingsRepository(IReadOnlyDictionary<string, decimal> densities)
    {
        foreach (var (processo, density) in densities)
        {
            _settings[processo] = Seed(processo, density);
        }
    }

    /// <summary>The current value of one processo, or <c>null</c> (absent-row arrangement).</summary>
    public decimal? Current(string? processo) =>
        processo is not null && _settings.TryGetValue(processo, out var setting) ? setting.DensityGCm3 : null;

    /// <summary>Directly applies a current-value change (arranges the settings surface state).</summary>
    public void SetCurrent(string processo, decimal density)
    {
        var persisted = _settings[processo];
        _settings[processo] = persisted with { DensityGCm3 = density, Version = persisted.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
    }

    /// <summary>Removes a row (defensive absent-row arrangement).</summary>
    public void Remove(string processo) => _settings.Remove(processo);

    /// <summary>Bumps the version directly (stale-version arrangement).</summary>
    public void BumpVersion(string processo)
    {
        var persisted = _settings[processo];
        _settings[processo] = persisted with { Version = persisted.Version + 1 };
    }

    public Task<GlassDensitySetting?> GetByProcessoAsync(string processo, CancellationToken cancellationToken) =>
        Task.FromResult(_settings.TryGetValue(processo, out var setting) ? setting : null);

    public Task<IReadOnlyList<GlassDensitySetting>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GlassDensitySetting>>(
            _settings.Values
                .OrderBy(setting => setting.Processo, StringComparer.Ordinal)
                .ToList());

    public Task<GlassDensitySetting> UpdatedAsync(GlassDensitySetting setting, CancellationToken cancellationToken)
    {
        if (!_settings.TryGetValue(setting.Processo, out var persisted))
        {
            throw new ConcurrencyConflictException("The glass-density setting no longer exists.");
        }

        if (persisted.Version != setting.Version)
        {
            throw new ConcurrencyConflictException("The glass-density setting was modified concurrently.");
        }

        var updated = setting with { Version = persisted.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
        _settings[updated.Processo] = updated;

        return Task.FromResult(updated);
    }

    private static GlassDensitySetting Seed(string processo, decimal density)
    {
        var now = DateTimeOffset.UtcNow;
        return new GlassDensitySetting(processo, density, Version: 1, now, now);
    }
}