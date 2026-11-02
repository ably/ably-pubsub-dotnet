namespace Assets.Tests.AblySandbox
{
    public class TestAppSetup
    {
        public static string _testAppSetup = @"
        {
          ""_post_apps"": ""/* JSON body using in POST sandbox.realtime.ably-nonprod.net/apps request to set up the Test app */"",
          ""post_apps"": {
            ""limits"": { ""presence"": { ""maxMembers"": 250 } },
            ""keys"": [
              {},
              {
                ""capability"": ""{ \""cansubscribe:*\"":[\""subscribe\""], \""canpublish:*\"":[\""publish\""], \""canpublish:andpresence\"":[\""presence\"",\""publish\""], \""pushenabled:*\"":[\""publish\"",\""subscribe\"",\""push-subscribe\""], \""pushenabled:admin:*\"":[\""publish\"",\""subscribe\"",\""push-admin\""] }""
              },
              {
                ""capability"": ""{ \""channel0\"":[\""publish\""], \""channel1\"":[\""publish\""], \""channel2\"":[\""publish\"", \""subscribe\""], \""channel3\"":[\""subscribe\""], \""channel4\"":[\""presence\"", \""publish\"", \""subscribe\""], \""channel5\"":[\""presence\""], \""channel6\"":[\""*\""] }""
              },
              {
                ""capability"": ""{ \""*\"":[\""subscribe\""] }""
              }
            ],
            ""namespaces"": [
              { ""id"": ""persisted"", ""persisted"": true },
              { ""id"": ""pushenabled"", ""pushEnabled"": true }
            ],
            ""channels"": [
              {
                ""name"": ""persisted:presence_fixtures"",
                ""presence"": [
                  { ""clientId"": ""client_bool"",    ""data"": ""true"" },
                  { ""clientId"": ""client_int"",     ""data"": ""24"" },
                  { ""clientId"": ""client_string"",  ""data"": ""This is a string clientData payload"" },
                  { ""clientId"": ""client_json"",    ""data"": ""{ \""test\"": \""This is a JSONObject clientData payload\""}"" },
                  { ""clientId"": ""client_decoded"", ""data"": ""{\""example\"":{\""json\"":\""Object\""}}"", ""encoding"": ""json"" },
                  {
                    ""clientId"": ""client_encoded"",
                    ""data"": ""HO4cYSP8LybPYBPZPHQOtuD53yrD3YV3NBoTEYBh4U0N1QXHbtkfsDfTspKeLQFt"",
                    ""encoding"": ""json/utf-8/cipher+aes-128-cbc/base64""
                  }
                ]
              }
            ]
          },

          ""_cipher"": ""/* Cipher configuration for client_encoded presence fixture data used in REST tests */"",
          ""cipher"": {
            ""algorithm"": ""aes"",
            ""mode"": ""cbc"",
            ""keylength"": 128,
            ""key"": ""WUP6u0K7MXI5Zeo0VppPwg=="",
            ""iv"": ""HO4cYSP8LybPYBPZPHQOtg==""
          }
        }
";

        public static string _statsFixture = @"
[
    {
      ""intervalId"": ""[[Interval1]]"",
      ""inbound"":  { ""realtime"": { ""messages"": { ""count"": 50, ""data"": 5000 } } },
      ""outbound"": { ""realtime"": { ""messages"": { ""count"": 20, ""data"": 2000 } } }
    },
    {
      ""intervalId"": ""[[Interval2]]"",
      ""inbound"":  { ""realtime"": { ""messages"": { ""count"": 60, ""data"": 6000 } } },
      ""outbound"": { ""realtime"": { ""messages"": { ""count"": 10, ""data"": 1000 } } }
    },
    {
      ""intervalId"": ""[[Interval3]]"",
      ""inbound"":       { ""realtime"": { ""messages"": { ""count"": 70, ""data"": 7000 } } },
      ""outbound"":      { ""realtime"": { ""messages"": { ""count"": 40, ""data"": 4000 } } },
      ""persisted"":     { ""presence"": { ""count"": 20, ""data"": 2000 } },
      ""connections"":   { ""tls"":      { ""peak"": 20,  ""opened"": 10 } },
      ""channels"":      { ""peak"": 50, ""opened"": 30 },
      ""apiRequests"":   { ""succeeded"": 50, ""failed"": 10 },
      ""tokenRequests"": { ""succeeded"": 60, ""failed"": 20 }
    }
  ]
";
    }
}