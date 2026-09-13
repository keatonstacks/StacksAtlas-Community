using System;
using System.Collections.Generic;
using System.Text;

namespace StacksAtlas.Core.Abstractions
{
    public class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; public DateTime Now => DateTime.Now; }
}
