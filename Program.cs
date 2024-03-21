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
using chatapp;

#pragma warning disable SKEXP0003
#pragma warning disable SKEXP0011
#pragma warning disable SKEXP0027

//add user secret config
var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

//initialize variables
string aoaiEndpoint = config["aoaiEndpoint"];
string aoaiApiKey = config["aoaiApiKey"];
string redisConnection = config["redisConnection"];
string aoaiModel = config["aoaiModel"];
string aoaiEmbeddingModel = config["aoaiEmbeddingModel"];
const string systemMessageSet = "systemMessageSet";
const string userMessageSet = "userMessageSet";
const string assistantMessageSet = "assistantMessageSet";
string _historyContent = "";
ChatHistory chat = new ChatHistory();

RedisConnection _redisConnection = await RedisConnection.InitializeAsync(redisConnection);

//initialize semantic kernel middleware
var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(aoaiModel, aoaiEndpoint, aoaiApiKey);
builder.AddAzureOpenAITextEmbeddingGeneration("TextEmbeddingAda002_1", aoaiEndpoint, aoaiApiKey);

Kernel kernel = builder.Build();

// Initialize Redis memory store. See https://stackexchange.github.io/StackExchange.Redis/Basics#basic-usage
ConnectionMultiplexer connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnection);
IDatabase database = connectionMultiplexer.GetDatabase();
RedisMemoryStore memoryStore = new RedisMemoryStore(database, vectorSize: 1536);
string collectionName = "Fsharpupdate";
ISemanticTextMemory memory = new MemoryBuilder()
        .WithLoggerFactory(kernel.LoggerFactory)
        .WithMemoryStore(memoryStore)
        .WithAzureOpenAITextEmbeddingGeneration(aoaiEmbeddingModel, aoaiEndpoint, aoaiApiKey)
        .Build();

//Converting a blog post online into Vector Embeddings and save in Redis.
//This section is commented out intentionally because the chat app doesn't have to convert a blog into vector everytime. 
//using (HttpClient client = new())
//{
//    string s = await client.GetStringAsync("https://devblogs.microsoft.com/dotnet/overhauled-fsharp-code-fixes-in-visual-studio/");
//#pragma warning disable SKEXP0055 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
//    List<string> paragraphs =
//        TextChunker.SplitPlainTextParagraphs(
//            TextChunker.SplitPlainTextLines(
//                WebUtility.HtmlDecode(Regex.Replace(s, @"<[^>]+>|&nbsp;", "")),
//                128),
//            1024);
//#pragma warning restore SKEXP0055 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

//    for (int i = 0; i < paragraphs.Count; i++)
//        await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");
//}

//Custom function to get current time - this is something ChatGPT doesn't know about.
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

Console.WriteLine("Enter username");
var _userName = Console.ReadLine();

//initialize chat history with system message
var savedSystemMessages = await _redisConnection.BasicRetryAsync(async (db) => (await db.SetMembersAsync(_userName+":"+systemMessageSet)));

if (savedSystemMessages.Any())
{
    _historyContent += "System message: ";
    foreach (var message in savedSystemMessages)
    {
        chat.AddSystemMessage(message.ToString());
        _historyContent += $" {message}";
    }
    _historyContent += '\n';
}
else 
{
    Console.WriteLine("Enter your preferred chat style");
    var _systemMessage = Console.ReadLine();
    chat.AddSystemMessage("You are an AI assistant that helps people find information." + _systemMessage);
    await _redisConnection.BasicRetryAsync(async (db) => (await db.SetAddAsync(_userName+":"+systemMessageSet, "You are an AI assistant that helps people find information. " + _systemMessage)));
}

// initialize chat history with saved user messages and assistant messages and store as embeddings

//Get user message in a temporary chat history string
RedisValue[] userMsgList = await _redisConnection.BasicRetryAsync(async (db) => (await db.HashValuesAsync(_userName+":"+userMessageSet)));
if (userMsgList.Any())
{
    foreach (var userMsg in userMsgList)
    {
        //chat.AddUserMessage(userMsg.ToString());
        _historyContent += $"User:{userMsg} \n";
    }
}

//Get assistant message in a temporary chat history string
RedisValue[] assistantMsgList = await _redisConnection.BasicRetryAsync(async (db) => (await db.HashValuesAsync(_userName+":"+assistantMessageSet)));
if (assistantMsgList.Any())
{
    foreach (var assistantMsg in assistantMsgList)
    {
        //chat.AddAssistantMessage(assistantMsg.ToString());
        _historyContent += $"Assistant:{assistantMsg} \n";
    }
}

//store user and assistant messages as vector embeddings in Redis. Only previous session is saved.
if (_historyContent.Length > 0)
{
    await memory.SaveInformationAsync(_userName+"_chathistory", _historyContent, "lastsession");
}

StringBuilder stbuilder = new();

// Start the chat loop
while (true)
{
    Console.Write("Question: ");
    string question = Console.ReadLine()!;

    stbuilder.Clear();
    await foreach (var result in memory.SearchAsync(collectionName, question, limit: 1))
        stbuilder.AppendLine(result.Metadata.Text);
    await foreach (var result in memory.SearchAsync(_userName+"_chathistory", question, limit: 1))
        stbuilder.Append(result.Metadata.Text);
    int contextToRemove = -1;
    if (stbuilder.Length != 0)
    {
        stbuilder.Insert(0, "Here's some additional information: ");
        contextToRemove = chat.Count;
        chat.AddUserMessage(stbuilder.ToString());
       
    }

    chat.AddUserMessage(question);

    await _redisConnection.BasicRetryAsync(async (_db) => _db.HashSetAsync($"{_userName}:{userMessageSet}", [new HashEntry(new RedisValue(Utility.GetTimestamp()), question)]));


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
    await _redisConnection.BasicRetryAsync(async (db) => db.HashSetAsync($"{_userName}:{assistantMessageSet}", [new HashEntry(new RedisValue(Utility.GetTimestamp()), stbuilder.ToString())]));



    if (contextToRemove >= 0) chat.RemoveAt(contextToRemove);


    Console.WriteLine();

    //Expire chat history before 2 minutes to keep information up-to-date and relevant
    Utility.expireEntriesBefore(120,_redisConnection, $"{_userName}:{assistantMessageSet}");
    Utility.expireEntriesBefore(120, _redisConnection, $"{_userName}:{userMessageSet}");

}
