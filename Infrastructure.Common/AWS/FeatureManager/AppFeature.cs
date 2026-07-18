using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.FeatureManager
{
    public record AppFeature(string Name, bool IsEnabled);

}
