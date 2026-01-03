using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities
{
    [Table("LotAuditEvents")]
    public class LotAuditEvent
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid LotId { get; set; }

        /// <summary>
        /// Тип события (например, "Classification")
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string EventType { get; set; } = default!;

        /// <summary>
        /// Время фиксации события
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Статус события ("Start", "Success", "Failure")
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = default!;

        /// <summary>
        /// Сервис-источник запуска события (например, "Scraper", "Recovery")
        /// </summary>
        [MaxLength(50)]
        public string Source { get; set; } = default!;

        /// <summary>
        /// Для записи ошибок или доп. инфо
        /// </summary>
        public string? Details { get; set; }
    }
}
