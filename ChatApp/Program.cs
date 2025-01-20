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
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Error));

// Build the kernel
Kernel kernel = builder.Build();

// Retrieve the chat completion service from the Kernel.
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
// Retrieve the embedding service from the Kernel.
ITextEmbeddingGenerationService embeddingService = kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

// Initialize a memory store using the redis database
ConnectionMultiplexer connection = ConnectionMultiplexer.Connect(REDIS_connectionString);
IDatabase _db = connection.GetDatabase();
RedisMemoryStore memoryStore = new RedisMemoryStore(_db);

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
        // Retrieve a memory with the Kernel.
        FunctionResult searchResult = await kernel.InvokeAsync(
            memory["Recall"],
            new()
            {
                [TextMemoryPlugin.InputParam] = "Ask: "+userInput,
                [TextMemoryPlugin.CollectionParam] = "sk-documentation2",
                [TextMemoryPlugin.LimitParam] = 2,
                [TextMemoryPlugin.RelevanceParam] = 0.5
            }
        );

        // User the result to augment the prompt
        history.AddUserMessage(userInput + searchResult.GetValue<string>());
    }

    // Get response from the AI
    ChatMessageContent result  = await chatCompletionService.GetChatMessageContentAsync(history, kernel: kernel);

    // Print the results
    Console.WriteLine("Assistance > " + result);

    // Add the message from the agennt to the chat history
    history.AddMessage(result.Role, result.Content ?? string.Empty);

} while (userInput is not null);

