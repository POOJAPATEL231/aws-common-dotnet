using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Domain.Common.Entities
{
    public abstract class DocEntity : BaseEntity, IAggregateRoot
    {
        // Currently EF Core 6 will throw an error if this is a read-only property
        [JsonIgnore]
        public abstract string PartitionKey { get; set; }

        [JsonPropertyName("id")]
        public virtual string Id { get; private set; } = "";

        [JsonPropertyName("_self")]
        public string? SelfLink { get; private set; }

        [JsonPropertyName("_etag")]
        public string? ETag { get; private set; }

        // [JsonPropertyName("ttl")]
        // [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        // public int? TimeToLive { get; private set; } = default;

        #region Constructors

        protected DocEntity()
        {
        }

        #endregion

        public override dynamic GetId()
        {
            return this.Id;
        }

        public override bool IsTransient()
        {
            return this.Id == default;
        }

        public string ModelType
        {
            get
            {
                return this.GetType().ToString();
            }
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this, this.GetType(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        #region Methods

        public void SetId(string id)
        {
            Id = id;
        }

        /// <summary>
        /// Sets the concurrency stamp. Intended for use by the persistence layer,
        /// which rotates the ETag on every successful write and uses the previous
        /// value as an optimistic-concurrency condition.
        /// </summary>
        public void SetETag(string? etag)
        {
            ETag = etag;
        }

        // public void SetTTL(int ttl)
        // {
        //     TimeToLive = ttl;
        // }

        #endregion
    }
}
