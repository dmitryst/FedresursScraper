using System.Threading.Tasks;

public interface ILotClassifier
{
    Task<string> ClassifyLotAsync(string lotTitle);
}
