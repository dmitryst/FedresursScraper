namespace Lots.Data.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public bool IsSubscriptionActive { get; set; } = false;
        public DateTime? SubscriptionEndDate { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Возвращает true, если подписка куплена ИЛИ если не прошло 7 дней с момента регистрации
        /// </summary>
        public bool HasProAccess =>
            (IsSubscriptionActive && (!SubscriptionEndDate.HasValue || SubscriptionEndDate.Value > DateTime.UtcNow))
            ||
            (DateTime.UtcNow <= CreatedAt.AddDays(7));

        /// <summary>
        /// Возвращает true, если пользователь пользуется именно триалом (подписки нет, но 7 дней еще не вышли)
        /// </summary>
        public bool IsOnTrial =>
            !IsSubscriptionActive && (DateTime.UtcNow <= CreatedAt.AddDays(7));
    }
}