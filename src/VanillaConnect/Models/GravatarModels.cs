using System.Collections.Generic;

namespace VanillaConnect.Models
{
	public class Photo
	{
		public string value { get; set; }
		public string type { get; set; }
	}

	public class Entry
	{
		public string id { get; set; }
		public string hash { get; set; }
		public string requestHash { get; set; }
		public string profileUrl { get; set; }
		public string preferredUsername { get; set; }
		public string thumbnailUrl { get; set; }
		public List<Photo> photos { get; set; }
		public string displayName { get; set; }
		public List<object> urls { get; set; }
	}

	public class Profile
	{
		public List<Entry> entry { get; set; }
	}
}
