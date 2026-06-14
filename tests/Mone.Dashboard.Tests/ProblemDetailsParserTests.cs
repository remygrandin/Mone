using Mone.Dashboard.Services;
using Xunit;

namespace Mone.Dashboard.Tests;

public class ProblemDetailsParserTests
{
    [Fact]
    public void DetailOnly_ReturnsDetail()
    {
        var body = """{"type":"https://example.com/bad-request","status":400,"detail":"Name is required."}""";
        Assert.Equal("Name is required.", ProblemDetailsParser.ExtractMessage(body));
    }

    [Fact]
    public void TitleOnly_ReturnsTitle()
    {
        var body = """{"type":"https://example.com/bad-request","title":"Bad Request","status":400}""";
        Assert.Equal("Bad Request", ProblemDetailsParser.ExtractMessage(body));
    }

    [Fact]
    public void BothPresent_DetailWins()
    {
        var body = """{"title":"Bad Request","status":400,"detail":"Name is required."}""";
        Assert.Equal("Name is required.", ProblemDetailsParser.ExtractMessage(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrNull_ReturnsNull(string? body)
    {
        Assert.Null(ProblemDetailsParser.ExtractMessage(body));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ malformed json")]
    public void Malformed_ReturnsNull(string body)
    {
        Assert.Null(ProblemDetailsParser.ExtractMessage(body));
    }

    [Fact]
    public void ObjectWithoutDetailOrTitle_ReturnsNull()
    {
        var body = """{"type":"https://example.com/bad-request","status":400}""";
        Assert.Null(ProblemDetailsParser.ExtractMessage(body));
    }

    [Theory]
    [InlineData("\"just a bare string\"")]
    [InlineData("42")]
    [InlineData("[\"detail\",\"title\"]")]
    public void NonObjectJson_ReturnsNull(string body)
    {
        Assert.Null(ProblemDetailsParser.ExtractMessage(body));
    }

    [Fact]
    public void NonStringDetail_FallsBackToTitle()
    {
        var body = """{"title":"Bad Request","detail":123}""";
        Assert.Equal("Bad Request", ProblemDetailsParser.ExtractMessage(body));
    }
}
