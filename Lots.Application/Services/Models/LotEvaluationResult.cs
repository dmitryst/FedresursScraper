namespace FedresursScraper.Services.Models
{
    public class LotEvaluationResult
    {
        public decimal EstimatedPrice { get; set; }
        public int LiquidityScore { get; set; }
        public string InvestmentSummary { get; set; } = string.Empty;
        public string ReasoningText { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int ReasoningTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }
}
