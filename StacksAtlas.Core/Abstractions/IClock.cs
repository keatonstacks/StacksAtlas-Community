using System;
using System.Collections.Generic;
using System.Text;

namespace StacksAtlas.Core.Abstractions
{
    public interface IClock { DateTime UtcNow { get; } DateTime Now { get; } }
}
