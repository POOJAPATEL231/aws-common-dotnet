using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Domain.Common.Entities
{
    public abstract class DocEntityTtl : DocEntity
    {
        [JsonPropertyName("ttl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int? TimeToLive { get; private set; } = default;

        public void SetTTL(int ttl)
        {
            TimeToLive = ttl;
        }
    }
}
