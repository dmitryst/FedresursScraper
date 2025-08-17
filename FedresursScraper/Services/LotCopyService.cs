using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Lots.Data;
using Lots.Data.Entities;

namespace FedresursScraper.Services;

public interface ILotCopyService
{
    Task<bool> CopyLotToProdAsync(Guid lotId, CancellationToken ct = default);
}

public class LotCopyService : ILotCopyService
{
    private readonly LotsDbContext _devContext;          // Scoped из DI
    private readonly IConfiguration _config;
    private readonly ILogger<LotCopyService> _logger;

    public LotCopyService(
        LotsDbContext devContext,
        IConfiguration config,
        ILogger<LotCopyService> logger)
    {
        _devContext = devContext;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> CopyLotToProdAsync(Guid lotId, CancellationToken ct = default)
    {
        var prodConnectionString = _config.GetConnectionString("PostgresProd");

        if (string.IsNullOrWhiteSpace(prodConnectionString))
        {
            _logger.LogError("Не заданы параметры подключения к PROD БД.");
            return false;
        };

        // Собираем Options для PROD вручную
        var prodOptions = new DbContextOptionsBuilder<LotsDbContext>()
            .UseNpgsql(prodConnectionString)
            .Options;

        // Контекст PROD создаём вручную и живём в пределах метода.
        await using var prodContext = new LotsDbContext(prodOptions);

        await using var tx = await prodContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Читаем из DEV
            var sourceLot = await _devContext.Lots
                .Include(l => l.Bidding)
                .Include(l => l.Categories)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == lotId, ct);

            if (sourceLot == null)
            {
                _logger.LogWarning("Лот {LotId} не найден в DEV БД.", lotId);
                return false;
            }

            // Копируем Bidding в PROD (идемпотентно)
            var prodBidding = await prodContext.Biddings
                .FirstOrDefaultAsync(b => b.Id == sourceLot.Bidding.Id, ct);

            if (prodBidding == null)
            {
                prodBidding = new Bidding
                {
                    Id = sourceLot.Bidding.Id,
                    AnnouncedAt = sourceLot.Bidding.AnnouncedAt,
                    Type = sourceLot.Bidding.Type,
                    BidAcceptancePeriod = sourceLot.Bidding.BidAcceptancePeriod,
                    BankruptMessageId = sourceLot.Bidding.BankruptMessageId,
                    ViewingProcedure = sourceLot.Bidding.ViewingProcedure,
                    CreatedAt = sourceLot.Bidding.CreatedAt
                };
                prodContext.Biddings.Add(prodBidding);
                await prodContext.SaveChangesAsync(ct);
            }

            // Копируем Lot в PROD (идемпотентно)
            var prodLot = await prodContext.Lots
                .FirstOrDefaultAsync(l => l.Id == sourceLot.Id, ct);

            if (prodLot == null)
            {
                prodLot = new Lot
                {
                    Id = sourceLot.Id,
                    LotNumber = sourceLot.LotNumber,
                    StartPrice = sourceLot.StartPrice,
                    Step = sourceLot.Step,
                    Deposit = sourceLot.Deposit,
                    Description = sourceLot.Description,
                    CreatedAt = sourceLot.CreatedAt,
                    BiddingId = prodBidding.Id
                };
                prodContext.Lots.Add(prodLot);
                await prodContext.SaveChangesAsync(ct);
            }

            // Копируем категории в PROD (идемпотентно)
            if (sourceLot.Categories != null && sourceLot.Categories.Count > 0)
            {
                foreach (var cat in sourceLot.Categories)
                {
                    var exists = await prodContext.LotCategories
                        .AnyAsync(c => c.LotId == prodLot.Id && c.Name == cat.Name, ct);
                    if (!exists)
                    {
                        prodContext.LotCategories.Add(new LotCategory
                        {
                            Name = cat.Name,
                            LotId = prodLot.Id
                        });
                    }
                }
                await prodContext.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("Лот {LotId} успешно скопирован в PROD.", lotId);
            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Ошибка копирования лота {LotId} в PROD.", lotId);
            return false;
        }
    }
}