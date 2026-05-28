using BarkFluff.Notification.Consumers;
using BarkFluff.Notification.Parsers;
using BarkFluff.Notification.Senders;
using BarkFluff.Shared.Queue.Notifications;

using MassTransit;

namespace BarkFluff.Notification.Tests.Consumers;

public class EmailQueueConsumerTests
{
    private readonly TestHelper _helper = new();

    private class StubEmailSender : EmailSender
    {
        private readonly Func<EmailNotification, Task> _action;

        public StubEmailSender(Func<EmailNotification, Task> action)
            : base(
                new Configurations.EmailConfiguration { Host = "h", Port = 1, SenderEmail = "a@a", SenderPassword = "p" },
                Mock.Of<HtmlEmailTemplateParser>(),
                Mock.Of<ILogger<EmailSender>>())
        {
            _action = action;
        }

        public override Task SendEmail(EmailNotification notification) => _action(notification);
    }

    private EmailSender CreateSuccessSender()
    {
        return new StubEmailSender(_ => Task.CompletedTask);
    }

    private EmailSender CreateFailingSender(Exception ex)
    {
        return new StubEmailSender(_ => throw ex);
    }

    private EmailSender CreateSenderWithTracker(List<EmailNotification> sent)
    {
        return new StubEmailSender(n => { sent.Add(n); return Task.CompletedTask; });
    }

    [Fact]
    public async Task Consume_ValidNotification_IncrementsConsumedMetric()
    {
        var consumer = _helper.CreateConsumer(CreateSuccessSender());
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        await consumer.Consume(context.Object);

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("rabbitmq_events_consumed");
        snapshot["rabbitmq_events_consumed"].Should().Be(1);
    }

    [Fact]
    public async Task Consume_ValidNotification_CallsEmailSender()
    {
        var sent = new List<EmailNotification>();
        var consumer = _helper.CreateConsumer(CreateSenderWithTracker(sent));
        var notification = TestHelper.CreateEmailNotification(
            address: "test@test.com",
            title: "Welcome",
            type: NotificationType.ConfirmationRegistration);
        var context = TestHelper.CreateConsumeContext(notification);

        await consumer.Consume(context.Object);

        sent.Should().HaveCount(1);
        sent[0].Address.Should().Be("test@test.com");
        sent[0].Title.Should().Be("Welcome");
        sent[0].Type.Should().Be(NotificationType.ConfirmationRegistration);
    }

    [Fact]
    public async Task Consume_SuccessfulSend_IncrementsSentMetric()
    {
        var consumer = _helper.CreateConsumer(CreateSuccessSender());
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        await consumer.Consume(context.Object);

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("emails_sent");
        snapshot["emails_sent"].Should().Be(1);
    }

    [Fact]
    public async Task Consume_SuccessfulSend_DoesNotIncrementFailedMetric()
    {
        var consumer = _helper.CreateConsumer(CreateSuccessSender());
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        await consumer.Consume(context.Object);

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot.Should().NotContainKey("emails_failed");
    }

    [Fact]
    public async Task Consume_FailedSend_IncrementsFailedMetric()
    {
        var consumer = _helper.CreateConsumer(CreateFailingSender(new Exception("SMTP failure")));
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        await Assert.ThrowsAsync<Exception>(() => consumer.Consume(context.Object));

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("emails_failed");
        snapshot["emails_failed"].Should().Be(1);
    }

    [Fact]
    public async Task Consume_FailedSend_DoesNotIncrementSentMetric()
    {
        var consumer = _helper.CreateConsumer(CreateFailingSender(new Exception("SMTP failure")));
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        await Assert.ThrowsAsync<Exception>(() => consumer.Consume(context.Object));

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot.Should().NotContainKey("emails_sent");
    }

    [Fact]
    public async Task Consume_FailedSend_RethrowsException()
    {
        var expectedException = new Exception("SMTP down");
        var consumer = _helper.CreateConsumer(CreateFailingSender(expectedException));
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        var ex = await Assert.ThrowsAsync<Exception>(() => consumer.Consume(context.Object));
        ex.Should().BeSameAs(expectedException);
    }

    [Fact]
    public async Task Consume_AlwaysIncrementsConsumedMetric_EvenOnFailure()
    {
        var consumer = _helper.CreateConsumer(CreateFailingSender(new Exception("Fail")));
        var notification = TestHelper.CreateEmailNotification();
        var context = TestHelper.CreateConsumeContext(notification);

        await Assert.ThrowsAsync<Exception>(() => consumer.Consume(context.Object));

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("rabbitmq_events_consumed");
        snapshot["rabbitmq_events_consumed"].Should().Be(1);
    }

    [Fact]
    public async Task Consume_MultipleNotifications_IncrementsMetricsCorrectly()
    {
        var consumer = _helper.CreateConsumer(CreateSuccessSender());

        for (int i = 0; i < 5; i++)
        {
            var notification = TestHelper.CreateEmailNotification(address: $"user{i}@test.com");
            var context = TestHelper.CreateConsumeContext(notification);
            await consumer.Consume(context.Object);
        }

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot["rabbitmq_events_consumed"].Should().Be(5);
        snapshot["emails_sent"].Should().Be(5);
    }

    [Fact]
    public async Task Consume_MixedSuccessAndFailure_TracksBothMetrics()
    {
        var callCount = 0;
        var sender = new StubEmailSender(_ =>
        {
            callCount++;
            if (callCount % 2 == 0) throw new Exception("Even call fails");
            return Task.CompletedTask;
        });
        var consumer = _helper.CreateConsumer(sender);

        for (int i = 0; i < 4; i++)
        {
            var notification = TestHelper.CreateEmailNotification(address: $"user{i}@test.com");
            var context = TestHelper.CreateConsumeContext(notification);
            try { await consumer.Consume(context.Object); } catch { }
        }

        var snapshot = _helper.Metrics.SnapshotAndReset();
        snapshot["rabbitmq_events_consumed"].Should().Be(4);
        snapshot["emails_sent"].Should().Be(2);
        snapshot["emails_failed"].Should().Be(2);
    }

    [Fact]
    public async Task Consume_PassesFullNotificationToSender()
    {
        var sent = new List<EmailNotification>();
        var consumer = _helper.CreateConsumer(CreateSenderWithTracker(sent));
        var notification = TestHelper.CreateEmailNotification(
            address: "full@test.com",
            title: "Test Title",
            type: NotificationType.ResetPassword,
            payload: new Dictionary<string, string> { ["key"] = "value" });
        notification.OwnerId = 42;
        var context = TestHelper.CreateConsumeContext(notification);

        await consumer.Consume(context.Object);

        sent.Should().HaveCount(1);
        var received = sent[0];
        received.Address.Should().Be("full@test.com");
        received.Title.Should().Be("Test Title");
        received.Type.Should().Be(NotificationType.ResetPassword);
        received.OwnerId.Should().Be(42);
        received.Payload.Should().ContainKey("key");
    }
}
