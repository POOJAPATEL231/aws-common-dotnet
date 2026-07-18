using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.Cache
{
    public record GetRedisCacheOptions : RedisCacheOptions
    {
        public GetRedisCacheOptions(IOptionsMonitor<InfraSettings> settingsAccessor)
            : base(settingsAccessor.CurrentValue.RedisCacheSettings)
        {
        }
    }
}
