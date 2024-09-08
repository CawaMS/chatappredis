using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Embeddings;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SKMemoryIngest;
using System.Text.Json;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001 
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0050 

// Get configuration object
var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

// Replace with your values.
string embeddingDeploymentName = config["AOAI:embeddingDeploymentName"] ?? "";
string endpoint = config["AOAI:endpoint"] ?? "";
string apiKey = config["AOAI:apiKey"] ?? "";
string redisConnectionString = config["Redis:connectionString"] ?? "";

var builder = Kernel
    .CreateBuilder()
    .AddAzureOpenAITextEmbeddingGeneration(embeddingDeploymentName, endpoint, apiKey);


// Build the kernel and get the data uploader.
var kernel = builder.Build();

// Load the data.
var textParagraphs = DocumentReader.ReadParagraphs(
    new FileStream(
        "vector-store-data-ingestion-input.docx",
        FileMode.Open),
    "file:///c:/Users/cawa/Documents/vector-store-data-ingestion-input.docx");

// Initialize a connection to the Redis database.
ConnectionMultiplexer connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
IDatabase database = connectionMultiplexer.GetDatabase();

// Initialize a memory store using the redis database
IMemoryStore memoryStore = new RedisMemoryStore(database);

// Retrieve the embedding service from the Kernel.

ITextEmbeddingGenerationService embeddingService =
    kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

// Initialize a SemanticTextMemory using the memory store and embedding generation service.
SemanticTextMemory textMemory = new(memoryStore, embeddingService);

// Initialize a TextMemoryPlugin using the text memory.
TextMemoryPlugin memoryPlugin = new(textMemory);

// Import the text memory plugin into the Kernel.
KernelPlugin memory = kernel.ImportPluginFromObject(memoryPlugin);

// Load the data.
string memoryCollectionName = "sk-documentation2";
int i = 1;
foreach (var paragraph in textParagraphs)
{
    // Save a memory with the Kernel.
    FunctionResult result = await kernel.InvokeAsync(
        memory["Save"],
        new()
        {
            [TextMemoryPlugin.InputParam] = paragraph.Text,
            [TextMemoryPlugin.CollectionParam] = memoryCollectionName,
            [TextMemoryPlugin.KeyParam] = "paragraph"+i,
        }
    );

    // If memories are recalled, the function result can be deserialized as a string[].
    string? resultStr = result.GetValue<string>();
    string[]? parsedResult = string.IsNullOrEmpty(resultStr)
        ? null
        : JsonSerializer.Deserialize<string[]>(resultStr);
    Console.WriteLine(
        $"Saved memories: {(parsedResult?.Length > 0 ? resultStr : "Paragraph uploaded")}"
    );

    i++;
    Thread.Sleep(12000);
}

    //await dataUploader.GenerateEmbeddingsAndUpload(
    //"sk-documentation",
    //textParagraphs);