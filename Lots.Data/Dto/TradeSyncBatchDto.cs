namespace Lots.Data.Dto;

public class BiddingScheduleUpdateDto
{
    public Guid BiddingId { get; set; }
    public DateTime NextStatusCheckAt { get; set; }
}

public class TradeSyncBatchDto
{
    public List<ImportLotTradeResultDto> Results { get; set; } = new();
    public List<BiddingScheduleUpdateDto> ScheduleUpdates { get; set; } = new();
}