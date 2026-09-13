namespace StacksAtlas.Core.Settings
{
    public class CleanupOptions
    {
        public int Version { get; set; } = 1;
        public bool Enabled { get; set; } = true;

        // How often the CleanupWorker wakes up to check the DB
        public int RunEveryMinutes { get; set; } = 5;

        // --- RETENTION LOGIC ---

        // Active devices not seen for this long get moved to Archive (IsDeleted = true, ArchivedUtc stamped)
        public int AutoArchiveDays { get; set; } = 30;

        // Archived devices get hard-deleted only after this long measured from ArchivedUtc (not LastSeen).
        // Use 0 or -1 if you never want to permanently delete anything.
        public int PermanentDeletionDays { get; set; } = 90;

        // Controls how long we keep the 'SweepHistory' entries (pings/results)
        public int SweepHistoryRetentionHours { get; set; } = 24;

        // --- PHYSICAL DB MAINTENANCE ---
        public bool CompactWeekly { get; set; } = false;
        public string CompactDay { get; set; } = "Sunday";
        public int CompactHourUtc { get; set; } = 3;

        // Brought over from DatabaseOptions for consolidation:
        public long MaxSizeMb { get; set; } = 512;
        public bool AutoVacuum { get; set; } = true;
    }
}
