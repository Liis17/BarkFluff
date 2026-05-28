using BarkFluff.Notification.Parsers;
using BarkFluff.Shared.Queue.Notifications;

namespace BarkFluff.Notification.Tests.Parsers;

public class HtmlEmailTemplateParserTests : IDisposable
{
    private readonly string _testDir;

    public HtmlEmailTemplateParserTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"notification_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(Path.Combine(_testDir, "Templates"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private void WriteTemplate(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_testDir, "Templates", fileName), content);
    }

    private async Task<string> ParseInTestDir(HtmlEmailTemplateParser parser, NotificationType type, Dictionary<string, string> payload)
    {
        var originalDir = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _testDir;
        try
        {
            return await parser.Parse(type, payload);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
        }
    }

    private void WriteDefaultTemplate(string content)
    {
        WriteTemplate("confirmation_account.html", content);
    }

    [Fact]
    public async Task Parse_ReplacesPlaceholdersFromPayload()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿusernameꟿꟿꟿ - ꟿꟿꟿconfirmation_codeꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>
        {
            ["username"] = "Alice",
            ["confirmation_code"] = "123456"
        };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain("Alice");
        result.Should().Contain("123456");
        result.Should().NotContain("ꟿꟿꟿusernameꟿꟿꟿ");
        result.Should().NotContain("ꟿꟿꟿconfirmation_codeꟿꟿꟿ");
    }

    [Fact]
    public async Task Parse_AddsCurrentYear()
    {
        WriteDefaultTemplate("<html>Year: ꟿꟿꟿcurrentyearꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain(DateTime.UtcNow.Year.ToString());
    }

    [Fact]
    public async Task Parse_AutoCurrentYearOverridesPayloadValue()
    {
        WriteDefaultTemplate("<html>Year: ꟿꟿꟿcurrentyearꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string> { ["currentyear"] = "1999" };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain(DateTime.UtcNow.Year.ToString());
        result.Should().NotContain("1999");
    }

    [Fact]
    public async Task Parse_LeavesUnknownPlaceholders()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿunknown_keyꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain("ꟿꟿꟿunknown_keyꟿꟿꟿ");
    }

    [Fact]
    public async Task Parse_HtmlEncodesValues()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿuser_inputꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string> { ["user_input"] = "<script>alert('xss')</script>" };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;");
        result.Should().NotContain("<script>");
    }

    [Fact]
    public async Task Parse_DoesNotModifyStaticTemplate()
    {
        WriteDefaultTemplate("<html><body>Hello World</body></html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Be("<html><body>Hello World</body></html>");
    }

    [Fact]
    public async Task Parse_MultiplePlaceholdersInSingleTemplate()
    {
        WriteDefaultTemplate(
            "<html>ꟿꟿꟿusernameꟿꟿꟿ, code: ꟿꟿꟿconfirmation_codeꟿꟿꟿ, ip: ꟿꟿꟿipꟿꟿꟿ, year: ꟿꟿꟿcurrentyearꟿꟿꟿ, unknown: ꟿꟿꟿmissingꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>
        {
            ["username"] = "Bob",
            ["confirmation_code"] = "ABC",
            ["ip"] = "1.2.3.4"
        };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain("Bob");
        result.Should().Contain("ABC");
        result.Should().Contain("1.2.3.4");
        result.Should().Contain(DateTime.UtcNow.Year.ToString());
        result.Should().Contain("ꟿꟿꟿmissingꟿꟿꟿ");
    }

    [Theory]
    [InlineData(NotificationType.ConfirmationRegistration, "confirmation_account.html")]
    [InlineData(NotificationType.ConfirmationOtpEmail, "confirmation_otp_email.html")]
    [InlineData(NotificationType.ConfirmationAuth, "confirmation_auth.html")]
    [InlineData(NotificationType.ResetPassword, "reset_password.html")]
    [InlineData(NotificationType.FailedLogin, "failed_login.html")]
    [InlineData(NotificationType.SuccessfulRegistration, "successful_registration.html")]
    [InlineData(NotificationType.SuccessfulLogin, "successful_login.html")]
    [InlineData(NotificationType.PasswordChanged, "password_changed.html")]
    [InlineData(NotificationType.TwoFactorMethodChanged, "two_factor_method_changed.html")]
    [InlineData(NotificationType.PasswordChangedByAdmin, "password_changed_by_admin.html")]
    public async Task Parse_AllNotificationTypesHaveTemplates(NotificationType type, string expectedFile)
    {
        WriteTemplate(expectedFile, $"<html>{type}</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var result = await ParseInTestDir(parser, type, payload);

        result.Should().Contain(type.ToString());
    }

    [Fact]
    public async Task Parse_DoesNotModifyOriginalPayload()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿcurrentyearꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();
        var originalCount = payload.Count;

        await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        payload.Should().HaveCount(originalCount);
        payload.Should().NotContainKey("currentyear");
    }

    [Fact]
    public async Task Parse_ThrowsForUnknownNotificationType()
    {
        WriteTemplate("dummy.html", "<html></html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var act = () => ParseInTestDir(parser, NotificationType.Unknown, payload);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Parse_ThrowsWhenTemplateFileNotFound()
    {
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var act = () => ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Parse_EmptyPayload_StillReplacesCurrentYear()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿcurrentyearꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>();

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        var expectedYear = DateTime.UtcNow.Year.ToString();
        result.Should().Be($"<html>{expectedYear}</html>");
    }

    [Fact]
    public async Task Parse_SpecialCharactersInValue_HtmlEncoded()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿcontentꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string> { ["content"] = "a & b < c > d \"e\" 'f'" };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain("a &amp; b &lt; c &gt; d &quot;e&quot; &#39;f&#39;");
    }

    [Fact]
    public async Task Parse_EmptyValueForPlaceholder_ReplacedWithEmptyString()
    {
        WriteDefaultTemplate("<html>Hello ꟿꟿꟿusernameꟿꟿꟿ!</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string> { ["username"] = "" };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Be("<html>Hello !</html>");
    }

    [Fact]
    public async Task Parse_NullPayload_ThrowsArgumentNullException()
    {
        WriteDefaultTemplate("<html>test</html>");
        var parser = new HtmlEmailTemplateParser();

        var act = () => ParseInTestDir(parser, NotificationType.ConfirmationRegistration, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Parse_PlaceholderAtStartAndEndOfTemplate()
    {
        WriteDefaultTemplate("ꟿꟿꟿgreetingꟿꟿꟿ middle ꟿꟿꟿfarewellꟿꟿꟿ");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string>
        {
            ["greeting"] = "Hello",
            ["farewell"] = "Goodbye"
        };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Be("Hello middle Goodbye");
    }

    [Fact]
    public async Task Parse_SamePlaceholderMultipleTimes_ReplacedEachTime()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿnameꟿꟿꟿ and ꟿꟿꟿnameꟿꟿꟿ again ꟿꟿꟿnameꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string> { ["name"] = "Alice" };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Be("<html>Alice and Alice again Alice</html>");
    }

    [Fact]
    public async Task Parse_ValueWithNewlines_PreservedInOutput()
    {
        WriteDefaultTemplate("<html>ꟿꟿꟿtextꟿꟿꟿ</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = new Dictionary<string, string> { ["text"] = "line1\nline2\r\nline3" };

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        result.Should().Contain("line1\nline2\r\nline3");
    }

    [Fact]
    public async Task Parse_LargeTemplateWithManyPlaceholders_AllReplaced()
    {
        var template = string.Join(" ", Enumerable.Range(0, 50).Select(i => $"ꟿꟿꟿkey{i}ꟿꟿꟿ"));
        WriteDefaultTemplate($"<html>{template}</html>");
        var parser = new HtmlEmailTemplateParser();
        var payload = Enumerable.Range(0, 50).ToDictionary(i => $"key{i}", i => $"val{i}");

        var result = await ParseInTestDir(parser, NotificationType.ConfirmationRegistration, payload);

        for (int i = 0; i < 50; i++)
        {
            result.Should().Contain($"val{i}");
            result.Should().NotContain($"ꟿꟿꟿkey{i}ꟿꟿꟿ");
        }
    }
}
