using System;
using System.Collections.Generic;

namespace VanillaConnect.Intercom
{
    public interface IIntercomUser
    {
        string id { get; set; }
        string user_id { get; set; }
        string email { get; set; }
        Dictionary<String, Object> custom_attributes { get; set; }
    }
}
