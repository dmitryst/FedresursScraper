using System;
using System.ComponentModel.DataAnnotations;

namespace Lots.Data.Entities
{
    /// <summary>
    /// Сырое сообщение из API Федресурса. 
    /// Выступает в роли Staging-слоя перед обработкой бизнес-логикой.
    /// </summary>
    public class RawFedresursMessage
    {
        /// <summary>
        /// GUID сообщения из API.
        /// </summary>
        [Key]
        public Guid Guid { get; set; }

        /// <summary>
        /// Номер сообщения.
        /// </summary>
        [MaxLength(20)]
        public string Number { get; set; } = null!; 

        /// <summary>
        /// Тип сообщения (например: Auction, TradeResult, CancelAuction).
        /// </summary>
        [MaxLength(50)]
        public string Type { get; set; } = null!; 

        /// <summary>
        /// Дата публикации сообщения.
        /// </summary>
        public DateTime DatePublish { get; set; }

        /// <summary>
        /// Сырой XML-контент. Может быть null, если сообщение заблокировано.
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Флаг заблокированного сообщения (lockReason != null в ответе API).
        /// </summary>
        public bool IsLocked { get; set; }

        // обработка сообщений

        /// <summary>
        /// Флаг успешной обработки (данные переложены в основную БД в виде Торгов и Лотов).
        /// </summary>
        public bool IsProcessed { get; set; } = false;

        /// <summary>
        /// Текст ошибки, если парсинг XML или сохранение бизнес-сущностей упали.
        /// </summary>
        public string? ProcessingError { get; set; }

        /// <summary>
        /// Количество попыток обработки сообщения.
        /// </summary>
        public int ProcessingAttempts { get; set; } = 0;

        /// <summary>
        /// Дата и время скачивания сообщения в нашу базу.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}