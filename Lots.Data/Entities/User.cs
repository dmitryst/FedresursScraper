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
        /// Возвращает true, если подписка куплена или у пользователя триальный период
        /// </summary>
        /// <remarks>
        /// Это поле нельзя использовать в LINQ запросах к БД (оно существует только в памяти)
        /// </remarks>
        public bool HasProAccess =>
            (IsSubscriptionActive && (!SubscriptionEndDate.HasValue || SubscriptionEndDate.Value > DateTime.UtcNow))
            ||
            IsOnTrial;

        /// <summary>
        /// Возвращает true, если пользователь пользуется именно триалом (подписки нет, но 7 дней еще не вышли)
        /// </summary>
        public bool IsOnTrial =>
            !IsSubscriptionActive && (DateTime.UtcNow <= CreatedAt.AddDays(7));
    }
}