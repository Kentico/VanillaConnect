using System.Collections.Generic;
using System.Linq;
using VanillaConnect.Intercom;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace VanillaConnect.Tests
{
    public class IntercomTests
    {
        IConfiguration Configuration { get; }

        public IntercomTests()
        {
            var dict = new Dictionary<string, string>
            {
                { "Intercom:ApiUri", "https://api.intercom.io/" },
                { "Intercom:AppId", "e42kus8l" },
                { "Intercom:AccessToken", "" },
                { "Intercom:ProfileUrlPropertyName", "forums_member" },
                { "Intercom:CachingTimeoutMinutes", "30" },
                { "Intercom:PageSize", "60" },
                { "Intercom:BurstSize", "15" },
                { "Intercom:BurstDelaySeconds", "1" },
                { "Vanilla:AllowWhitespaceInUsername", "false" },
                { "Vanilla:BaseUri", "https://forums.kenticocloud.com/" },
            };

            var builder = new ConfigurationBuilder();
            builder.AddInMemoryCollection(dict);
            builder.AddEnvironmentVariables();
            builder.AddUserSecrets<Startup>();
            Configuration = builder.Build();
        }

        /// <summary>
        /// Always supply a new existing user to each run of this test or wipe out the 'forums_member' custom attribute via an API call. The tested method return collection of only updated users. If they were updated before, the method will return null and the test will fail.
        /// </summary>
        [Theory]
        [InlineData("janc@kentico.com", "https://forums.kenticocloud.com/profile/JanCerman/")]
        public async void CreatesOrUpdatesUsingEmail(string email, string profileUrl)
        {
            // Arrange
            var client = new IntercomUsersClient(Configuration, null, null, new MemoryCache(new MemoryCacheOptions()));

            // Act
            var users = await client.CreateOrUpdateWithProfileUrl(email);

            // Assert
            Assert.NotNull(users);
            Assert.True(users.All(u => u.email == email));
            Assert.True(users.All(u => (string)u.custom_attributes["forums_member"] == profileUrl));
        }

        [Theory]
        [InlineData("robert.stebel@gmail.com")]
        public async void ViewReturnsMultipleUsersForEmailAddress(string email)
        {
            // Arrange
            var user = new IntercomUser { email = email };
            //Configuration.AsEnumerable().Where(kvp => kvp.Key == "Intercom:BurstSize").FirstOrDefault() = new KeyValuePair<string, string>("Intercom:BurstSize", "15");
            var client = new IntercomUsersClient(Configuration, null, null, new MemoryCache(new MemoryCacheOptions()));

            // Act
            var users = await client.View(user);

            // Assert
            Assert.NotNull(users);
            Assert.NotEmpty(users);
            Assert.InRange(users.Count(), 2, int.MaxValue);
        }

        [Theory]
        [InlineData("janl@kentico.com")]
        [InlineData("janap@kentico.com")]
        [InlineData("martin.danko@kentico.com")]
        public async void ViewReturnsOneUserForEmailAddress(string email)
        {
            // Arrange
            var user = new IntercomUser { email = email };
            var client = new IntercomUsersClient(Configuration, null, null, new MemoryCache(new MemoryCacheOptions()));

            // Act
            var users = await client.View(user);

            // Assert
            Assert.NotNull(users);
            Assert.NotEmpty(users);
            Assert.Equal(1, users.Count());
        }

        [Fact]
        public async void PutsAllUsersToCache()
        {
            // Arrange
            var client = new IntercomUsersClient(Configuration, null, null, new MemoryCache(new MemoryCacheOptions()));
            List<IIntercomUser> cachedUsers;

            // Act
            var users = await client.GetAllUsers();
            client.MemoryCache.TryGetValue("allIntercomUsers", out cachedUsers);

            // Assert
            Assert.NotNull(users);
            Assert.NotEmpty(users);
            Assert.NotEmpty(cachedUsers);
        }
    }
}
