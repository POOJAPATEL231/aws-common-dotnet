using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Application.Common.Email;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Email
{
    /// <summary>
    /// <see cref="IEmailService"/> implementation on Amazon SES v2.
    /// </summary>
    public class SesEmailService : IEmailService
    {
        private readonly IAmazonSimpleEmailServiceV2 _sesClient;

        public SesEmailService(IAmazonSimpleEmailServiceV2 sesClient)
        {
            _sesClient = sesClient;
        }

        public async Task<string> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (message.To.Count == 0)
            {
                throw new ArgumentException("At least one recipient is required.", nameof(message));
            }

            var body = new Body();
            if (!string.IsNullOrEmpty(message.HtmlBody))
            {
                body.Html = new Content { Data = message.HtmlBody };
            }
            if (!string.IsNullOrEmpty(message.TextBody))
            {
                body.Text = new Content { Data = message.TextBody };
            }

            var request = new SendEmailRequest
            {
                FromEmailAddress = message.From,
                Destination = new Destination
                {
                    ToAddresses = message.To,
                    CcAddresses = message.Cc,
                    BccAddresses = message.Bcc
                },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = message.Subject },
                        Body = body
                    }
                }
            };

            if (!string.IsNullOrEmpty(message.ReplyTo))
            {
                request.ReplyToAddresses = new List<string> { message.ReplyTo };
            }

            var response = await _sesClient.SendEmailAsync(request, cancellationToken);
            return response.MessageId;
        }

        public async Task<string> SendTemplatedAsync(string from, IEnumerable<string> to, string templateName,
            IDictionary<string, string> templateData, CancellationToken cancellationToken = default)
        {
            var request = new SendEmailRequest
            {
                FromEmailAddress = from,
                Destination = new Destination { ToAddresses = to.ToList() },
                Content = new EmailContent
                {
                    Template = new Template
                    {
                        TemplateName = templateName,
                        TemplateData = JsonSerializer.Serialize(templateData)
                    }
                }
            };

            var response = await _sesClient.SendEmailAsync(request, cancellationToken);
            return response.MessageId;
        }
    }
}
