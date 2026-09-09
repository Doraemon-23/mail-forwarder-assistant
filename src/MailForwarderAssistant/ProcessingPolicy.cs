using System.Collections.Generic;
using System.Linq;

namespace MailForwarderAssistant
{
    public static class ProcessingPolicy
    {
        public static bool ShouldMarkRead(IEnumerable<ProcessingRecord> records)
        {
            var items = (records ?? Enumerable.Empty<ProcessingRecord>()).ToList();
            return items.Any(x => x.MarkReadRequested && !x.ReadMarked) &&
                   items.All(x => x.Status == ProcessingStatus.Success || x.Status == ProcessingStatus.SkippedRead);
        }
    }
}
