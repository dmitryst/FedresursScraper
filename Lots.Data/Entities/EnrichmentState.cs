using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

[Table("EnrichmentStates")]
public class EnrichmentState
{
    [Key]
    public Guid BiddingId { get; set; }

    // Навигационное свойство (опционально, зависит от того, нужна ли связь в обе стороны)
    [ForeignKey(nameof(BiddingId))]
    public virtual Bidding Bidding { get; set; } = null!;

    public int RetryCount { get; set; } = 0;

    public DateTime? LastAttemptAt { get; set; }

    public string? LastError { get; set; }

}
