using Azure.AI.OpenAI;
using Azure.Core;
using Json.Schema.Generation.Intents;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Text;
using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using StackExchange.Redis;

#pragma warning disable SKEXP0003
#pragma warning disable SKEXP0011
#pragma warning disable SKEXP0027
#pragma warning disable SKEXP0055

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

string aoaiEndpoint = config["aoaiEndpoint"];
string aoaiApiKey = config["aoaiApiKey"];
string redisConnection = config["redisConnection"];
string aoaiModel = config["aoaiModel"];
string aoaiEmbeddingModel = config["aoaiEmbeddingModel"];

var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(aoaiModel, aoaiEndpoint, aoaiApiKey);
builder.AddAzureOpenAITextEmbeddingGeneration("TextEmbeddingAda002_1", aoaiEndpoint, aoaiApiKey);

Kernel kernel = builder.Build();

// See https://stackexchange.github.io/StackExchange.Redis/Basics#basic-usage
ConnectionMultiplexer connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnection);
IDatabase database = connectionMultiplexer.GetDatabase();
RedisMemoryStore memoryStore = new RedisMemoryStore(database, vectorSize: 1536);

string collectionName = "Fsharpupdate";
ISemanticTextMemory memory = new MemoryBuilder()
        .WithLoggerFactory(kernel.LoggerFactory)
        .WithMemoryStore(memoryStore)
        .WithAzureOpenAITextEmbeddingGeneration(aoaiEmbeddingModel, aoaiEndpoint, aoaiApiKey)
        .Build();

using (HttpClient client = new())
{
    string s = await client.GetStringAsync("https://devblogs.microsoft.com/dotnet/overhauled-fsharp-code-fixes-in-visual-studio/");
    List<string> paragraphs =
        TextChunker.SplitPlainTextParagraphs(
            TextChunker.SplitPlainTextLines(
                WebUtility.HtmlDecode(Regex.Replace(s, @"<[^>]+>|&nbsp;", "")),
                128),
            1024);

    for (int i = 0; i < paragraphs.Count; i++)
        await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");
}



string TimePrompt = @$"
Today is: {DateTime.UtcNow:r}
Current time is: {DateTime.UtcNow:r}
Time now is: {DateTime.UtcNow:r}

Answer to the following questions using JSON syntax, including the data used.
Is it morning, afternoon, evening, or night (morning/afternoon/evening/night)?
Is it weekend time (weekend/not weekend)?
";

// Create a Semantic Kernel template for chat
var promptFunction = kernel.CreateFunctionFromPrompt(
    TimePrompt+ @"    
    {{$history}}
    User: {{$request}}
    Assistant:
    "
);

// Create a new chat
ChatHistory chat = [new ChatMessageContent(AuthorRole.System, "You are an AI assistant that helps people find information.")];
StringBuilder stbuilder = new();

// Start the chat loop
while (true)
{
    Console.Write("Question: ");
    string question = Console.ReadLine()!;

    stbuilder.Clear();
    await foreach (var result in memory.SearchAsync(collectionName, question, limit: 3))
        stbuilder.AppendLine(result.Metadata.Text);
    int contextToRemove = -1;
    if (stbuilder.Length != 0)
    {
        stbuilder.Insert(0, "Here's some additional information: ");
        contextToRemove = chat.Count;
        chat.AddUserMessage(stbuilder.ToString());
    }

    chat.AddUserMessage(question);

    stbuilder.Clear();
    await foreach (StreamingChatMessageContent message in kernel.InvokeStreamingAsync<StreamingChatMessageContent>(
        promptFunction,
        new() {
            { "request", question },
            { "history", string.Join("\n", chat.Select(x => x.Role + ": " + x.Content)) }

        }))
    {
        Console.Write(message);
        stbuilder.Append(message);
    }
    Console.WriteLine();
    chat.AddAssistantMessage(stbuilder.ToString());

    if (contextToRemove >= 0) chat.RemoveAt(contextToRemove);
    Console.WriteLine();
}
