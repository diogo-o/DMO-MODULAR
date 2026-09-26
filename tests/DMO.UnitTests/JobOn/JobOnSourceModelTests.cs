using DMO.Infrastructure.Persistence.Entities;


namespace DMO.UnitTests.JobOn;

/// <summary>
/// P2-T04 source/model rows JOB23 and CTX10 (contract §20.2/§20.3, AC-33, AC-46).
/// </summary>
/// <remarks>
/// Both rows are static checks over the persistence model and the P2-T04 production sources:
/// <list type="bullet">
/// <item><b>JOB23</b> — no EF query exists outside the contracted repositories, and no module
/// queries another module's table;</item>
/// <item><b>CTX10</b> — the three context tables declare exactly the contracted columns; none of
/// the excluded facts has a column.</item>
/// </list>
/// </remarks>
public sealed class JobOnSourceModelTests
{
    /// <summary>JOB23 (AC-46) — EF queries live only in the contracted repositories.</summary>
    [Fact]
    public void JOB23_NoEfQueryExistsOutsideTheContractedRepositories_AndNoModuleQueriesAnotherModulesTable()
    {
        // The application boundary reference points: the Web layer holds no persistence query.
        var webQueryReferences = new[]
        {
            "src/DMO.Web/Endpoints/ToolJobOn/JobOnEndpoints.cs",
            "src/DMO.Web/Endpoints/ToolJobOn/FerramentasEndpoints.cs",
            "src/DMO.Web/Pages/JobOn/Index.cshtml.cs",
            "src/DMO.Web/Pages/JobOn/View.cshtml.cs",
            "src/DMO.Web/Pages/JobOn/Create.cshtml.cs",
            "src/DMO.Web/Pages/JobOn/Edit.cshtml.cs",
            "src/DMO.Web/Pages/JobOn/Duplicate.cshtml.cs",
            "src/DMO.Web/Pages/Ferramentas/Tool.cshtml.cs",
            "src/DMO.Web/Pages/JobOn/JobOnToolPickerAdapter.cs",
        };

        foreach (var path in webQueryReferences)
        {
            var source = Read(path);

            Assert.DoesNotContain("DmoDbContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Include(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FirstOrDefaultAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ToListAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Set<ToolEntity>", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Set<JobOnEntity>", source, StringComparison.Ordinal);
        }

        // Every EF query in the P2-T04 production code sits in one of the two contracted
        // repositories or in the Job On module's own dependency probe (its own table).
        var repositorySources = new[]
        {
            Read("src/DMO.Infrastructure/Persistence/ToolJobOn/ToolRepository.cs"),
            Read("src/DMO.Infrastructure/Persistence/ToolJobOn/JobOnRepository.cs"),
            Read("src/DMO.Infrastructure/Persistence/ToolJobOn/JobOnLineageDependencyProbe.cs"),
        };

        foreach (var source in repositorySources)
        {
            Assert.Contains("DmoDbContext", source, StringComparison.Ordinal);
        }

        // No other P2-T04 production file under src/ performs an EF query: a file that uses any
        // DB-set/async-LINQ token must be one of the three contracted query holders. The
        // DesignTimeDmoDbContextFactory and the context itself reference the context type but
        // query nothing, so the scan keys on query tokens, not on the type name.
        var queryTokens = new[]
        {
            "_context.Set<", "context.Set<", "DbSet<", "Include(", "FirstOrDefaultAsync", "ToListAsync", "SingleOrDefaultAsync",
            "ExecuteDeleteAsync", "FromSql",
        };

        // Disclosed P2-T05 extension (accepted P2-T05 contract): the query-token scan below is scoped
        // to the P2-T04 surface, so the P2-T05 persistence holders are excluded exactly as
        // registered storage of their OWN tables plus the single sanctioned read-only traversal
        // (DmoPesoContextRead, §26.3 "composed from cm_contexts"; reader-confirmed on 2026-09-23).
        // The exclusion is asserted non-vacuous at the end of this test.
        var p2t05PersistenceHolders = new[]
        {
            "src/DMO.Infrastructure/Persistence/Controlo/PesoRepository.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/RepairerRepository.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/MachineRepairerAssignmentRepository.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/PdfDirectorySettingsRepository.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/EmailListRepository.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/EmailTemplateRepository.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/DmoPesoContextRead.cs",
            "src/DMO.Infrastructure/Persistence/Controlo/PesoJobOnDependencyProbe.cs",
            "src/DMO.Infrastructure/Configuration/ConfigurationCalculationConfiguration.cs",
        };

        // Disclosed P2-T07 extension (accepted P2-T07 contract): the Boquilhas persistence
        // holders are excluded exactly as registered storage of their OWN six tables plus the
        // read-only entity-set traversal of the accepted N1 reading (the History/ficha filters
        // and facts compose bq_contexts/tools/job_ons — the P2-T06 PesoReviewRepository
        // precedent, review ACCEPT 4d88dbe; review observation N1 resolved in the P2-T07
        // implementation response). The exclusion is asserted non-vacuous at the end of this test.
        var p2t07PersistenceHolders = new[]
        {
            "src/DMO.Infrastructure/Persistence/Boquilhas/BoquilhasRepository.cs",
            "src/DMO.Infrastructure/Persistence/Boquilhas/BoquilhasDependencyProbe.cs",
            "src/DMO.Infrastructure/Persistence/Boquilhas/DmoBoquilhasContextRead.cs",
        };

        var p2t05ExcludedContents = p2t05PersistenceHolders
            .Concat(p2t07PersistenceHolders)
            .Select(path => (Path: path, Source: Read(path)))
            .ToArray();

        foreach (var path in AllP2T04SourcePaths())
        {
            var source = Read(path);

            if (source == repositorySources[0] || source == repositorySources[1] || source == repositorySources[2])
            {
                continue;
            }

            if (p2t05ExcludedContents.Any(entry => entry.Source == source))
            {
                continue;
            }

            foreach (var token in queryTokens)
            {
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
            }
        }

        // The exclusion is real, not vacuous: every disclosed holder exists, and every EF
        // holder among them (of either workstream's extension) carries a query token.
        foreach (var (path, source) in p2t05ExcludedContents)
        {
            Assert.True(source.Length > 0, $"Disclosed holder '{path}' is empty.");

            if (path.EndsWith("DmoPesoContextRead.cs", StringComparison.Ordinal)
                || path.EndsWith("ConfigurationCalculationConfiguration.cs", StringComparison.Ordinal))
            {
                continue; // the two non-EF holders are disclosed for completeness but query nothing
            }

            Assert.True(
                queryTokens.Any(token => source.Contains(token, StringComparison.Ordinal)),
                $"Disclosed holder '{path}' carries no query token.");
        }
    }

    /// <summary>CTX10 (AC-33) — the context tables declare exactly the contracted columns.</summary>
    [Fact]
    public void CTX10_TheThreeContextTablesDeclareExactlyTheContractedColumns()
    {
        var cm = typeof(CmContextEntity).GetProperties().Select(property => property.Name).ToHashSet();
        var mf = typeof(MfContextEntity).GetProperties().Select(property => property.Name).ToHashSet();
        var bq = typeof(BqContextEntity).GetProperties().Select(property => property.Name).ToHashSet();

        // Exactly the contracted column set per context table.
        Assert.Equal(
            new[]
            {
                nameof(CmContextEntity.CmId), nameof(CmContextEntity.JobOnId), nameof(CmContextEntity.ToolId),
                nameof(CmContextEntity.ToolType), nameof(CmContextEntity.ToolReference),
                nameof(CmContextEntity.ToolLot), nameof(CmContextEntity.CreatedAt), nameof(CmContextEntity.UpdatedAt),
            },
            cm);
        Assert.Equal(
            new[]
            {
                nameof(MfContextEntity.MfId), nameof(MfContextEntity.JobOnId), nameof(MfContextEntity.ToolId),
                nameof(MfContextEntity.ToolType), nameof(MfContextEntity.ToolReference),
                nameof(MfContextEntity.ToolLot), nameof(MfContextEntity.CreatedAt), nameof(MfContextEntity.UpdatedAt),
            },
            mf);
        Assert.Equal(
            new[]
            {
                nameof(BqContextEntity.BqId), nameof(BqContextEntity.JobOnId), nameof(BqContextEntity.ToolId),
                nameof(BqContextEntity.ToolType), nameof(BqContextEntity.ToolReference),
                nameof(BqContextEntity.ToolLot), nameof(BqContextEntity.CreatedAt), nameof(BqContextEntity.UpdatedAt),
            },
            bq);

        // None of the excluded facts has a column on any context table.
        foreach (var excluded in new[]
                 {
                     "Quantity", "Processo", "OperationalNote", "Note", "Baffle", "Calote",
                     "Measurement", "Boquilha", "Machine", "Status", "State", "Version",
                 })
        {
            Assert.DoesNotContain(cm, member => string.Equals(member, excluded, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(mf, member => string.Equals(member, excluded, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(bq, member => string.Equals(member, excluded, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DMO.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> AllP2T04SourcePaths()
    {
        var applicationFiles = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src", "DMO.Application"),
            "*.cs",
            SearchOption.AllDirectories).Select(Relative);

        var infrastructureFiles = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src", "DMO.Infrastructure", "Persistence"),
            "*.cs",
            SearchOption.AllDirectories).Select(Relative);

        var domainFiles = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src", "DMO.Domain"),
            "*.cs",
            SearchOption.AllDirectories).Select(Relative);

        return applicationFiles
            .Concat(infrastructureFiles)
            .Concat(domainFiles)
            .Distinct()
            .Where(path => !path.Contains("DmoDbContext", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.Contains("Tool", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("JobOn", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("Context", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("Machine", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string Relative(string path) =>
        path.Replace(RepositoryRoot(), string.Empty, StringComparison.Ordinal).TrimStart('\\');
}