using System.Text.Json;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ScenarioPersistenceService
{
    private readonly string _rootPath;
    private readonly string _snapshotPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ScenarioPersistenceService()
    {
        _rootPath = AppContext.BaseDirectory;
        _snapshotPath = Path.Combine(_rootPath, "scenario-snapshot.json");
    }

    public async Task SaveSnapshotAsync(ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_snapshotPath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }

    public async Task<ScenarioSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_snapshotPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer.DeserializeAsync<ScenarioSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public Task<bool> HasSnapshotAsync()
    {
        return Task.FromResult(File.Exists(_snapshotPath));
    }

    public Task ClearSnapshotAsync()
    {
        if (File.Exists(_snapshotPath))
        {
            File.Delete(_snapshotPath);
        }

        return Task.CompletedTask;
    }
}

public sealed record ScenarioSnapshot(
    Guid CaseId,
    Guid ClaimAId,
    Guid ClaimBId,
    Guid ContradictionId,
    DateTime CreatedAtUtc,
    string CaseStatus,
    string ContradictionStatus);