using System.Diagnostics;
using Xunit;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// K5 (AC-K4 / D2 preservation) — BEHAVIORAL proof of the page-owned adapter
/// (<c>src/DMO.Web/wwwroot/js/dmo-controlo-approve.js</c>, the REAL shipped file) executed with a
/// real JavaScript engine (node) against a minimal document/window/fetch stub.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §11.3/§26.4 row K5 and the accepted D2 pattern (conflict
/// presentation + explicit "Recarregar estado atual" recovery; no automatic retry, no auto-merge,
/// no silent overwrite; the observed version refreshes only on success). Environment-gated exactly
/// like the accepted D1/D2 harness: when <c>node</c> is not available the test is skipped (the
/// rendered K5 row and the full suites still guard the surface).
/// </remarks>
/// <seealso cref="DmoControloAdapterBehaviorTests"/>
public sealed class DmoControloApproveAdapterBehaviorTests
{
    /// <summary>The behavioral harness (JS), relative to the repository root.</summary>
    private const string HarnessRelativePath =
        "tests/DMO.IntegrationTests/Controlo/Approve/dmo-controlo-approve-adapter.behavior.mjs";

    /// <summary>The REAL shipped adapter under test, relative to the repository root.</summary>
    private const string AdapterRelativePath = "src/DMO.Web/wwwroot/js/dmo-controlo-approve.js";

    /// <summary>
    /// K5 — a typed <c>stale-version</c> on approve/reject/reopen enters the accepted conflict
    /// state with the explicit reload recovery, issues exactly ONE request (no automatic retry, no
    /// auto-merge, no overwrite of the newer server version), the recovery control reloads the
    /// authoritative state, non-stale typed failures keep the errors presentation, the reason
    /// input gates reject/reopen, and open arbitration resolves through the page-owned route map.
    /// </summary>
    [SkippableFact]
    public void K5_TheAdapterRecoversFromStaleVersionWithExplicitReloadAndNeverAutoRetries()
    {
        Skip.If(
            !TryLocateNode(out var nodeExecutable),
            "node is not available on this environment; the JS behavioral harness is skipped " +
            "(the rendered K5 row and the full suites still guard the P2-T06 surface).");

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