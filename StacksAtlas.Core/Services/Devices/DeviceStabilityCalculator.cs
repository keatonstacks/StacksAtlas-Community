using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Devices;

public static class DeviceStabilityCalculator
{
    /// <summary>
    /// Recalculates uptime, average latency, and the composite stability score for a device.
    /// Score = (Uptime × 70) + (Latency Consistency × 30) − Flap Penalty, clamped to [0, 100].
    /// </summary>
    public static void UpdateDeviceStability(Device device)
    {
        if (device == null) return;

        // --- 1. UPTIME PERCENTAGE ---
        int totalSeen = Math.Max(device.TotalSweepsSeen, device.ScanCount);

        // Fallback: If totalSeen is 0 but we have latency history, imply coverage
        if (totalSeen == 0 && device.LatencyHistory != null && device.LatencyHistory.Count > 0)
        {
            totalSeen = device.LatencyHistory.Count;
        }
        
        // Sync the property for consistency
        if (device.TotalSweepsSeen < totalSeen) device.TotalSweepsSeen = totalSeen;

        if (totalSeen > 0)
        {
            // Clamp to 100% to prevent race-condition inflation
            double rawUptime = ((double)device.TotalSweepsOnline / totalSeen) * 100;
            device.UptimePercent = Math.Round(Math.Min(rawUptime, 100.0), 1);
        }
        else
        {
            device.UptimePercent = 0;
        }

        // --- 2. LATENCY METRICS (single pass) ---
        double? averageLatency = null;
        if (device.LatencyHistory != null && device.LatencyHistory.Count > 0)
        {
            averageLatency = device.LatencyHistory.Average();
            device.AverageLatencyMs = Math.Round(averageLatency.Value, 1);
        }
        else
        {
            device.AverageLatencyMs = null;
        }

        // --- 3. COMPOSITE STABILITY SCORE (0-100) ---
        // Weighted: 70% Uptime, 30% Latency Consistency, minus Flap Penalty

        // Base Score from Uptime (Max 70 points)
        double uptimePoints = (device.UptimePercent / 100.0) * 70.0;

        // Flap Penalty: Logarithmic scaling so severity increases with count
        // 1 flap = 5pts, 4 flaps = 20pts, 10 flaps = 33pts, 50 flaps = 50pts (hard cap)
        double flapPenalty = 0;
        if (device.FlapCount > 0)
        {
            flapPenalty = Math.Min(Math.Log(device.FlapCount + 1) * 13, 50);
        }

        // Latency Consistency (Max 30 points)
        double latencyPoints;
        if (device.LatencyHistory != null && device.LatencyHistory.Count > 1 && averageLatency.HasValue)
        {
            // Reuse the average we already computed above
            double avg = averageLatency.Value;
            double sumOfSquares = device.LatencyHistory.Sum(val => Math.Pow(val - avg, 2));
            double stdDev = Math.Sqrt(sumOfSquares / device.LatencyHistory.Count);

            // < 5ms stddev = perfect (30 pts), > 100ms stddev = 0 pts
            latencyPoints = stdDev > 100 ? 0 : 30 * (1 - (stdDev / 100.0));
        }
        else if (device.LatencyHistory == null || device.LatencyHistory.Count == 0)
        {
            // No latency data? neutral score (15)
            latencyPoints = 15;
        }
        else
        {
            // Single data point = full latency marks (can't measure jitter yet)
            latencyPoints = 30;
        }

        double score = uptimePoints + latencyPoints - flapPenalty;
        
        // Clamp and Round
        device.StabilityScore = (int)Math.Max(0, Math.Min(100, Math.Round(score)));
    }
}
