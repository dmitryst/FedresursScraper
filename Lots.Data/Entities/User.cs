namespace Lots.Data.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public bool IsSubscriptionActive { get; set; } = false;
        public DateTime? SubscriptionEndDate { get; set; }
    }
}