using System.Text.RegularExpressions;
using Lots.Data.Entities;

namespace Lots.Data;

public static class TradeResultsScheduleHelper
{
    public static bool ShouldUseSuspendedRecheckInterval(Bidding bidding, IEnumerable<LotTradeResult> tradeResults)
    {
        var activeLots = bidding.Lots
            .Where(l => l.IsActive() && !string.IsNullOrWhiteSpace(l.LotNumber))
            .ToList();

        if (activeLots.Count == 0)
            return false;

        var resultsByLot = tradeResults
            .Where(r => r.BiddingId == bidding.Id)
            .GroupBy(r => r.LotNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EventDate).ThenByDescending(r => r.CreatedAt).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var lot in activeLots)
        {
            var lotNumber = NormalizeLotNumber(lot.LotNumber);
            if (string.IsNullOrWhiteSpace(lotNumber))
                continue;

            if (string.Equals(lot.TradeStatus, Lot.SuspendedTradeStatus, StringComparison.OrdinalIgnoreCase))
                return true;

            if (resultsByLot.TryGetValue(lotNumber, out var latestResult) &&
                string.Equals(latestResult.EventType, Lot.SuspendedTradeStatus, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFinalizingEventType(string eventType) =>
        eventType switch
        {
            Lot.SuspendedTradeStatus => false,
            "Торги не состоялись" => true,
            "Отмена торгов" => true,
            "Результаты торгов" => true,
            _ when eventType.Contains("оставления конкурсным кредитором предмета залога за собой", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };

    public static bool AllActiveLotsHaveFinalizingResults(Bidding bidding, IEnumerable<LotTradeResult> tradeResults)
    {
        var activeLots = bidding.Lots.Where(l => l.IsActive()).ToList();
        if (activeLots.Count == 0)
            return true;

        var results = tradeResults.Where(r => r.BiddingId == bidding.Id).ToList();

        foreach (var lot in activeLots)
        {
            var normalizedNumber = NormalizeLotNumber(lot.LotNumber);
            if (string.IsNullOrWhiteSpace(normalizedNumber))
                continue;

            var hasFinalizingResult = results.Any(r =>
                string.Equals(r.LotNumber, normalizedNumber, StringComparison.OrdinalIgnoreCase) &&
                IsFinalizingEventType(r.EventType));

            if (!hasFinalizingResult)
                return false;
        }

        return true;
    }

    public static string? GetLatestReasonForLot(Lot lot, IEnumerable<LotTradeResult> tradeResults)
    {
        var normalizedLotNumber = NormalizeLotNumber(lot.LotNumber);
        if (string.IsNullOrWhiteSpace(normalizedLotNumber))
            return null;

        return tradeResults
            .Where(r => r.BiddingId == lot.BiddingId &&
                        string.Equals(r.LotNumber, normalizedLotNumber, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(r.Reason))
            .OrderByDescending(r => r.EventDate)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => r.Reason)
            .FirstOrDefault();
    }

    public static string NormalizeLotNumber(string? lotNumber)
    {
        if (string.IsNullOrWhiteSpace(lotNumber)) return string.Empty;
        return Regex.Replace(lotNumber.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim();
    }
}
