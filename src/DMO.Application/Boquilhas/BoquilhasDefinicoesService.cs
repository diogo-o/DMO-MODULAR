using DMO.Application.Controlo.Settings;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.Application.Boquilhas;

/// <summary>
/// Pure static validator of <c>Boquilhas > Definições</c>: it runs before any write and returns the
/// EXACT closed tokens of the P2-T05 Definições shape rules (the ownership changed, the tokens and
/// rules did not — <see cref="ControloDefinicoesValidationErrors"/>).
/// </summary>
public static class BoquilhasDefinicoesValidator
{
    /// <summary>Validates the add-repairer command (name is the only required data).</summary>
    public static IReadOnlyList<string> Validate(CreateRepairerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return string.IsNullOrWhiteSpace(command.Name)
            ? [ControloDefinicoesValidationErrors.NameRequired]
            : [];
    }

    /// <summary>Validates the rename-repairer command.</summary>
    public static IReadOnlyList<string> Validate(RenameRepairerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (command.RepairerId == Guid.Empty)
        {
            errors.Add(ControloDefinicoesValidationErrors.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            errors.Add(ControloDefinicoesValidationErrors.NameRequired);
        }

        return errors;
    }

    /// <summary>
    /// Validates a one-machine assignment set/change/clear command: the machine must be one of the
    /// six settled codes and an EMPTY repairer id is a resolution failure (a null id is the
    /// explicit clear).
    /// </summary>
    public static IReadOnlyList<string> Validate(SetMachineAssignmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Machine) || !MachineCode.IsKnown(command.Machine.Trim()))
        {
            errors.Add(ControloDefinicoesValidationErrors.MachineUnknown);
        }

        if (command.RepairerId == Guid.Empty)
        {
            errors.Add(ControloDefinicoesValidationErrors.RepairerNotFound);
        }

        return errors;
    }
}

/// <summary>
/// The <c>Boquilhas > Definições</c> service: the repairer register and the independent
/// line/machine → repairer assignments — the repairer family owned by Boquilhas (Owner
/// clarification P2-T07 §34.3; P2-T05 §31.3).
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION §34.3. Controlo_Create no longer owns the operational
/// surface of this family; the physical <c>repairers</c>/<c>machine_repairer_assignments</c>
/// tables stay exactly where they are (the same repositories — ownership/service/UI only, no
/// schema migration; Owner rule). Shape rules unchanged as shape: name-only register, no
/// delete/deactivation, one INDEPENDENT assignment per machine (B1/B2/B3/C1/C2/C3 — changing one
/// machine never touches another), no grouping, current-state only; historical movement facts are
/// frozen and a default-repairer change never rewrites them.
/// <para>
/// Every guarded write is version-checked explicitly and through the active token (Q-CONC): a race
/// surfaces as <c>stale-version</c>, never a silent overwrite and never a generic 500. Admin gains
/// nothing — every route is gated by the canonical <c>boquilhas</c> Module policy like every other
/// Boquilhas route.</para>
/// </remarks>
public sealed class BoquilhasDefinicoesService : IBoquilhasDefinicoesService
{
    private readonly IRepairerRepository _repairers;
    private readonly IMachineRepairerAssignmentRepository _assignments;

    /// <summary>Creates the service over the shared repairer repositories (no schema move).</summary>
    public BoquilhasDefinicoesService(
        IRepairerRepository repairers,
        IMachineRepairerAssignmentRepository assignments)
    {
        ArgumentNullException.ThrowIfNull(repairers);
        ArgumentNullException.ThrowIfNull(assignments);
        _repairers = repairers;
        _assignments = assignments;
    }

    // ------------------------------------------------------------------ repairers

    /// <inheritdoc />
    public async Task<BoquilhasDefinicoesResult> ListRepairersAsync(CancellationToken cancellationToken)
    {
        var register = await _repairers.ListAsync(cancellationToken);

        return new BoquilhasDefinicoesResult.RepairersFound(
            register
                .Select(repairer => new BoquilhasRepairerItem(
                    repairer.RepairerId.Value,
                    repairer.Name,
                    repairer.Version))
                .ToList());
    }

