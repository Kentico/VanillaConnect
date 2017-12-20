using System.Collections.Generic;

namespace VanillaConnect.Models
{
	public class Request
	{
		public string client { get; set; }
		public string ip { get; set; }
	}

	public class Custom
	{
	}

	public class Credential
	{
		public string @object { get; set; }
		public string id { get; set; }
		public string credential_type { get; set; }
	}

	public class User
	{
		public string @object { get; set; }
		public string id { get; set; }
		public string realm_id { get; set; }
		public string username { get; set; }
		public string state { get; set; }
		public string user_type { get; set; }
		public string reference { get; set; }
		public string name { get; set; }
		public string email { get; set; }
		public string email_verification { get; set; }
		public int last_login_at { get; set; }
		public int last_login_on { get; set; }
		public double created_at { get; set; }
		public string first_name { get; set; }
		public string last_name { get; set; }
		public Custom custom { get; set; }
		public List<Credential> credentials { get; set; }
		public int membership_count { get; set; }
	}

	public class Session
	{
		public string @object { get; set; }
		public string id { get; set; }
		public Request request { get; set; }
		public string user_id { get; set; }
		public double created_at { get; set; }
		public int expires_at { get; set; }
		public User user { get; set; }
	}
}
