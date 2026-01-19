using System;

namespace Lots.Data.Entities
{
    /// <summary>
    /// Документ лота (отчеты, планы, договоры)
    /// </summary>
    public class LotDocument
    {
        public Guid Id { get; set; }

        public Guid LotId { get; set; }
        public Lot Lot { get; set; } = default!;

        /// <summary>
        /// Ссылка на файл в S3
        /// </summary>
        public string Url { get; set; } = default!;

        /// <summary>
        /// Название документа (например: "Договор купли-продажи", "План помещения")
        /// </summary>
        public string Title { get; set; } = default!;

        /// <summary>
        /// Расширение файла (например: .pdf, .docx)
        /// </summary>
        public string? Extension { get; set; }

        /// <summary>
        /// Дата создания записи
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
