using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0027
#pragma warning disable SKEXP0052

// Add user secret to config provider and get the configuration object
var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

string AOAI_deploymentName = config["AOAI:deploymentName"] ?? "";
string AOAI_endPoint = config["AOAI:endPoint"] ?? "";
string AOAI_apiKey = config["AOAI:apiKey"] ?? "";
string AOAI_embeddingDeploymentName = config["AOAI:embeddingDeploymentName"] ?? "";
string REDIS_connectionString = config["REDIS:connectionString"] ?? "";

// Create a kernel with Azure OpenAI chat completion

var builder = Kernel
.CreateBuilder()
    .AddAzureOpenAITextEmbeddingGeneration(AOAI_embeddingDeploymentName, AOAI_endPoint, AOAI_apiKey)
    .AddAzureOpenAIChatCompletion(AOAI_deploymentName, AOAI_endPoint, AOAI_apiKey);


// Add Enterprise components
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

// Build the kernel
Kernel kernel = builder.Build();

var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Initialize a memory store using the redis database
// Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
ConnectionMultiplexer connection = ConnectionMultiplexer.Connect(REDIS_connectionString);
IDatabase _db = connection.GetDatabase();
IMemoryStore memoryStore = new RedisMemoryStore(_db);

// Retrieve the embedding service from the Kernel.
ITextEmbeddingGenerationService embeddingService =
    kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

// Initialize a SemanticTextMemory using the memory store and embedding generation service.
SemanticTextMemory textMemory = new(memoryStore, embeddingService);

// Initialize a TextMemoryPlugin using the text memory.
TextMemoryPlugin memoryPlugin = new(textMemory);

// Import the text memory plugin into the Kernel.
KernelPlugin memory = kernel.ImportPluginFromObject(memoryPlugin);




// Create a history store the conversation
var history = new ChatHistory();
// Initiate a back-and-forth chat
string? userInput;

do
{
    // Collect user input
    Console.Write("User > ");
    userInput = Console.ReadLine();

    // Add user input
    if (userInput is not null)
    {
        //history.AddUserMessage(userInput);
        // Retrieve a memory with the Kernel.
        FunctionResult searchResult = await kernel.InvokeAsync(
            memory["Recall"],
            new()
            {
                [TextMemoryPlugin.InputParam] = "Ask: "+userInput,
                [TextMemoryPlugin.CollectionParam] = "sk-documentation2",
                [TextMemoryPlugin.LimitParam] = "2",
                [TextMemoryPlugin.RelevanceParam] = "0.5",
            }
        );

        string? resultStr = searchResult.GetValue<string>();
        userInput += resultStr;
        history.AddUserMessage(userInput ?? string.Empty);
    }

    // Get response from the AI
    ChatMessageContent result  = await chatCompletionService.GetChatMessageContentAsync(history, kernel: kernel);

    // Print the results
    Console.WriteLine("Assistance > " + result);

    // Add the message from the agennt to the chat history
    history.AddMessage(result.Role, result.Content ?? string.Empty);

} while (userInput is not null);

