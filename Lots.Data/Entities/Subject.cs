using System.ComponentModel.DataAnnotations;

namespace Lots.Data.Entities
{
    /// <summary>
    /// Тип субъекта
    /// </summary>
    public enum SubjectType
    {
        /// <summary>
        /// Физическое лицо
        /// </summary>
        Individual = 0,

        /// <summary>
        /// Юридическое лицо
        /// </summary>
        Company = 1
    }

    /// <summary>
    /// Субъект (физическое или юр. лицо)
    /// </summary>
    public class Subject
    {
        /// <summary>
        /// Уникальный идентификатор
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// ФИО / Наименование субъекта
        /// </summary>
        [MaxLength(500)]
        public string Name { get; set; } = default!;

        /// <summary>
        /// ИНН
        /// </summary>
        [MaxLength(20)]
        public string? Inn { get; set; }

        /// <summary>
        /// СНИЛС (для физ. лиц)
        /// </summary>
        [MaxLength(20)]
        public string? Snils { get; set; }

        /// <summary>
        /// ОГРН (для юр. лиц)
        /// </summary>
        [MaxLength(20)]
        public string? Ogrn { get; set; }

        /// <summary>
        /// Тип: физическое или юридическое лицо
        /// </summary>
        public SubjectType Type { get; set; }
    }
}
