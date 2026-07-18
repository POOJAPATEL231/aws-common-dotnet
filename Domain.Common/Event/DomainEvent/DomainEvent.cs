using MediatR;

namespace Domain.Common.Event
{
    public abstract record DomainEvent
         : INotification
    {
        /// <summary>
        /// Id of User who triggered the event
        /// </summary>
        public dynamic? EventUserId { get; private set; }

        /// <summary>
        /// Name of user who triggered the event
        /// </summary>
        public string? EventUserName { get; private set; }

        /// <summary>
        /// Id of Org who event is related to (Org user is currently logged in as)
        /// </summary>
        public dynamic? EventOrgId { get; private set; }

        /// <summary>
        /// Client Ip address of user who triggered the event
        /// </summary>
        public string? EventSource { get; private set; }

        protected DomainEvent() { }

        public void SetOrg(dynamic? eventOrgId)
        {
            EventOrgId = eventOrgId;
        }

        public void SetUserSource(dynamic? eventUserid, string? eventUserName, string? eventSource = default)
        {
            if (eventUserid != null)
            {
                EventUserId = eventUserid;
            }

            if (eventUserName != null)
            {
                EventUserName = eventUserName;
            }

            if (eventSource != null)
            {
                EventSource = eventSource;
            }
        }
    }
}
