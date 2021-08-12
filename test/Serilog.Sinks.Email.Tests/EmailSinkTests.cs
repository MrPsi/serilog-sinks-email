﻿using Moq;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Serilog.Sinks.Email.Tests
{
    public class EmailSinkTests
    {
        [Fact(Skip = "Requires a localhost mail server")]
        public void Works()
        {
            var selfLogMessages = new List<string>();
            SelfLog.Enable(selfLogMessages.Add);

            using (var emailLogger = new LoggerConfiguration()
                .WriteTo.Email(
                    fromEmail: "from@localhost.local",
                    toEmail: "to@localhost.local",
                    mailServer: "localhost",
                    outputTemplate: "[{Level}] {Message}{NewLine}{Exception}",
                    mailSubject: "subject")
                .CreateLogger())
            {
                emailLogger.Information("test {test}", "test");
            }

            Assert.Equal(Enumerable.Empty<string>(), selfLogMessages);
        }

        [Fact(Skip = "Requires a smtp mail server")]
        public void WorksMulitpleEventsInOneMail()
        {
            var selfLogMessages = new List<string>();
            SelfLog.Enable(selfLogMessages.Add);

            using (var emailLogger = new LoggerConfiguration()
                .WriteTo.Email(
                    fromEmail: "from@smtpserver.local",
                    toEmail: "to@smtpserver.local",
                    mailServer: "smtpserver.local",
                    outputTemplate: "[{Level}] {Message}{NewLine}{Exception}",
                    mailSubject: "test subject")
                .CreateLogger())
            {
                emailLogger.Information("first test {test}", "test1");
                emailLogger.Error("second {test}", "test2");
                emailLogger.Fatal("third {test}", "test3");
            }

            Assert.Equal(Enumerable.Empty<string>(), selfLogMessages);
        }

        [Fact]
        public void EmailTransportIsCreatedWhenEmailSinkIsConstructed()
        {
            var (emailConnectionInfoMock, emailTransportMock) = CreateEmailTransportConnectionInfoMocks();
            CreateDefaultEmailSink(emailConnectionInfoMock.Object);

            emailConnectionInfoMock.Verify(eci => eci.CreateEmailTransport(), Times.Once);
            emailTransportMock.Verify(eci => eci.Dispose(), Times.Never);
        }

        [Fact]
        public void EmailTransportIsDisposedWhenEmailSinkIsDisposed()
        {
            var (emailConnectionInfoMock, emailTransportMock) = CreateEmailTransportConnectionInfoMocks();
            var emailSink = CreateDefaultEmailSink(emailConnectionInfoMock.Object);

            emailSink.Dispose();

            emailTransportMock.Verify(eci => eci.Dispose(), Times.Once);
        }

        [Fact]
        [UseCulture("en-us")]
        public async Task SendEmailIsCorrectlyCalledWhenEventAreLogged()
        {
            EmailMessage actual = null;
            var (emailConnectionInfoMock, emailTransportMock) =
                CreateEmailTransportConnectionInfoMocks(email => actual = email);
            var emailConnectionInfo = emailConnectionInfoMock.Object;
            emailConnectionInfo.ToEmail = "to@localhost.local";
            emailConnectionInfo.FromEmail = "from@localhost.local";
            emailConnectionInfo.IsBodyHtml = true;
            var emailSink = CreateDefaultEmailSink(emailConnectionInfoMock.Object);

            await emailSink.EmitBatchAsync(new[] {
                new LogEvent(
                    DateTimeOffset.Now,
                    LogEventLevel.Error,
                    new ArgumentOutOfRangeException("parameter1", "Message of the exception"),
                    new MessageTemplate(@"Subject",
                        new MessageTemplateToken[]
                        {
                            new PropertyToken("Message", "A multiline" + Environment.NewLine
                                                                       + "Message")
                        })
                    , Enumerable.Empty<LogEventProperty>())});
            emailSink.Dispose();

            emailTransportMock.Verify(et => et.SendMailAsync(It.IsAny<EmailMessage>()), Times.Once);

            Assert.Equal("[Error] A multiline" + Environment.NewLine
                        + "Message" + Environment.NewLine
                        + "System.ArgumentOutOfRangeException: Message of the exception"
#if NEW_ARGUMENTOUTOFRANGEEXCEPTION_MESSAGE
                        + " (Parameter 'parameter1')"
#else
                        + Environment.NewLine + "Parameter name: parameter1"
#endif
                        + Environment.NewLine + "", actual.Body);
            Assert.Equal(@"[Error] A multiline" + Environment.NewLine
                        + "Message" + Environment.NewLine
                        + "System.ArgumentOutOfRangeException: Message of the exception"
#if NEW_ARGUMENTOUTOFRANGEEXCEPTION_MESSAGE
                        + " (Parameter 'parameter1')"
#else
                        + Environment.NewLine + "Parameter name: parameter1"
#endif
                        + Environment.NewLine + "", actual.Subject);
            Assert.Equal("from@localhost.local", actual.From);
            Assert.Equal(new[] { "to@localhost.local" }, actual.To);
            Assert.True(actual.IsBodyHtml);
        }

        [Fact]
        public void MultilineMessageCreatesSubjectWithTheFirstLineOnly()
        {
            var subjectLineFormatter = new MessageTemplateTextFormatter("{Message}", null);

            var logEvents = new[]
            {
                new LogEvent(DateTimeOffset.Now, LogEventLevel.Error, new Exception("An exception occured"),
                    new MessageTemplate(@"Subject",
                        new MessageTemplateToken[]{new PropertyToken("Message", "A multiline" + Environment.NewLine + "Message")})
                    , Enumerable.Empty<LogEventProperty>())
            };
            var mailSubject = EmailSink.ComputeMailSubject(subjectLineFormatter, logEvents);

            Assert.Equal("A multiline", mailSubject);
        }

        private EmailSink CreateDefaultEmailSink(EmailConnectionInfo emailConnectionInfo)
        {
            var formatter = new MessageTemplateTextFormatter("[{Level}] {Message}{NewLine}{Exception}", null);
            var subjectLineFormatter = new MessageTemplateTextFormatter("[{Level}] {Message}{NewLine}{Exception}", null);

            var emailSink = new EmailSink(
                emailConnectionInfo,
                formatter,
                subjectLineFormatter);
            return emailSink;
        }

        private (Mock<EmailConnectionInfo> EmailConnectionInfoMock, Mock<IEmailTransport> EmailTransportMock)
            CreateEmailTransportConnectionInfoMocks()
        {
            return CreateEmailTransportConnectionInfoMocks(_ => { });
        }

        private (Mock<EmailConnectionInfo> EmailConnectionInfoMock, Mock<IEmailTransport> EmailTransportMock)
            CreateEmailTransportConnectionInfoMocks(Action<EmailMessage> emailSend)
        {
            var emailTransportMock = new Mock<IEmailTransport>();
            emailTransportMock.Setup(et => et.SendMailAsync(It.IsAny<EmailMessage>()))
                .Callback<EmailMessage>(email => emailSend(email))
                .Returns(Task.Factory.StartNew(() => { }));
            var emailTransport = emailTransportMock.Object;
            var emailConnectionInfoMock = new Mock<EmailConnectionInfo>();
            emailConnectionInfoMock
                .Setup(eci => eci.CreateEmailTransport())
                .Returns(emailTransport);
            return (emailConnectionInfoMock, emailTransportMock);
        }
    }
}
