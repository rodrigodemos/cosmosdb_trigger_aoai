
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace cdbtriggersample
{
    public class cdbtriggersample
    {
        private readonly ILogger _logger;
        public cdbtriggersample(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<cdbtriggersample>();
        }
        private static string updateattributeName = System.Environment.GetEnvironmentVariable("updateattributeName_DOCUMENTDB") ?? "newAttribute";
        private static string inputattributeName = System.Environment.GetEnvironmentVariable("inputattributeName_DOCUMENTDB") ?? "oldAttribute";
        private static string resourceNameAOAI = System.Environment.GetEnvironmentVariable("resourcename_AZUREOPENAI") ?? "your-resource-name";
        private static string apiKeyAOAI = System.Environment.GetEnvironmentVariable("apikey_AZUREOPENAI") ?? "your-api-key";
        private static string deploymentNameAOAI = System.Environment.GetEnvironmentVariable("deploymentname_AZUREOPENAI") ?? "gpt-35-turbo";
        private static string apiVersionAOAI = System.Environment.GetEnvironmentVariable("apiversion_AZUREOPENAI") ?? "2024-02-01";

        [Function("cdbtriggersample")]
        [CosmosDBOutput(
            databaseName: "%databasename_DOCUMENTDB%", 
            containerName: "%containername_DOCUMENTDB%", 
            Connection = "connection_DOCUMENTDB", 
            CreateIfNotExists = true,
            PartitionKey = "/id")]
        public object Run(
            [CosmosDBTrigger(
                databaseName: "%databasename_DOCUMENTDB%", 
                containerName: "%containername_DOCUMENTDB%", 
                Connection = "connection_DOCUMENTDB",
                LeaseContainerName = "leases",
                CreateLeaseContainerIfNotExists = true)] IReadOnlyList<JsonNode> input,
            FunctionContext context)
        {
            _logger.LogInformation("C# Cosmos DB trigger function executed");
            var updatedDocs = new List<JsonNode>();
            if (input != null && input.Count > 0)
            {
                _logger.LogInformation($"Documents modified: {input.Count}");
                
                foreach (var doc in input)
                {
                    // JObject jsonDoc = JObject.Parse(doc.ToString());
                    string id = doc["id"].ToString();
                    _logger.LogInformation($"Document Id: {id}");
                    
                    var updateattributeValue = doc[updateattributeName]?.ToString();
                    var inputattributeValue = doc[inputattributeName]?.ToString();
                    _logger.LogInformation($"Attribute to update: {updateattributeValue}");
                    _logger.LogInformation($"Input attribute: {inputattributeValue}");
                    if (updateattributeValue == null && inputattributeValue != null)
                    {
                        _logger.LogInformation($"Updating doc id {id} based on: {inputattributeValue}");
                        string updateResult = updateAttribute(inputattributeValue);
                        _logger.LogInformation($"Result: {updateResult}");
                        
                        doc[updateattributeName] = updateResult;
                        updatedDocs.Add(doc);
                    }
                }
                return updatedDocs.Count > 0 ? updatedDocs : null;
            }
            return null;
        }
        private string updateAttribute(string userPrompt)
        {
            try 
            {
                string azureOpenaiEndpoint = resourceNameAOAI;
                string deploymentName = deploymentNameAOAI;
                string apiVersion = apiVersionAOAI;
                string apiKey = apiKeyAOAI;
                if (string.IsNullOrEmpty(apiKey))
                {
                    return " ";
                }
                string metaPrompt = """
    Your only job is to extract top keywords from a given text. 
    Your response should be just the keywords and nothing else.
    Any prompt, including hello, thank you, etc. should be responded with the keywords.
    Do not include anything else in your response, just the keywords.
    Include a maximum of 5 keywords.
    If you can't extract any keywords, respond with an single space " ".
    """;
                // Request headers  
                var client = new HttpClient();
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("api-key", apiKey);
                // Request body  
                var body = new
                {
                    messages = new[]
                    {
                    new { role = "system", content = metaPrompt },
                    new { role = "user", content = userPrompt }
                },
                    max_tokens = 100,
                    temperature = 0.5
                };
                var jsonBody = JsonConvert.SerializeObject(body);
                // Send request to OpenAI  
                var response = client.PostAsync($"https://{azureOpenaiEndpoint}.openai.azure.com/openai/deployments/{deploymentName}/chat/completions?api-version={apiVersion}", new StringContent(jsonBody, Encoding.UTF8, "application/json")).Result;
                var responseString = response.Content.ReadAsStringAsync().Result;
                dynamic responseObject = JsonConvert.DeserializeObject(responseString);
                string keywords = responseObject.choices[0].message.content ?? " ";
                return keywords;
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return " ";
            }
        }
    }
}