    /// <inheritdoc />
    public async Task<BoquilhasDefinicoesResult> CreateRepairerAsync(
        CreateRepairerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = BoquilhasDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new BoquilhasDefinicoesResult.ValidationFailed(errors);
        }

        var now = DateTimeOffset.UtcNow;
        var repairer = new Repairer(RepairerId.New(), command.Name.Trim(), Version: 1, now, now);

        var created = await _repairers.CreatedAsync(repairer, cancellationToken);

        return new BoquilhasDefinicoesResult.RepairerCreated(
            created.RepairerId.Value,
            created.Version);
    }

    /// <inheritdoc />
    public async Task<BoquilhasDefinicoesResult> RenameRepairerAsync(
        RenameRepairerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = BoquilhasDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new BoquilhasDefinicoesResult.ValidationFailed(errors);
        }

        var persisted = await _repairers.GetByIdAsync(command.RepairerId, cancellationToken);
        if (persisted is null)
        {
            return new BoquilhasDefinicoesResult.NotFound(command.RepairerId);
        }

        var stale = AssertVersion(persisted.Version, command.ExpectedVersion, "reparador");
        if (stale is not null)
        {
            return stale;
        }

        var renamed = persisted with { Name = command.Name.Trim() };

        try
        {
            var saved = await _repairers.RenamedAsync(renamed, cancellationToken);

            // The SAME repairer_id is retained across the rename.
            return new BoquilhasDefinicoesResult.RepairerRenamed(
                saved.RepairerId.Value,
                saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(exception.Message);
        }
    }

    // ------------------------------------------------------------------ machine assignments

    /// <inheritdoc />
    public async Task<BoquilhasDefinicoesResult> ListMachineAssignmentsAsync(CancellationToken cancellationToken)
    {
        var assignments = await _assignments.ListAsync(cancellationToken);

        return new BoquilhasDefinicoesResult.AssignmentsFound(
            MachineCode.All
                .Select(machine =>
                {
                    var assignment = assignments.FirstOrDefault(candidate =>
                        candidate.Machine.Value == machine.Value);

                    return new BoquilhasMachineAssignmentItem(
                        machine.Value,
                        assignment?.RepairerId.Value,
                        assignment?.Version ?? 1);
                })
                .ToList());
    }

    /// <inheritdoc />
    public async Task<BoquilhasDefinicoesResult> SetMachineAssignmentAsync(
        SetMachineAssignmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = BoquilhasDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new BoquilhasDefinicoesResult.ValidationFailed(errors);
        }

        var machine = command.Machine.Trim();

        // A null repairer id is the explicit clear operation (row removal).
        if (command.RepairerId is not { } repairerId)
        {
            try
            {
                await _assignments.ClearedAsync(machine, command.ExpectedVersion ?? 1, cancellationToken);

                return new BoquilhasDefinicoesResult.AssignmentCleared(machine);
            }
            catch (ConcurrencyConflictException exception)
            {
                return Refuse(exception.Message);
            }
        }

        var repairer = await _repairers.GetByIdAsync(repairerId, cancellationToken);
        if (repairer is null)
        {
            return new BoquilhasDefinicoesResult.ValidationFailed(
                [ControloDefinicoesValidationErrors.RepairerNotFound]);
        }

        var now = DateTimeOffset.UtcNow;
        var assignment = new MachineRepairerAssignment(
            Guid.NewGuid(),
            MachineCode.From(machine),
            RepairerId.From(repairerId),
            command.ExpectedVersion ?? 1,
            now,
            now);

        try
        {
            var saved = await _assignments.SetAsync(assignment, cancellationToken);

            return new BoquilhasDefinicoesResult.AssignmentSet(
                saved.Machine.Value,
                saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(exception.Message);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static BoquilhasDefinicoesResult Refuse(string message) =>
        new BoquilhasDefinicoesResult.Refused(BoquilhasDefinicoesRefusalReason.StaleVersion, message);

    private static BoquilhasDefinicoesResult? AssertVersion(int current, int expected, string what)
    {
        if (current != expected)
        {
            return Refuse(
                $"O {what} foi alterado depois de observado (versão esperada {expected}, " +
                $"atual {current}); nada foi escrito.");
        }

        return null;
    }
}