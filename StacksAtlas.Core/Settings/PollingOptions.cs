
using System;
using System.Collections.Generic;
using System.Text;

namespace StacksAtlas.Core.Settings
{
    public class PollingOptions
    {
        public int Version { get; set; } = 1;
        public int IntervalSeconds { get; set; } = 10;

        public int RefreshIntervalSeconds { get; set; } = 30;

        public int OfflineAfterSeconds { get; set; } = 300;

    }

}
