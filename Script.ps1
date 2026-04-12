param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = 'Stop'

function Write-FileUtf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
    Write-Host "Wrote: $Path"
}

function Join-RootPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    return [System.IO.Path]::Combine($RootDir, $RelativePath)
}

$files = @{
    "apps/api/VeritasAtlas.Application/Intelligence/Interfaces/IAIOrchestratorService.cs" = @'
using VeritasAtlas.Application.Intelligence.Models;

namespace VeritasAtlas.Application.Intelligence.Interfaces;

public interface IAIOrchestratorService
{
    Task<DocumentProcessingResult> ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
'@

    "apps/api/VeritasAtlas.Application/Intelligence/Interfaces/IStatementExtractionService.cs" = @'
using VeritasAtlas.Application.Intelligence.Models;

namespace VeritasAtlas.Application.Intelligence.Interfaces;

public interface IStatementExtractionService
{
    Task<IReadOnlyList<ExtractedStatementResult>> ExtractAsync(
        string documentContent,
        CancellationToken cancellationToken = default);
}
'@

    "apps/api/VeritasAtlas.Application/Intelligence/Interfaces/IClaimBuilderService.cs" = @'
using VeritasAtlas.Application.Intelligence.Models;

namespace VeritasAtlas.Application.Intelligence.Interfaces;

public interface IClaimBuilderService
{
    Task<IReadOnlyList<BuiltClaimResult>> BuildAsync(
        IReadOnlyList<ExtractedStatementResult> statements,
        CancellationToken cancellationToken = default);
}
'@

    "apps/api/VeritasAtlas.Application/Intelligence/Models/ExtractedStatementResult.cs" = @'
namespace VeritasAtlas.Application.Intelligence.Models;

public sealed class ExtractedStatementResult
{
    public int OrderIndex { get; init; }
    public string RawText { get; init; } = string.Empty;
    public string NormalizedText { get; init; } = string.Empty;
}
'@

    "apps/api/VeritasAtlas.Application/Intelligence/Models/BuiltClaimResult.cs" = @'
namespace VeritasAtlas.Application.Intelligence.Models;

public sealed class BuiltClaimResult
{
    public int OrderIndex { get; init; }
    public int SourceStatementOrderIndex { get; init; }
    public string Topic { get; init; } = string.Empty;
    public string NormalizedText { get; init; } = string.Empty;
}
'@

    "apps/api/VeritasAtlas.Application/Intelligence/Models/DocumentProcessingResult.cs" = @'
namespace VeritasAtlas.Application.Intelligence.Models;

public sealed class DocumentProcessingResult
{
    public Guid DocumentId { get; init; }
    public int StatementsCreated { get; init; }
    public int ClaimsCreated { get; init; }
    public IReadOnlyList<ExtractedStatementResult> Statements { get; init; } = Array.Empty<ExtractedStatementResult>();
    public IReadOnlyList<BuiltClaimResult> Claims { get; init; } = Array.Empty<BuiltClaimResult>();
}
'@

    "apps/api/VeritasAtlas.Infrastructure/Intelligence/StatementExtractionService.cs" = @'
using System.Text.RegularExpressions;
using VeritasAtlas.Application.Intelligence.Interfaces;
using VeritasAtlas.Application.Intelligence.Models;

namespace VeritasAtlas.Infrastructure.Intelligence;

public sealed class StatementExtractionService : IStatementExtractionService
{
    private const int MinFragmentLength = 15;

    private static readonly Regex SplitRegex = new(
        @"(?<=[\.\!\?\;\:])|[\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MultiWhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<IReadOnlyList<ExtractedStatementResult>> ExtractAsync(
        string documentContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            return Task.FromResult<IReadOnlyList<ExtractedStatementResult>>(Array.Empty<ExtractedStatementResult>());
        }

        var fragments = SplitRegex.Split(documentContent);
        var results = new List<ExtractedStatementResult>(fragments.Length);
        var orderIndex = 1;

        foreach (var fragment in fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalized = NormalizeWhitespace(fragment);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (normalized.Length < MinFragmentLength)
            {
                continue;
            }

            results.Add(new ExtractedStatementResult
            {
                OrderIndex = orderIndex,
                RawText = normalized,
                NormalizedText = normalized
            });

            orderIndex++;
        }

        return Task.FromResult<IReadOnlyList<ExtractedStatementResult>>(results);
    }

    private static string NormalizeWhitespace(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(trimmed, " ");
    }
}
'@

    "apps/api/VeritasAtlas.Infrastructure/Intelligence/ClaimBuilderService.cs" = @'
using System.Text.RegularExpressions;
using VeritasAtlas.Application.Intelligence.Interfaces;
using VeritasAtlas.Application.Intelligence.Models;

namespace VeritasAtlas.Infrastructure.Intelligence;

public sealed class ClaimBuilderService : IClaimBuilderService
{
    private static readonly Regex NonWordRegex = new(
        @"[^\p{L}\p{N}\s]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from",
        "has", "have", "he", "in", "is", "it", "its", "of", "on", "or",
        "she", "that", "the", "their", "they", "this", "to", "was",
        "were", "will", "with"
    };

    public Task<IReadOnlyList<BuiltClaimResult>> BuildAsync(
        IReadOnlyList<ExtractedStatementResult> statements,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BuiltClaimResult>(statements.Count);

        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            results.Add(new BuiltClaimResult
            {
                OrderIndex = statement.OrderIndex,
                SourceStatementOrderIndex = statement.OrderIndex,
                Topic = DeriveTopic(statement.NormalizedText),
                NormalizedText = NormalizeClaimText(statement.NormalizedText)
            });
        }

        return Task.FromResult<IReadOnlyList<BuiltClaimResult>>(results);
    }

    private static string DeriveTopic(string text)
    {
        var cleaned = NonWordRegex.Replace(text, " ");
        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 2)
            .Where(x => !StopWords.Contains(x))
            .Take(4)
            .ToArray();

        if (words.Length == 0)
        {
            return "General Statement";
        }

        return string.Join(' ', words);
    }

    private static string NormalizeClaimText(string text)
    {
        return text.Trim();
    }
}
'@

    "apps/api/VeritasAtlas.Infrastructure/Intelligence/AIOrchestratorService.cs" = @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Intelligence.Interfaces;
