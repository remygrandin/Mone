namespace Mone.Messaging;

public static class MoneStreams
{
    public static class ProbeResults
    {
        public const string StreamName = "PROBE_RESULTS";
        public const string SubjectPrefix = "probes.results.>";
    }

    public static class StatusChanges
    {
        public const string StreamName = "STATUS_CHANGES";
        public const string SubjectPrefix = "status.changes.>";
    }

    public static class ProbeTriggers
    {
        public const string StreamName = "PROBE_TRIGGERS";
        public const string SubjectPrefix = "probe.trigger.>";
    }

    public static class PluginReload
    {
        public const string Subject = "mone.plugins.reload";
    }
}
