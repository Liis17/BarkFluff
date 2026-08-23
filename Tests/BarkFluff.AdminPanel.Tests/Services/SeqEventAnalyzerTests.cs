using Barkfluff.AdminPanel.Services;

using System.Text.Json;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class SeqEventAnalyzerTests
{
    [Fact]
    public void GroupErrors_GroupsSameTemplateAndExceptionType()
    {
        var events = new[]
        {
            Parse("""
            {
              "Id":"evt-1","Timestamp":"2026-08-23T10:00:00Z","Level":"Error",
              "MessageTemplate":"Request {Path} failed","RenderedMessage":"Request /one failed",
              "Exception":"System.TimeoutException: first timeout",
              "Properties":{"Application":"BarkFluff.Messages","CorrelationId":"corr-1"}
            }
            """),
            Parse("""
            {
              "Id":"evt-2","Timestamp":"2026-08-23T10:05:00Z","Level":"Fatal",
              "MessageTemplate":"Request {Path} failed","RenderedMessage":"Request /two failed",
              "Exception":"System.TimeoutException: second timeout",
              "Properties":{"Application":"BarkFluff.Messages","CorrelationId":"corr-2","RequestId":"req-2"}
            }
            """)
        };

        var group = Assert.Single(SeqEventAnalyzer.GroupErrors(events));

        Assert.Equal(2, group.Count);
        Assert.Equal("BarkFluff.Messages", group.Application);
        Assert.Equal("evt-2", group.RepresentativeEventId);
        Assert.Equal("corr-2", group.CorrelationId);
        Assert.Equal("req-2", group.RequestId);
        Assert.Equal(DateTime.Parse("2026-08-23T10:00:00Z").ToUniversalTime(), group.FirstSeenUtc);
        Assert.Equal(DateTime.Parse("2026-08-23T10:05:00Z").ToUniversalTime(), group.LastSeenUtc);
    }

    [Fact]
    public void GroupErrors_KeepsApplicationsSeparateAndSkipsNonErrors()
    {
        var events = new[]
        {
            Parse("""{"Level":"Error","MessageTemplate":"Failed","Properties":{"Application":"BarkFluff.Files"}}"""),
            Parse("""{"Level":"Error","MessageTemplate":"Failed","Properties":{"Application":"BarkFluff.Users"}}"""),
            Parse("""{"Level":"Warning","MessageTemplate":"Failed","Properties":{"Application":"BarkFluff.Files"}}""")
        };

        var groups = SeqEventAnalyzer.GroupErrors(events);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.Application == "BarkFluff.Files");
        Assert.Contains(groups, group => group.Application == "BarkFluff.Users");
    }

    [Fact]
    public void ReadContext_SupportsArrayPropertiesAndNumericUserId()
    {
        var evt = Parse("""
        {
          "Properties":[
            {"Name":"TraceId","Value":"trace-1"},
            {"Name":"TraceIdentifier","Value":"request-1"},
            {"Name":"AffectedUserId","Value":42}
          ]
        }
        """);

        var context = SeqEventAnalyzer.ReadContext(evt);

        Assert.Equal("trace-1", context.TraceId);
        Assert.Equal("trace-1", context.CorrelationId);
        Assert.Equal("request-1", context.RequestId);
        Assert.Equal("42", context.UserId);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