using VeritasAtlas.Application.Intelligence.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Intelligence;

public sealed class AIOrchestratorService : IAIOrchestratorService
{
    private const string SystemActor = "ai:manual-document-processing";

    private readonly VeritasAtlasDbContext _dbContext;
    private readonly IStatementExtractionService _statementExtractionService;
    private readonly IClaimBuilderService _claimBuilderService;

    public AIOrchestratorService(
        VeritasAtlasDbContext dbContext,
        IStatementExtractionService statementExtractionService,
        IClaimBuilderService claimBuilderService)
    {
        _dbContext = dbContext;
        _statementExtractionService = statementExtractionService;
        _claimBuilderService = claimBuilderService;
    }

    public async Task<DocumentProcessingResult> ProcessDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(document.Content))
        {
            throw new InvalidOperationException($"Document '{documentId}' has no content.");
        }

        var extractedStatements = await _statementExtractionService.ExtractAsync(document.Content, cancellationToken);

        if (extractedStatements.Count == 0)
        {
            return new DocumentProcessingResult
            {
                DocumentId = documentId,
                StatementsCreated = 0,
                ClaimsCreated = 0,
                Statements = Array.Empty<ExtractedStatementResult>(),
                Claims = Array.Empty<BuiltClaimResult>()
            };
        }

        var statementEntities = extractedStatements
            .Select(x => CreateStatementEntity(documentId, x))
            .ToList();

        await _dbContext.Statements.AddRangeAsync(statementEntities, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var builtClaims = await _claimBuilderService.BuildAsync(extractedStatements, cancellationToken);

        var statementMap = statementEntities.ToDictionary(x => x.OrderIndex, x => x);

        var claimEntities = builtClaims
            .Select(x => CreateClaimEntity(documentId, statementMap[x.SourceStatementOrderIndex].Id, x))
            .ToList();

        await _dbContext.Claims.AddRangeAsync(claimEntities, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DocumentProcessingResult
        {
            DocumentId = documentId,
            StatementsCreated = statementEntities.Count,
            ClaimsCreated = claimEntities.Count,
            Statements = extractedStatements,
            Claims = builtClaims
        };
    }

    private static Statement CreateStatementEntity(Guid documentId, ExtractedStatementResult extracted)
    {
        return new Statement
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Text = extracted.RawText,
            NormalizedText = extracted.NormalizedText,
            OrderIndex = extracted.OrderIndex,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = SystemActor
        };
    }

    private static Claim CreateClaimEntity(Guid documentId, Guid statementId, BuiltClaimResult builtClaim)
    {
        return new Claim
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            StatementId = statementId,
            Topic = builtClaim.Topic,
            NormalizedText = builtClaim.NormalizedText,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = SystemActor
        };
    }
}
'@

    "apps/api/VeritasAtlas.Api/Contracts/AIContracts.cs" = @'
