using DMO.Application.Accounts;
using DMO.Application.Session;

namespace DMO.UnitTests.Controlo.Approve.Fakes;

/// <summary>
/// Fixed current-account fake for the P2-T06 unit tests: the backend actor of every decision.
/// </summary>
internal sealed class FixedCurrentAccount(Guid userId) : ICurrentAccountContext
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static FixedCurrentAccount User() => new(UserId);

    public static FixedCurrentAccount None() => new(Guid.Empty);

    public Task<CurrentAccount> GetCurrentAsync(CancellationToken cancellationToken) =>
        Task.FromResult<CurrentAccount>(
            userId == Guid.Empty
                ? new CurrentAccount.None()
                : new CurrentAccount.User(new UserAccount(userId, "1042", "Test User", "test@example.pt", "Turno A", IsActive: true, TemplateId: Guid.NewGuid(), Version: 1)));
}