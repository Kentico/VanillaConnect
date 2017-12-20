using System;
using System.Collections.Generic;

namespace VanillaConnect.Intercom
{
    public class IntercomUser : IIntercomUser
    {
        public string id { get; set; }
        public string user_id { get; set; }
        public string email { get; set; }
        public Dictionary<String, Object> custom_attributes { get; set; }
    }
}
