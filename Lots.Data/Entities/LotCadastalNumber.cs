namespace Lots.Data.Entities
{
    public class LotCadastralNumber
    {
        public int Id { get; set; }
        public string CadastralNumber { get; set; } = default!;
        public Guid LotId { get; set; }
        public Lot Lot { get; set; } = default!;
    }
}