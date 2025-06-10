using System;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest
{
    public class SSHOMCache
    {
        public CacheMetadata Metadata { get; set; }
        public List<SSHOMEntry> Sshoms { get; set; }
    }

    public class CacheMetadata
    {
        public string StrykerVersion { get; set; }
        public string CodeHash { get; set; }
        public string TestHash { get; set; }
        public DateTime DiscoveredAt { get; set; }
    }

    public class SSHOMEntry
    {
        public string Id { get; set; }
        public List<string> Foms { get; set; }
        public List<string> KilledTests { get; set; }
        public List<string> SubsumedFoms { get; set; }
    }
}
