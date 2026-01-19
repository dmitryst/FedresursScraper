using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace FedresursScraper.Services;

public interface IFileStorageService
{
    // Возвращает публичную ссылку на сохраненный файл
    Task<string> UploadAsync(byte[] fileData, string fileName, string contentType = "image/jpeg");
    Task DeleteAsync(string fileName);
}


public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _publicUrlBase;

    public S3FileStorageService(IConfiguration config)
    {
        var accessKey = config["S3:AccessKey"] ?? throw new ArgumentNullException("S3:AccessKey");
        var secretKey = config["S3:SecretKey"] ?? throw new ArgumentNullException("S3:SecretKey");
        var serviceUrl = config["S3:ServiceUrl"] ?? throw new ArgumentNullException("S3:ServiceUrl");
        _bucketName = config["S3:BucketName"] ?? throw new ArgumentNullException("S3:BucketName");
        _publicUrlBase = config["S3:PublicUrlBase"] ?? throw new ArgumentNullException("S3:PublicUrlBase");

        var s3Config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            UseHttp = true
        };

        _s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);

        // авто-создания бакета
        EnsureBucketExistsAsync().Wait();
    }

    public async Task<string> UploadAsync(byte[] fileData, string fileName, string contentType = "image/jpeg")
    {
        using var stream = new MemoryStream(fileData);

        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            Key = fileName,
            BucketName = _bucketName,
            CannedACL = S3CannedACL.PublicRead,
            ContentType = contentType
        };

        var fileTransferUtility = new TransferUtility(_s3Client);
        await fileTransferUtility.UploadAsync(uploadRequest);

        // Формируем публичную ссылку для базы
        return $"{_publicUrlBase}/{_bucketName}/{fileName}";
    }

    public async Task DeleteAsync(string fileName)
    {
        var deleteObjectRequest = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = fileName
        };

        await _s3Client.DeleteObjectAsync(deleteObjectRequest);
    }

    private async Task EnsureBucketExistsAsync()
    {
        try
        {
            var bucketExists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName);
            if (!bucketExists)
            {
                var putBucketRequest = new PutBucketRequest
                {
                    BucketName = _bucketName,
                    UseClientRegion = true
                };
                await _s3Client.PutBucketAsync(putBucketRequest);

                // Опционально: Настройка политики для публичного чтения (аналог действий в UI)
                string policyJson = $@"{{
                ""Version"": ""2012-10-17"",
                ""Statement"": [{{
                    ""Action"": [""s3:GetObject""],
                    ""Effect"": ""Allow"",
                    ""Principal"": {{""AWS"": [""*""]}},
                    ""Resource"": [""arn:aws:s3:::{_bucketName}/*""]
                }}]
            }}";

                await _s3Client.PutBucketPolicyAsync(_bucketName, policyJson);
            }
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не роняем приложение, если это временная проблема сети
            Console.WriteLine($"Ошибка при создании бакета: {ex.Message}");
        }
    }
}
