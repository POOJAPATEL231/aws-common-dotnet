using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Event
{
    public interface IQueueService
    {
        /// <summary>
        /// Asynchronously sends a message to the queue.
        /// </summary>
        /// <param name="message">Message to send.</param>
        /// <returns>A task that when awaited contains the message ID of the queued message.</returns>
        Task<string> SendMessageAsync(object message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously sends multiple messages to the queue.
        /// Messages will be processed in the same order that they exist in the collection.
        /// </summary>
        /// <param name="messages">Messages to send.</param>
        /// <returns>A task that when awaited contains the session ID of the queued messages.</returns>
        Task<string> SendMessagesAsync(IEnumerable<object> messages, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously sends a message to the queue after the specified delay.
        /// </summary>
        /// <param name="message">Message to send.</param>
        /// <param name="delay">Time to wait to queue message.</param>
        /// <returns>A task that when awaited contains the message ID of the queued message.</returns>
        Task<string> ScheduleMessageAsync(object message, TimeSpan delay, CancellationToken cancellationToken = default);
    }
}
