using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities
{
    [Table("LotVotes")]
    public class LotVote
    {
        [Key]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid LotId { get; set; }
        public Lot Lot { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
