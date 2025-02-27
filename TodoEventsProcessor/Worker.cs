using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Messaging.ServiceBus;
using System.Text;
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly BlobServiceClient _blobServiceClient;
    private ServiceBusProcessor _processor;
    private const string QueueName = "todoevents";
    private const string ContainerName = "todo-events";

    public Worker(
        ILogger<Worker> logger,
        ServiceBusClient serviceBusClient,
        BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _blobServiceClient = blobServiceClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        _processor = _serviceBusClient.CreateProcessor(QueueName);

        _processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var body = args.Message.Body.ToString();
                _logger.LogInformation($"Received message: {body}");

                // Save to blob storage
                var blobName = $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}-{Guid.NewGuid()}.json";
                var blobClient = containerClient.GetBlobClient(blobName);
                
                await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(body));
                await blobClient.UploadAsync(ms, overwrite: true);

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        };

        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Error handling message");
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            await _processor.StopProcessingAsync();
        }
    }
}