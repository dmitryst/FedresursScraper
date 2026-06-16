using Lots.Application.Services.DebtScoring;
using Lots.Data.Entities;
using Lots.Data.Entities.DebtScoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Services.DebtScoring;

public interface IDebtLotDiscoveryService
{
    Task<bool> DiscoverPendingLotsAsync(CancellationToken cancellationToken);
}

public class DebtLotDiscoveryService : IDebtLotDiscoveryService
{
    private readonly LotsDbContext _context;
    private readonly ICourtActEntityExtractor _entityExtractor;
    private readonly IOptionsMonitor<DebtScoringOptions> _options;
    private readonly ILogger<DebtLotDiscoveryService> _logger;

    public DebtLotDiscoveryService(
        LotsDbContext context,
        ICourtActEntityExtractor entityExtractor,
        IOptionsMonitor<DebtScoringOptions> options,
        ILogger<DebtLotDiscoveryService> logger)
    {
        _context = context;
        _entityExtractor = entityExtractor;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> DiscoverPendingLotsAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var existingProfileLotIds = _context.DebtLotProfiles.Select(p => p.LotId);

        var lots = await _context.Lots
            .Include(l => l.Categories)
            .Where(l => l.Categories.Any(c => c.Name == DebtScoringConstants.DebtCategoryName))
            .Where(l => !existingProfileLotIds.Contains(l.Id))
            .OrderByDescending(l => l.CreatedAt)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        if (lots.Count == 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;

        foreach (var lot in lots)
        {
            var nominal = ResolveDebtNominal(lot);
            var profile = new DebtLotProfile
            {
                LotId = lot.Id,
                DebtNominal = nominal,
                CreatedAt = now,
                UpdatedAt = now,
            };

            if (nominal.HasValue && nominal.Value < options.MinDebtNominal)
            {
                profile.Status = DebtLotProcessingStatus.Rejected;
                profile.RejectionReason =
                    $"Номинал {nominal.Value:N0} руб. ниже порога {options.MinDebtNominal:N0} руб.";
            }
            else
            {
                profile.Status = DebtLotProcessingStatus.PendingDocuments;
            }

            _context.DebtLotProfiles.Add(profile);
            _logger.LogInformation(
                "Debt lot profile created for lot {LotId} (public {PublicId}), status={Status}",
                lot.Id,
                lot.PublicId,
                profile.Status);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private decimal? ResolveDebtNominal(Lot lot)
    {
        if (lot.StartPrice is > 0)
        {
            return lot.StartPrice;
        }

        var text = !string.IsNullOrWhiteSpace(lot.Description) && lot.Description != "не найдено"
            ? lot.Description
            : lot.Title;

        if (string.IsNullOrWhiteSpace(text))
        {
            return lot.StartPrice;
        }

        return _entityExtractor.Extract(text).DebtNominal ?? lot.StartPrice;
    }
}
