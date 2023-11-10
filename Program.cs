using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Connectors.Memory.Redis;
using Microsoft.SemanticKernel.Text;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using StackExchange.Redis;
using NRedisStack.Search;

string aoaiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!;
string aoaiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!;
string redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")!;
string aoaiModel = "gpt35";
string aoaiEmbeddingModel = "text-embedding-ada-002";


// ConnectionMultiplexer should be a singleton instance in your application, please consider to dispose of it when your application shuts down.
// See https://stackexchange.github.io/StackExchange.Redis/Basics#basic-usage
ConnectionMultiplexer connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnection);
IDatabase database = connectionMultiplexer.GetDatabase();
RedisMemoryStore memoryStore = new RedisMemoryStore(database, vectorSize: 1536);

//// Initialize the kernel
//IKernel kernel = new KernelBuilder()
//    .WithAzureChatCompletionService(aoaiModel, aoaiEndpoint, aoaiApiKey)
//    .Build();

IKernel kernel = new KernelBuilder().WithAzureOpenAIChatCompletionService(aoaiModel, aoaiEndpoint, aoaiApiKey).Build();

// Download a document and create embeddings for it
ISemanticTextMemory memory = new MemoryBuilder()
    .WithLoggerFactory(kernel.LoggerFactory)
    .WithMemoryStore(memoryStore)
    .WithAzureOpenAITextEmbeddingGenerationService(aoaiEmbeddingModel, aoaiEndpoint, aoaiApiKey)
    .Build();

string collectionName = "net7perf";
using (HttpClient client = new())
{
    //string s = await client.GetStringAsync("https://devblogs.microsoft.com/dotnet/performance_improvements_in_net_7");
    //List<string> paragraphs =
    //    TextChunker.SplitPlainTextParagraphs(
    //        TextChunker.SplitPlainTextLines(
    //            WebUtility.HtmlDecode(Regex.Replace(s, @"<[^>]+>|&nbsp;", "")),
    //            128),
    //        1024);

    List<string> paragraphs = [".NET 7 is fast. Really fast. A thousand performance-impacting PRs went into runtime and core libraries this release, never mind all the improvements in ASP.NET Core and Windows Forms and Entity Framework and beyond. It’s the fastest .NET ever. "];

    for (int i = 0; i < paragraphs.Count; i++)
        await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");
}

// Create a new chat
IChatCompletion ai = kernel.GetService<IChatCompletion>();
ChatHistory chat = ai.CreateNewChat("You are an AI assistant that helps people find information.");
StringBuilder builder = new();

// Q&A loop
while (true)
{
    Console.Write("Question: ");
    string question = Console.ReadLine()!;

    builder.Clear();
    await foreach (var result in memory.SearchAsync(collectionName, question, limit: 3))
        builder.AppendLine(result.Metadata.Text);
    int contextToRemove = -1;
    if (builder.Length != 0)
    {
        builder.Insert(0, "Here's some additional information: ");
        contextToRemove = chat.Count;
        chat.AddUserMessage(builder.ToString());
    }

    chat.AddUserMessage(question);

    builder.Clear();
    await foreach (string message in ai.GenerateMessageStreamAsync(chat))
    {
        Console.Write(message);
        builder.Append(message);
    }
    Console.WriteLine();
    chat.AddAssistantMessage(builder.ToString());

    if (contextToRemove >= 0) chat.RemoveAt(contextToRemove);
    Console.WriteLine();
}