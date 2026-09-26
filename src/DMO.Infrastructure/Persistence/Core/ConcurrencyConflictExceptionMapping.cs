using DMO.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// Maps EF persistence concurrency failures onto the domain typed conflict.
/// </summary>
/// <remarks>
/// <para>
/// A stale write makes EF's optimistic-concurrency <c>UPDATE … WHERE … AND version =
/// @expected</c> affect zero rows and <c>SaveChanges</c> throws
/// <see cref="DbUpdateConcurrencyException"/>. The repositories surface exactly the domain
/// <see cref="ConcurrencyConflictException"/> so callers can translate it to a 409 Conflict;
/// no silent overwrite is ever performed.
/// </para>
/// <para>
/// Other EF exceptions are not mapped here and propagate unchanged.
/// </para>
/// </remarks>
public static class ConcurrencyConflictExceptionMapping
{
    /// <summary>Converts the EF concurrency failure into the domain typed conflict.</summary>
    public static ConcurrencyConflictException ToDomainConflict(DbUpdateConcurrencyException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ConcurrencyConflictException(
            "A concurrent update conflict was detected: the persisted row was modified after it was read. " +
            "The write was rejected without overwriting the current state; reload and retry.",
            exception);
    }
}