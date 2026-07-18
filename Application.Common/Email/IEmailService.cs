namespace Application.Common.Email
{
    /// <summary>An email to send. Bodies may be plain text, HTML, or both.</summary>
    public class EmailMessage
    {
        public string From { get; set; } = string.Empty;
        public List<string> To { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string? TextBody { get; set; }
        public string? HtmlBody { get; set; }
        public string? ReplyTo { get; set; }
    }

    /// <summary>
    /// Email sending abstraction (implemented for AWS SES by
    /// Infrastructure.Common.AWS.Email.SesEmailService).
    /// </summary>
    public interface IEmailService
    {
        /// <summary>Sends a single email; returns the provider message id.</summary>
        Task<string> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

        /// <summary>Sends an email based on a provider-side template; returns the provider message id.</summary>
        Task<string> SendTemplatedAsync(string from, IEnumerable<string> to, string templateName,
            IDictionary<string, string> templateData, CancellationToken cancellationToken = default);
    }
}
