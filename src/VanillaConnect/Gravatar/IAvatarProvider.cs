using System.Threading.Tasks;

namespace VanillaConnect.Gravatar
{
	public interface IAvatarProvider
	{
		Task<string> GetAvatarUrlAsync(string email, int timeOutSeconds = 5);
	}
}