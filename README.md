# Running a chat console app locally
This application is a proof of concept for using Redis cache as memory store and other purposes in an intelligent chat application.
## Pre-requisites
* Azure subscription
* .NET 8 or later
* Create an Azure Cache for Redis Enterprise with RediSearch module enabled
* Create Azure OpenAI service and deploy the following models:

    * gpt-35-turbo
    * text-embedding-ada-002

    Note down the deployment names

## Running the application
1. Open a command prompt. Change directory to the folder containing .csproj file.
2. Initialize and set secret configuration values for running locally
```
dotnet user-secrets init
dotnet user-secrets set "redisConnection" "your_rediscache_connectionstring"
dotnet user-secrets set "aoaiModel" "your_gptmodel_deploymentname"
dotnet user-secrets set "aoaiEndpoint" "your_openai_endpoint"
dotnet user-secrets set "aoaiEmbeddingModel" "your_textembeddingadamodel_deployment"
dotnet user-secrets set "aoaiApiKey" "your_azureopenai_key"
```
3. Build and run the application
```
dotnet build
dotnet run
```
4. The chat application will ask your name at the beginning. If it finds your name and preferences in the cache, it will not prompt you for the preferences, and instead will directly jump to the question/answers prompt. Else it will ask for your preferences and talk to you in a certain tone. For example, you and ask it to "talk in poetic style", it will return all the answers in the lines of poets!

    ![Poetic answers from gpt](./images/poetic-answer-from-gpt.png)

    ![Cached user prefrence](./images/Cached-system-prompt.png)

5. The command will prompt you to ask question. You can ask the following questions to test the app is running correctly:
    
    * What is the current time? *This question is from the embedded function in the system prompt. ChatGPT cannot answer such questions by itself as it's only a language model that doesn't have knowledge on real time values*
    * Tell me a joke. Then followed by Why is this joke funny? *These two questions test the chat history is working properly. By default each question to chatgpt is a separate session. Unless stored in chat history, the conversation doesn't have previous context info*
    * What is the F# FS0010 error, and how to trouble shoot it? *This question will find answer in the blog post https://devblogs.microsoft.com/dotnet/overhauled-fsharp-code-fixes-in-visual-studio/ which is released after gpt35 is trained. The application can answer this question because the content of the blog is saved in the external memory store as vector embeddings in Redis*