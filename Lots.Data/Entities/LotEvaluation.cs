using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities
{
    public class LotEvaluation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid LotId { get; set; }

        [ForeignKey("LotId")]
        public Lot Lot { get; set; } = default!;

        /// <summary>
        /// Оценочная стоимость лота
        /// </summary>
        public decimal EstimatedPrice { get; set; }

        /// <summary>
        /// Ликвидность лота по шкале от 1 до 10
        /// </summary>
        public int LiquidityScore { get; set; }

        /// <summary>
        /// Текстовое описание инвестиционной привлекательности
        /// </summary>
        public string InvestmentSummary { get; set; } = string.Empty;

        /// <summary>
        /// Пошаговое рассуждение модели (Step-by-step reasoning)
        /// </summary>
        public string ReasoningText { get; set; } = string.Empty;

        /// <summary>
        /// Количество токенов потраченное на промт
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>
        /// Количество токенов потраченное на "thinking" (рассуждение)
        /// </summary>
        public int ReasoningTokens { get; set; }

        /// <summary>
        /// Количество токенов потраченное на ответ (включая reasoning, если модель так считает, или только completion)
        /// </summary>
        public int CompletionTokens { get; set; }

        /// <summary>
        /// Общее количество токенов
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Модель, которая произвела оценку (например, "deepseek-reasoner")
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
