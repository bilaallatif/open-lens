#:package Azure.Messaging.ServiceBus
#:property ArtifactsPath=.

using Azure.Messaging.ServiceBus;

const string connectionString =
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
const string queueName = "image-uploaded";

const string blobName = "test.jpg";
const string containerName = "images";
var subject = $"/blobServices/default/containers/{containerName}/blobs/{blobName}";
var body = BinaryData.FromString($$"""{"Subject":"{{subject}}"}""");

await using var client = new ServiceBusClient(connectionString);
var sender = client.CreateSender(queueName);
await sender.SendMessageAsync(new ServiceBusMessage(body));

Console.WriteLine($"Sent: BlobName={blobName}, ContainerName={containerName}");
