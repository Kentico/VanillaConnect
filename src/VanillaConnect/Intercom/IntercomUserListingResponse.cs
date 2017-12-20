using System.Collections.Generic;

namespace VanillaConnect.Intercom
{
    public class IntercomUserListingResponse
    {
        public string type { get; set; }
        public PagingSection pages { get; set; }
        public IEnumerable<IntercomUser> users { get; set; }
        public int total_count { get; set; }
    }

    public class PagingSection
    {
        public string type { get; set; }
        public string next { get; set; }
        public int page { get; set; }
        public int per_page { get; set; }
        public int total_pages { get; set; }
    }
}
