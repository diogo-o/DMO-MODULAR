using System.Diagnostics;
using Xunit;

namespace DMO.IntegrationTests.Controlo.Pesos;

/// <summary>
/// D1 + D2 focused regression — BEHAVIORAL proof of the page-owned adapter
/// (<c>src/DMO.Web/wwwroot/js/dmo-controlo.js</c>, the REAL shipped file) executed with a real
/// JavaScript engine (node) against a minimal document/window/fetch stub:
/// <list type="bullet">
/// <item><b>D1</b> — the submitted Peso view (editable actions absent) initializes safely — the
/// pre-fix adapter threw a <c>TypeError</c> on the unguarded calculate/save/cancel bindings and
/// killed every subsequent binding; the harness proves initialization completes, the disabled
/// submit never fires, and the still-present submitted-state controls (pending associate) keep
/// working.</item>
/// <item><b>D2</b> — a typed <c>409 stale-version</c> on every guarded mutation (save / submit /
/// machine-assignment set / email-list update) enters the accepted conflict state with the
/// explicit recovery action, issues exactly ONE request (no automatic retry, no auto-merge, no
/// overwrite of the newer server version), the recovery control reloads the authoritative state,
/// and NON-stale typed failures (validation-failed, already-submitted) keep the existing
/// presentation.</item>
/// </list>
/// Environment-gated exactly like the persistence rows: when <c>node</c> is not available the
/// test is skipped (the rendered D1 row and the whole green suites still guard the surface).
/// </summary>
/// <remarks>
/// Authority: P2-T05 focused correction D1 (submitted-view adapter crash) and D2 (stale-version
/// recovery), per the independent verification report
/// <c>reports/P2_T05_INDEPENDENT_VERIFICATION.md</c> §11 D1/D2; contract §8.4/§8.5/§8.6/§19.3 and
/// freeze §4 (<c>conflict</c> = clear message + supplied recovery choices).
/// </remarks>
public sealed class DmoControloAdapterBehaviorTests
{
    /// <summary>The behavioral harness (JS), relative to the repository root (the suite's working
    /// directory — the same relative-path convention as <see cref="P2T04ProductionScan"/>).</summary>
    private const string HarnessRelativePath =
        "tests/DMO.IntegrationTests/Controlo/Pesos/dmo-controlo-adapter.behavior.mjs";

    /// <summary>The REAL shipped adapter under test, relative to the repository root.</summary>
    private const string AdapterRelativePath = "src/DMO.Web/wwwroot/js/dmo-controlo.js";

    /// <summary>
    /// The D1 + D2 behavioral regression: the harness runs all scenarios against the shipped
    /// adapter and fails loudly (with the harness output) on any deviation.
    /// </summary>
    [SkippableFact]
    public void D1_D2_TheAdapterInitializesSafelyOnTheSubmittedViewAndRecoversFromStaleVersion()
    {
        Skip.If(
            !TryLocateNode(out var nodeExecutable),
            "node is not available on this environment; the JS behavioral harness is skipped " +
            "(the rendered D1 row and the full suites still guard the P2-T05 surface).");

        var harnessPath = Path.Combine(
            DMO.IntegrationTests.JobOn.P2T04ProductionScan.RepositoryRoot(),
            HarnessRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var adapterPath = Path.Combine(
            DMO.IntegrationTests.JobOn.P2T04ProductionScan.RepositoryRoot(),
            AdapterRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            Arguments = $"\"{harnessPath}\" \"{adapterPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)!;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            throw new Xunit.Sdk.XunitException(
                $"The adapter behavioral harness timed out.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }

        Assert.True(
            process.ExitCode == 0,
            $"The adapter behavioral harness FAILED (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");
    }

    /// <summary>Probes <c>node</c> on PATH (a real availability probe, never a silent assumption).</summary>
    private static bool TryLocateNode(out string executable)
    {
        try
        {
            var probe = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(probe)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            executable = process.WaitForExit(TimeSpan.FromSeconds(15)) && process.ExitCode == 0
                ? "node"
                : string.Empty;

            return executable.Length > 0;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            || exception is InvalidOperationException
            || exception is System.IO.IOException)
        {
            executable = string.Empty;
            return false;
        }
    }
}