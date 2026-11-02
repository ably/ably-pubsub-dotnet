// Error Types Tests (TI1, TI2, TI3, TI4, TI5)
// Derived from uts/rest/unit/types/error_types.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class ErrorTypesTests : UtsUnitTestBase
    {
        public ErrorTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TI1/errorinfo-attributes-0
        [Fact]
        [Trait("spec", "TI1")]
        public void TI1_ErrorinfoAttributes()
        {
            // Test Steps
            // TI1 - code attribute
            var error = new ErrorInfo(reason: null, code: 40000);
            error.Code.Should().Be(40000);

            // TI2 - statusCode attribute
            error = new ErrorInfo(reason: null, code: 40100, statusCode: HttpStatusCode.Unauthorized);
            ((int?)error.StatusCode).Should().Be(401);

            // TI3 - message attribute
            error = new ErrorInfo(
                reason: "Bad request: invalid parameter",
                code: 40000,
                statusCode: HttpStatusCode.BadRequest);
            error.Message.Should().Be("Bad request: invalid parameter");

            // TI4 - href attribute (optional)
            error = new ErrorInfo(
                reason: null,
                code: 40000,
                href: "https://help.ably.io/error/40000");
            error.Href.Should().Be("https://help.ably.io/error/40000");

            // TI5 - cause attribute (optional)
            // NOTE: the spec's cause is an Exception; ErrorInfo.Cause is an ErrorInfo, an exception cause lives in ErrorInfo.InnerException (rest-mapping.md section 5)
            var originalError = new Exception("Network failure");
            error = new ErrorInfo(
                reason: "Timeout",
                code: 50003,
                statusCode: HttpStatusCode.InternalServerError,
                innerException: originalError);
            error.InnerException.Should().BeSameAs(originalError);
        }

        // UTS: rest/unit/TI/errorinfo-from-json-0
        [Fact]
        [Trait("spec", "TI")]
        public void TI_ErrorinfoFromJson()
        {
            // Test Steps
            var jsonResponse = new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = 40100,
                    ["statusCode"] = 401,
                    ["message"] = "Token expired",
                    ["href"] = "https://help.ably.io/error/40100",
                },
            };

            var error = JsonHelper.DeserializeObject<ErrorInfo>((JObject)jsonResponse["error"]);

            // Assertions
            error.Code.Should().Be(40100);
            ((int?)error.StatusCode).Should().Be(401);
            error.Message.Should().Be("Token expired");
            error.Href.Should().Be("https://help.ably.io/error/40100");
        }

        // UTS: rest/unit/TI/errorinfo-nested-cause-1
        [Fact]
        [Trait("spec", "TI")]
        public void TI_ErrorinfoNestedCause()
        {
            // Test Steps
            var jsonResponse = new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = 50000,
                    ["statusCode"] = 500,
                    ["message"] = "Internal error",
                    ["cause"] = new JObject
                    {
                        ["code"] = 50001,
                        ["message"] = "Database connection failed",
                    },
                },
            };

            var error = JsonHelper.DeserializeObject<ErrorInfo>((JObject)jsonResponse["error"]);

            // Assertions
            error.Code.Should().Be(50000);

            // NOTE: ErrorInfo.Cause is statically an ErrorInfo, so the spec's "IS ErrorInfo OR IS Exception" reduces to "Cause is set"
            // ASSERT error.cause IS ErrorInfo OR error.cause IS Exception
            error.Cause.Should().NotBeNull();

            // IF error.cause IS ErrorInfo:
            error.Cause.Code.Should().Be(50001);
            error.Cause.Message.Should().Be("Database connection failed");
        }

        // UTS: rest/unit/TI/ably-exception-wraps-errorinfo-2
        [Fact]
        [Trait("spec", "TI")]
        public void TI_AblyExceptionWrapsErrorinfo()
        {
            // Test Steps
            var errorInfo = new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest);

            var exception = new AblyException(errorInfo);

            // Assertions
            // NOTE: exception.code, .statusCode and .message are read through exception.ErrorInfo; AblyException.Message is ErrorInfo.ToString(), not the error message (rest-mapping.md section 7)
            exception.ErrorInfo.Code.Should().Be(40000);
            ((int?)exception.ErrorInfo.StatusCode).Should().Be(400);
            exception.ErrorInfo.Message.Should().Be("Bad request");
            exception.ErrorInfo.Should().BeSameAs(errorInfo);
        }

        // UTS: rest/unit/TI/common-error-codes-3
        [Fact]
        [Trait("spec", "TI")]
        public void TI_CommonErrorCodes()
        {
            // Test Cases
            var testCases = new[]
            {
                new { Code = 40000, Status = 400, Meaning = "Bad request" },
                new { Code = 40100, Status = 401, Meaning = "Unauthorized" },
                new { Code = 40101, Status = 401, Meaning = "Invalid credentials" },
                new { Code = 40140, Status = 401, Meaning = "Token error" },
                new { Code = 40142, Status = 401, Meaning = "Token expired" },
                new { Code = 40160, Status = 401, Meaning = "Invalid capability" },
                new { Code = 40300, Status = 403, Meaning = "Forbidden" },
                new { Code = 40400, Status = 404, Meaning = "Not found" },
                new { Code = 50000, Status = 500, Meaning = "Internal server error" },
                new { Code = 50003, Status = 500, Meaning = "Timeout" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                var error = new ErrorInfo(
                    reason: testCase.Meaning,
                    code: testCase.Code,
                    statusCode: (HttpStatusCode)testCase.Status);

                error.Code.Should().Be(testCase.Code);
                ((int?)error.StatusCode).Should().Be(testCase.Status);
            }
        }

        // UTS: rest/unit/TI/error-string-representation-4
        [Fact]
        [Trait("spec", "TI")]
        public void TI_ErrorStringRepresentation()
        {
            // Test Steps
            var error = new ErrorInfo(
                reason: "Unauthorized: token expired",
                code: 40100,
                statusCode: HttpStatusCode.Unauthorized);

            var stringRepr = error.ToString();

            // String should include key information
            stringRepr.Should().Contain("40100");
            stringRepr.Should().Contain("401");
            (stringRepr.Contains("Unauthorized") || stringRepr.Contains("token")).Should().BeTrue();
        }

        // UTS: rest/unit/TI/error-equality-5
        // DEVIATION: see deviations.md
        [DeviationFact("TI", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TI")]
        public void TI_ErrorEquality()
        {
            // Test Steps
            var error1 = new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest);
            var error2 = new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest);
            var error3 = new ErrorInfo("Unauthorized", 40100, HttpStatusCode.Unauthorized);

            // Assertions
            error1.Should().Be(error2, "same content");
            error1.Should().NotBe(error3, "different code");
        }
    }
}
