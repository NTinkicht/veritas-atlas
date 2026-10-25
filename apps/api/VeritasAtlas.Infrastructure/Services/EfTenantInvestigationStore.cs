using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed immutable investigation records (VER-P0-03 slice 5,
/// #93). The database enforces the tenant key, title shape and refuses
/// UPDATE/DELETE/TRUNCATE; this service checks the same rules first so callers
/// get a typed outcome rather than a raw error. Every read is filtered to the
/// caller tenant.
/// </summary>
public sealed class EfTenantInvestigationStore : ITenantInvestigationStore
{
    private const string UniqueViolation = "23505";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<InvestigationCreateResult> CreateAsync(
        Guid tenantId, InvestigationInput input, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);

        var existing = await FindByKeyAsync(tenantId, input.CreationKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, input);
        }

        var row = new TenantInvestigation
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            CreationKey = input.CreationKey,
            Title = input.Title,
            CreatedBySubject = input.CreatedBySubject,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        _db.TenantInvestigations.Add(row);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(row).State = EntityState.Detached;

            // A concurrent writer won the creation key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.CreationKey, cancellationToken)
                ?? throw new InvalidOperationException("Creation key winner is not visible.", ex);
            return Replay(winner, input);
        }

        return new InvestigationCreateResult(InvestigationCreateOutcome.Created, ToModel(row));
    }

    public async Task<TenantInvestigationModel?> GetAsync(
        Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var row = await _db.TenantInvestigations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async Task<IReadOnlyList<TenantInvestigationModel>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var rows = await _db.TenantInvestigations.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<TenantInvestigation?> FindByKeyAsync(
        Guid tenantId, string creationKey, CancellationToken cancellationToken) =>
        _db.TenantInvestigations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CreationKey == creationKey, cancellationToken);

    private static InvestigationCreateResult Replay(TenantInvestigation existing, InvestigationInput input)
    {
        var same = existing.Title == input.Title && existing.CreatedBySubject == input.CreatedBySubject;
        return same
            ? new InvestigationCreateResult(InvestigationCreateOutcome.Replayed, ToModel(existing))
            : new InvestigationCreateResult(InvestigationCreateOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void Validate(InvestigationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.CreationKey)
            || input.CreationKey.Length > TenantInvestigationLimits.MaxCreationKeyLength)
        {
            throw new ArgumentException("Invalid creation key.", nameof(input));
        }

        if (!IsTitle(input.Title))
        {
            throw new ArgumentException("Invalid investigation title.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.CreatedBySubject)
            || input.CreatedBySubject.Length > TenantInvestigationLimits.MaxSubjectLength)
        {
            throw new ArgumentException("A creating subject is required.", nameof(input));
        }
    }

    // Stricter than ck_tenant_investigations_title_shape: refuses any Unicode
    // leading/trailing whitespace, and char.IsControl covers C0, DEL and C1.
    private static bool IsTitle(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= TenantInvestigationLimits.MaxTitleLength
        && !char.IsWhiteSpace(value[0])
        && !char.IsWhiteSpace(value[^1])
        && !value.Any(char.IsControl);

    // PostgreSQL stores microseconds: round-trip-stable values keep a created
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationModel ToModel(TenantInvestigation row) =>
        new(row.Id, row.CreationKey, row.Title, row.CreatedBySubject,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
