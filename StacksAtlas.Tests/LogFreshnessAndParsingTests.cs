using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Tests
{
    public class LogFreshnessAndParsingTests
    {
        // Copy the regex from SystemController to verify its exact behavior
        private static readonly Regex LogLineRegex = new(
            @"^\[?(\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?: ?[+-]\d{2}:?\d{2}| ?Z)?)\]?(.*)",
            RegexOptions.Compiled);

        private string ParseLogLine(string line)
        {
            var match = LogLineRegex.Match(line);
            if (match.Success)
            {
                var timestampStr = match.Groups[1].Value.Trim();
                var restOfLine = match.Groups[2].Value;
                if (DateTimeOffset.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto))
                {
                    return $"[{dto.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} +00:00]{restOfLine}";
                }
            }
            return line;
        }

        [Theory]
        [InlineData("2026-05-23 13:28:55.123 -04:00 [INF] Network settings saved successfully.", "[2026-05-23 17:28:55.123 +00:00] [INF] Network settings saved successfully.")]
        [InlineData("[2026-05-23 13:28:55.123 -04:00] [INF] Network settings saved successfully.", "[2026-05-23 17:28:55.123 +00:00] [INF] Network settings saved successfully.")]
        [InlineData("2026-05-23T13:28:55.1234567-04:00 [INF] Re-scheduling scan.", "[2026-05-23 17:28:55.123 +00:00] [INF] Re-scheduling scan.")]
        [InlineData("2026-05-23 17:28:55.123 Z [INF] Sweeping scope.", "[2026-05-23 17:28:55.123 +00:00] [INF] Sweeping scope.")]
        [InlineData("[2026-05-23 17:28:55.123 +00:00] [INF] Rescheduling.", "[2026-05-23 17:28:55.123 +00:00] [INF] Rescheduling.")]
        public void ParseLogLine_ShouldConvertTimestampsToUtcCorrectly(string inputLine, string expectedLine)
        {
            var parsed = ParseLogLine(inputLine);
            Assert.Equal(expectedLine, parsed);
        }

        [Fact]
        public void ParseLogLine_ShouldLeaveNonTimestampLinesUntouched()
        {
            var stackTraceLine = "   at System.Threading.Tasks.Task.Execute()";
            var parsed = ParseLogLine(stackTraceLine);
            Assert.Equal(stackTraceLine, parsed);

            var infoLine = "Some arbitrary text without timestamp";
            var parsedInfo = ParseLogLine(infoLine);
            Assert.Equal(infoLine, parsedInfo);
        }

        [Fact]
        public void PendingLog_ShouldCorrectlyStoreProperties()
        {
            var id = Guid.NewGuid();
            var timestamp = DateTime.UtcNow;
            var created = DateTime.UtcNow;

            var log = new PendingLog
            {
                Id = id,
                LogLevel = "Error",
                Message = "Failed to connect to Hub",
                Exception = "SocketException: Connection refused",
                Timestamp = timestamp,
                CreatedAtUtc = created
            };

            Assert.Equal(id, log.Id);
            Assert.Equal("Error", log.LogLevel);
            Assert.Equal("Failed to connect to Hub", log.Message);
            Assert.Equal("SocketException: Connection refused", log.Exception);
            Assert.Equal(timestamp, log.Timestamp);
            Assert.Equal(created, log.CreatedAtUtc);
        }
    }
}
