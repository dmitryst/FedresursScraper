using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace FedresursScraper.Services;

/// <summary>
/// Базовый интерфейс с общим функционалом
/// </summary>
public interface IFileStorageService
{
    // Возвращает публичную ссылку на сохраненный файл
    Task<string> UploadAsync(byte[] fileData, string fileName, string contentType = "image/jpeg");
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType = "image/jpeg");
    Task DeleteAsync(string fileName);
}

/// <summary>
/// Интерфейс специально для фотографий объявлений пользователей
/// </summary>
public interface IUserAdsFileStorageService : IFileStorageService
{
}

/// <summary>
/// Интерфейс специально для фотографий лотов
/// </summary>
public interface ILotsFileStorageService : IFileStorageService
{
}


public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _publicUrlBase;

    public S3FileStorageService(IAmazonS3 s3Client, string bucketName, string publicUrlBase)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _publicUrlBase = publicUrlBase ?? throw new ArgumentNullException(nameof(publicUrlBase));
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

    public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType = "image/jpeg")
    {
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = fileStream, // Передаем поток напрямую
            Key = fileName,
            BucketName = _bucketName,
            CannedACL = S3CannedACL.PublicRead,
            ContentType = contentType
        };

        var fileTransferUtility = new TransferUtility(_s3Client);
        await fileTransferUtility.UploadAsync(uploadRequest);

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

// Реализация для объявлений
public class UserAdsFileStorageService : S3FileStorageService, IUserAdsFileStorageService
{
    public UserAdsFileStorageService(IAmazonS3 s3Client, IConfiguration config)
        : base(s3Client,
               config["S3:UserAdsBucketName"] ?? throw new ArgumentNullException("S3:UserAdsBucketName"),
               config["S3:PublicUrlBase"] ?? throw new ArgumentNullException("S3:PublicUrlBase"))
    {
    }
}

// Реализация для лотов
public class LotsFileStorageService : S3FileStorageService, ILotsFileStorageService
{
    public LotsFileStorageService(IAmazonS3 s3Client, IConfiguration config)
        : base(s3Client,
               config["S3:LotsBucketName"] ?? throw new ArgumentNullException("S3:LotsBucketName"),
               config["S3:PublicUrlBase"] ?? throw new ArgumentNullException("S3:PublicUrlBase"))
    {
    }
}