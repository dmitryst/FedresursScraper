using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities
{
    /// <summary>
    /// Бизнес-результат работы DeepSeek. 
    /// Его цель — аналитика качества данных (классификации лота) и улучшение дерева категорий
    /// </summary>
    [Table("LotClassificationAnalysis")]
    public class LotClassificationAnalysis
    {
        /// <summary>
        /// Идентификатор записи
        /// </summary>
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Идентификатор лота
        /// </summary>
        public Guid LotId { get; set; }

        /// <summary>
        /// Новая прдлашаемая нейросетью категория для лота
        /// </summary>
        [MaxLength(200)]
        public string? SuggestedCategory { get; set; }

        /// <summary>
        /// Выбранные нейросетью категории из предложенных в промте
        /// </summary>
        [MaxLength(500)]
        public string? SelectedCategories { get; set; }

        /// <summary>
        /// Полный JSON ответа
        /// </summary>
        public string RawResponseJson { get; set; } = default!;

        /// <summary>
        /// Время создания записи
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Версия модели (или промпта)
        /// </summary>
        [MaxLength(50)]
        public string ModelVersion { get; set; } = "deepseek-v1";
    }
}
