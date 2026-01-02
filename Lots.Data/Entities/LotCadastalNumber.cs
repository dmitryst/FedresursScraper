namespace Lots.Data.Entities
{
    public class LotCadastralNumber
    {
        public int Id { get; set; }
        public string CadastralNumber { get; set; } = default!;
        public Guid LotId { get; set; }
        public Lot Lot { get; set; } = default!;

        /// <summary>
        /// Кадастровый номер, очищенный от всех знаком кроме цифр (только для чтения)
        /// </summary>
        public string CleanCadastralNumber { get; private set; } = default!;
    }
}