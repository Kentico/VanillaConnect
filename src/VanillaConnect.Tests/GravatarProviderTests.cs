using VanillaConnect.Gravatar;
using Xunit;

namespace VanillaConnect.Tests
{
	public class GravatarProviderTests
	{
	    [Theory]
		[InlineData("petrs@kentico.com")]
		[InlineData("PETRS@kentico.com")]
		[InlineData("  PETRS@kentico.com  ")]
		public async void GetExistingAvatarUrl(string email)
		{
			// Arrange
			var provider = new GravatarProvider(null);

			// Act
			var avatarUrl = await provider.GetAvatarUrlAsync(email);

			// Assert
			Assert.Equal("https://secure.gravatar.com/avatar/9aaab6c31e261e904a89030c5c85e4c2", avatarUrl);
		}
	}
}