namespace VeritasAtlas.Api.Contracts;

public sealed class ProcessDocumentRequest
{
    public Guid DocumentId { get; init; }
}

public sealed class ProcessDocumentResponse
{
    public Guid DocumentId { get; init; }
    public int StatementsCreated { get; init; }
    public int ClaimsCreated { get; init; }
}
'@

    "apps/api/VeritasAtlas.Api/Controllers/AIController.cs" = @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts;
using VeritasAtlas.Application.Intelligence.Interfaces;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/ai")]
public sealed class AIController : ControllerBase
{
    private readonly IAIOrchestratorService _aiOrchestratorService;

    public AIController(IAIOrchestratorService aiOrchestratorService)
    {
        _aiOrchestratorService = aiOrchestratorService;
    }

    [HttpPost("process-document")]
    [ProducesResponseType(typeof(ProcessDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProcessDocumentResponse>> ProcessDocument(
        [FromBody] ProcessDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DocumentId == Guid.Empty)
        {
            return BadRequest("DocumentId is required.");
        }

        try
        {
            var result = await _aiOrchestratorService.ProcessDocumentAsync(request.DocumentId, cancellationToken);

            return Ok(new ProcessDocumentResponse
            {
                DocumentId = result.DocumentId,
                StatementsCreated = result.StatementsCreated,
                ClaimsCreated = result.ClaimsCreated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
'@

    "apps/api/VeritasAtlas.Infrastructure/DependencyInjection/IntelligenceServiceCollectionExtensions.cs" = @'
using Microsoft.Extensions.DependencyInjection;
using VeritasAtlas.Application.Intelligence.Interfaces;
using VeritasAtlas.Infrastructure.Intelligence;

namespace VeritasAtlas.Infrastructure.DependencyInjection;

public static class IntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddIntelligenceServices(this IServiceCollection services)
    {
        services.AddScoped<IStatementExtractionService, StatementExtractionService>();
        services.AddScoped<IClaimBuilderService, ClaimBuilderService>();
        services.AddScoped<IAIOrchestratorService, AIOrchestratorService>();

        return services;
    }
}
'@

    "apps/api/VeritasAtlas.Api/Patches/Program.AddIntelligenceServices.snippet.cs" = @'
using VeritasAtlas.Infrastructure.DependencyInjection;

builder.Services.AddIntelligenceServices();
'@
}

Write-Host "Applying THREAD 30A - Intelligence Layer Foundation..."

foreach ($relativePath in $files.Keys) {
    $fullPath = Join-RootPath $relativePath
    Write-FileUtf8NoBom -Path $fullPath -Content $files[$relativePath]
}

Write-Host "Done."
