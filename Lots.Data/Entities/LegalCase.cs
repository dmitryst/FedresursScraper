using System.ComponentModel.DataAnnotations;

namespace Lots.Data.Entities
{
    /// <summary>
    /// Судебное дело
    /// </summary>
    public class LegalCase
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Номер дела
        /// </summary>
        [MaxLength(50)]
        public string? CaseNumber { get; set; }
    }
}
