using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities
{
    [Table("Favorites")]
    public class Favorite
    {
        [Key]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid LotId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
