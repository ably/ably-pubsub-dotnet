using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Shared.Helpers
{
    internal static class JAssert
    {
        // todo: upgrade testing library - https://github.com/fluentassertions/fluentassertions.json/issues/7
        // https://stackoverflow.com/questions/52645603/how-to-compare-two-json-objects-using-c-sharp

        public static bool DeepEquals(JToken token1, JToken token2, ITestOutputHelper testOutputHelper)
        {
            var areEqual = JToken.DeepEquals(token1, token2);
            if (!areEqual)
            {
                var diff = JDiff.Differentiate(token1, token2);
                testOutputHelper.WriteLine($"Json Difference {diff}");
            }

            return areEqual;
        }
    }
}
