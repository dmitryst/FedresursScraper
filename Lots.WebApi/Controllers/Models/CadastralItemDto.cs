namespace FedresursScraper.Controllers.Models
{
    public class CadastralItemDto
    {
        public string CadastralNumber { get; set; } = default!;
        public double? Area { get; set; }
        public decimal? CadastralCost { get; set; }
        public string? Category { get; set; }
        public string? PermittedUse { get; set; }
        public string? Address { get; set; }
        public string? Status { get; set; }
        public string? ObjectType { get; set; }
        public string? RightType { get; set; }
        public string? OwnershipType { get; set; }
        public string? RegDate { get; set; }
    }
}