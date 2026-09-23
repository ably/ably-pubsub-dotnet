using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    [Collection("UnitTests")]
    [Trait("spec", "RSA8e")]
    public class AuthOptionsMergeTests
    {
        private AuthOptions GetBlankOptions()
        {
            return new AuthOptions();
        }

        // AuthOption names defined in RSA8e (made idiomatic for .Net)
        private readonly string[] _authOptionPropertyNames =
        {
            "AuthCallback",
            "AuthHeaders",
            "AuthMethod",
            "AuthParams",
            "AuthUrl",
            "Key",
            "QueryTime",
            "Token",
            "TokenDetails",
            "UseTokenAuth"
        };

        private AuthOptions GetCompleteOptions()
        {
            return new AuthOptions
            {
                AuthCallback = param => null,
                AuthHeaders = new Dictionary<string, string> { { "Test", "Test" } },
                AuthMethod = HttpMethod.Get,
                AuthParams = new Dictionary<string, string> { { "Test", "Test" } },
                AuthUrl = new Uri("http://www.google.com"),
                Key = "key",
                QueryTime = true,
                Token = "Token",
                TokenDetails = new TokenDetails("Token"),
                UseTokenAuth = true
            };
        }

        [Fact]
        public void AuthOptions_HasAllProperties()
        {
            var authOptions = GetCompleteOptions();
            _authOptionPropertyNames.Length.Should().Be(10);
            var props = authOptions.GetType().GetProperties();
            props.Length.Should().Be(_authOptionPropertyNames.Length);
            foreach (var propertyInfo in props)
            {
                _authOptionPropertyNames.Contains(propertyInfo.Name).Should().BeTrue();
                propertyInfo.CanRead.Should().BeTrue();
                propertyInfo.CanWrite.Should().BeTrue();
                propertyInfo.GetGetMethod(false).IsPublic.Should().BeTrue();
                propertyInfo.GetSetMethod(false).IsPublic.Should().BeTrue();
            }
        }

        [Fact]
        public void Merge_WithOptionsNotSet_OverwritesThem()
        {
            AuthOptions complete = GetCompleteOptions();
            var blankOptions = GetBlankOptions();
            blankOptions.Merge(complete);

            Assert.Equal(blankOptions.AuthHeaders, complete.AuthHeaders);
            Assert.Equal(blankOptions.AuthParams, complete.AuthParams);
            Assert.Equal(blankOptions.AuthParams, complete.AuthParams);
            Assert.Equal(blankOptions.AuthUrl, complete.AuthUrl);
            Assert.Equal(blankOptions.AuthCallback, complete.AuthCallback);
            Assert.Equal(blankOptions.QueryTime, complete.QueryTime);
            Assert.Equal(blankOptions.UseTokenAuth, complete.UseTokenAuth);
        }

        [Fact]
        public void Merge_WithOptionsNotSet_DoesNotOverwriteKey()
        {
            AuthOptions complete = GetCompleteOptions();
            var blankOptions = GetBlankOptions();
            complete.Merge(blankOptions);

            Assert.NotEqual(blankOptions.Key, complete.Key);
        }

        [Fact]
        public void Merge_WithCompleteOptions_DoesNotOverwriteAnything()
        {
            AuthOptions complete = GetCompleteOptions();
            var otherComplete = new AuthOptions
            {
                AuthHeaders = new Dictionary<string, string> { { "Complete", "Test" } },
                AuthParams = new Dictionary<string, string> { { "Complete", "Test" } },
                Token = "Complete",
                AuthUrl = new Uri("http://www.ably.io"),
                Key = "completeKey",
                QueryTime = true,
                AuthCallback = param => null
            };
            otherComplete.Merge(complete);

            Assert.NotEqual(otherComplete.AuthHeaders, complete.AuthHeaders);
            Assert.NotEqual(otherComplete.AuthParams, complete.AuthParams);
            Assert.NotEqual(otherComplete.AuthParams, complete.AuthParams);
            Assert.NotEqual(otherComplete.AuthUrl, complete.AuthUrl);
            Assert.NotEqual(otherComplete.AuthCallback, complete.AuthCallback);
        }
    }
}
