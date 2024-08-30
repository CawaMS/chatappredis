using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Add user secret to config provider and get the configuration object
var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

string AOAI_deploymentName = config["AOAI:deploymentName"] ?? "";
string AOAI_endPoint = config["AOAI:endPoint"] ?? "";
string AOAI_apiKey = config["AOAI:apiKey"] ?? "";

// Create a kernel with Azure OpenAI chat completion
var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(AOAI_deploymentName, AOAI_endPoint, AOAI_apiKey);

// Add Enterprise components
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Error));

// Build the kernel
Kernel kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();



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
        history.AddUserMessage(userInput);
    }
    

    // Get response from the AI
    ChatMessageContent result  = await chatCompletionService.GetChatMessageContentAsync(history, kernel: kernel);

    // Print the results
    Console.WriteLine("Assistance > " + result);

    // Add the message from the agennt to the chat history
    history.AddMessage(result.Role, result.Content ?? string.Empty);

} while (userInput is not null);

