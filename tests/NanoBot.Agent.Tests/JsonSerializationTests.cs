using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Xunit;

namespace NanoBot.Agent.Tests;

public class JsonSerializationTests
{
    [Fact]
    public void Test_BrowserTool_Result_Serialization_Flow()
    {
        // 1. Simulate BrowserTools returning a JSON string with UnsafeRelaxedJsonEscaping
        var browserResultObj = new { Ok = true, Action = "open", Url = "https://www.163.com", Title = "网易" };
        
        var toolJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        // This is what BrowserTools.ExecuteAsync returns
        string toolResultString = JsonSerializer.Serialize(browserResultObj, toolJsonOptions);
        
        // Expect: {"ok":true,"action":"open","url":"https://www.163.com","title":"网易"}
        // Verify no unicode escaping for Chinese
        Assert.Contains("网易", toolResultString); 
        Assert.DoesNotContain("\\u", toolResultString);

        // 2. Simulate SessionManager saving this result
        // SessionManager puts this string into a JsonObject
        
        var sessionManagerJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var messageObj = new JsonObject
        {
            ["role"] = "tool",
            ["content"] = toolResultString, // The JSON string is the content
            ["timestamp"] = DateTime.Now.ToString("o")
        };

        // This is what gets written to the .jsonl file
        string jsonlLine = messageObj.ToJsonString(sessionManagerJsonOptions);
        
        // Verify the line is valid JSON
        // content field should be escaped properly as a JSON string value, but inner content should be readable if possible
        // Note: System.Text.Json will escape quotes inside the string value.
        // Expected: "content":"{\"ok\":true,\"action\":\"open\",\"url\":\"https://www.163.com\",\"title\":\"网易\"}"
        
        // Check if Chinese characters are escaped in the outer JSON
        Assert.Contains("网易", jsonlLine);
        
        // CRITICAL CHECK: Does the serialized string contain unicode escaped quotes?
        // If sessionManagerJsonOptions is configured correctly with UnsafeRelaxedJsonEscaping,
        // it should produce \" for quotes, NOT \u0022.
        Assert.DoesNotContain("\\u0022", jsonlLine);
        Assert.Contains("\\\"", jsonlLine); // Should use standard escape

        // 3. Simulate SessionService reading this line
        var loadedMsg = JsonSerializer.Deserialize<JsonElement>(jsonlLine);
        string loadedContent = loadedMsg.GetProperty("content").GetString();

        // loadedContent should match toolResultString exactly
        Assert.Equal(toolResultString, loadedContent);
        Assert.Contains("网易", loadedContent);
        Assert.DoesNotContain("\\u", loadedContent);

        // 4. Simulate SessionService formatting for UI
        // It tries to deserialize the content string as JSON object again to pretty print it
        
        string formattedContent = loadedContent;
        if (loadedContent.TrimStart().StartsWith("{"))
        {
            var resultObj = JsonSerializer.Deserialize<JsonElement>(loadedContent);
            
            var uiJsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            
            formattedContent = JsonSerializer.Serialize(resultObj, uiJsonOptions);
        }

        // Check final UI output
        Assert.Contains("网易", formattedContent);
        Assert.DoesNotContain("\\u", formattedContent);
    }

    [Fact]
    public void Test_Double_Serialization_With_Default_Encoder()
    {
        // Suppose BrowserTools returns this string (clean JSON)
        string toolResult = "{\"Ok\":true}";
        
        // Now suppose something wraps this string and serializes it using Default Encoder
        var wrapper = new { Content = toolResult };
        var defaultOptions = new JsonSerializerOptions(); // Strict encoder
        
        string serialized = JsonSerializer.Serialize(wrapper, defaultOptions);
        
        // Expected: {"Content":"{\u0022Ok\u0022:true}"}
        // Because default encoder escapes " inside string values as \u0022 to be HTML safe!
        
        Assert.Contains("\\u0022", serialized);
        Assert.Contains("{\\u0022Ok\\u0022:true}", serialized);
    }
}
