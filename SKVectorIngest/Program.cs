#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0020 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using SKVectorIngest;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

// Get configuration object
var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

// Replace with your values.
string deploymentName = config["AOAI:embeddingDeploymentName"] ?? "";
string endpoint = config["AOAI:endpoint"] ?? "";
string apiKey = config["AOAI:apiKey"] ?? "";
string redisConnectionString = config["Redis:connectionString"] ?? "";

// Register Azure Open AI text embedding generation service and Redis vector store.
var builder = Kernel.CreateBuilder()
    .AddAzureOpenAITextEmbeddingGeneration(deploymentName, endpoint, apiKey)
    .AddRedisVectorStore(redisConnectionString);

// Register the data uploader.
builder.Services.AddSingleton<DataUploader>();

// Build the kernel and get the data uploader.
var kernel = builder.Build();
var dataUploader = kernel.Services.GetRequiredService<DataUploader>();

// Load the data.
var textParagraphs = DocumentReader.ReadParagraphs(
    new FileStream(
        "vector-store-data-ingestion-input.docx",
        FileMode.Open),
    "file:///c:/Users/cawa/Documents/vector-store-data-ingestion-input.docx");

await dataUploader.GenerateEmbeddingsAndUpload(
    "sk-documentation",
    textParagraphs);
