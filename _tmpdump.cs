using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using JuniorDev.Contracts;
public class Dump
{
    public static void Main(){
        var json = @"{
          \"AppConfig\": {
            \"Policy\": {
              \"Profiles\": {
                \"test\": {
                  \"Name\": \"Test Policy\",
                  \"ProtectedBranches\": [\"master\", \"main\"],
                  \"MaxFilesPerCommit\": 25,
                  \"RequireTestsBeforePush\": true,
                  \"RequireApprovalForPush\": true,
                  \"Limits\": {
                    \"CallsPerMinute\": 30,
                    \"Burst\": 5,
                    \"PerCommandCaps\": {
                      \"RunTests\": 2,
                      \"Push\": 1
                    }
                  }
                }
              },
              \"DefaultProfile\": \"test\",
              \"GlobalLimits\": {
                \"CallsPerMinute\": 100,
                \"Burst\": 10
              }
            }
          }
        }";
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();
        var appConfig = ConfigBuilder.GetAppConfig(config);
        Console.WriteLine(appConfig.Policy.Profiles["test"].Limits.PerCommandCaps["RunTests"]);
    }
}
