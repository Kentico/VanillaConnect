using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using jsConnectNetCore;

namespace VanillaConnect.Intercom
{
    public class IntercomUsersClient : IIntercomUsersClient
    {
        #region "Constants"

        protected const string USERS_ENDPOINT_URI = "users";

        #endregion

        #region "Properties"

        protected string IntercomUri => Configuration.GetValue("Intercom:ApiUri", "https://api.intercom.io/");
        protected string IntercomAppId => Configuration.GetValue("Intercom:AppId", string.Empty);
        protected string IntercomAccessToken => Configuration.GetValue("Intercom:AccessToken", string.Empty);
        protected string IntercomProfileUrlPropertyName => Configuration.GetValue("Intercom:ProfileUrlPropertyName", "forums_member");
        protected double IntercomCachingTimeout => Configuration.GetValue<double>("Intercom:CachingTimeoutMinutes", 30);
        protected int IntercomPageSize => Configuration.GetValue<int>("Intercom:PageSize");
        protected int IntercomBurstSize => Configuration.GetValue("Intercom:BurstSize", 50);
        protected int IntercomBurstDelaySeconds => Configuration.GetValue("Intercom:BurstDelaySeconds", 10);
        protected string BaseUri => Configuration.GetValue("Vanilla:BaseUri", string.Empty);
        protected IConfiguration Configuration { get; set; }
        protected ILogger<IntercomUsersClient> Logger { get; set; }
        protected ILoggerFactory LoggerFactory { get; set; }
        internal IMemoryCache MemoryCache { get; set; }

        #endregion

        #region "Constructors"

        public IntercomUsersClient(IConfiguration configuration, ILogger<IntercomUsersClient> logger, ILoggerFactory loggerFactory, IMemoryCache memoryCache)
        {
            Configuration = configuration;
            Logger = logger;
            LoggerFactory = loggerFactory;
            MemoryCache = memoryCache;
        }

        #endregion

        #region "Public methods"

        /// <summary>
        /// Updates the Intercom user with the claims data.
        /// </summary>
        /// <param name="email">E-mail of the user</param>
        /// <returns>Collection of matching updated users, otherwise <see langword="null" />.</returns>
        public async Task<IEnumerable<IIntercomUser>> CreateOrUpdateWithProfileUrl(string email)
        {
            if (!string.IsNullOrEmpty(email))
            {
                IIntercomUser intercomUser = new IntercomUser
                {
                    email = email,
                    custom_attributes = new Dictionary<string, object>()
                };

                string profileUrl = await GetProfileUrl(email);
                var users = await View(intercomUser);

                if (profileUrl != null)
                {
                    AddProfileAttributeToUser(intercomUser, profileUrl); 
                }
                else
                {
                    Logger.LogWarning($"Couldn't get Vanilla user profile URL for user with email address {email}.");
                }

                var output = new List<IIntercomUser>();

                foreach (var user in users)
                {
                    if (!user.custom_attributes.ContainsKey(IntercomProfileUrlPropertyName) || (string)user.custom_attributes[IntercomProfileUrlPropertyName] != profileUrl)
                    {
                        intercomUser.id = user.id;
                        output.Add(await CreateOrUpdate(intercomUser));
                    }
                }

                return output;
            }
            else
            {
                throw new ArgumentException("The 'email' property cannot be null or an empty string.", nameof(email));
            }
        }

        /// <summary>
        /// Gets <see cref="IntercomUser"/> objects by either the 'email', 'user_id' or 'id' property of the <paramref name="user"/> object.
        /// </summary>
        /// <param name="user">Dummy user used for querying</param>
        /// <returns>A collection of one or more matching users, otherwise <see langword="null"/>.</returns>
        /// <remarks>Unlike the official Intercom REST API and their .NET SDK, this method is capable of returning a collection of <see cref="IIntercomUser"/> objects if just the 'email' property was specified and there are more users with such address in the Intercom database.</remarks>
        public async Task<IEnumerable<IIntercomUser>> View(IIntercomUser user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            string requestUri = $"{IntercomUri}/{USERS_ENDPOINT_URI}";

            if (!String.IsNullOrEmpty(user.id))
            {
                requestUri += $"/{user.id}";
            }
            else if (!String.IsNullOrEmpty(user.user_id))
            {
                requestUri += $"?user_id={user.user_id}";
            }
            else if (!String.IsNullOrEmpty(user.email))
            {
                requestUri += $"?email={WebUtility.UrlEncode(user.email)}";
            }
            else
            {
                throw new ArgumentException("You need to provide either 'user.id', 'user.user_id', or 'user.email' to view a user.");
            }

            var results = await GetUsers(requestUri, user);

            return results;
        }

        /// <summary>
        /// Gets a <see cref="List{IIntercomUser}"/> of all users off of the Intercom endpoint using the Scroll API.
        /// </summary>
        /// <returns>List of all users</returns>
        /// <remarks>The users are being cached for the duration of 30 minutes by default. Otherwise for the duration specified in the 'Intercom:CachingTimeout' app setting.</remarks>
        public async Task<IEnumerable<IIntercomUser>> GetAllUsers()
        {
            List<IIntercomUser> allIntercomUsers;

            if (!MemoryCache.TryGetValue(nameof(allIntercomUsers), out allIntercomUsers))
            {
                using (var httpClient = BuildClient())
                {
                    // First, we get the initial page to see what is the total count of pages to iterate over.
                    var initialListing = await GetUserListing($"{IntercomUri}{USERS_ENDPOINT_URI}", httpClient);
                    allIntercomUsers = new List<IIntercomUser>();

                    if (initialListing != null)
                    {
                        allIntercomUsers.AddRange(initialListing.users);
                    }

                    int pageCount = (int)initialListing?.pages?.total_pages;

                    // Should there be more pages, fetch them asynchronously using Task.WhenAll() in bursts repeated every few seconds (to avoid request throttling).
                    if (pageCount > 1)
                    {
                        var currentSpan = new Tuple<int, int>(0, 0);

                        if (pageCount > IntercomBurstSize)
                        {
                            int burstCount = pageCount / IntercomBurstSize;

                            if (pageCount % IntercomBurstSize > 0)
                            {
                                burstCount++;
                            }

                            int currentBurst = 1;
                            int spanLowerBoundary = 2;
                            int spanHigherBoundary = IntercomBurstSize;
                            int delayMilliseconds = IntercomBurstDelaySeconds * 1000;

                            while (currentBurst <= burstCount)
                            {
                                if (currentBurst > 1)
                                {
                                    spanLowerBoundary = (currentBurst - 1) * IntercomBurstSize + 1;
                                    spanHigherBoundary = currentBurst * IntercomBurstSize;
                                    
                                    if (currentBurst == burstCount)
                                    {
                                        spanHigherBoundary = pageCount;
                                    } 
                                }

                                currentSpan = new Tuple<int, int>(spanLowerBoundary, spanHigherBoundary);
                                allIntercomUsers = await AddUsersBurst(allIntercomUsers, currentSpan, httpClient);
                                currentBurst++;

                                // Pausing execution in an async-friendly manner.
                                await Task.Delay(delayMilliseconds);
                            }
                        }
                        else
                        {
                            currentSpan = new Tuple<int, int>(2, pageCount);
                            allIntercomUsers = await AddUsersBurst(allIntercomUsers, currentSpan, httpClient);
                        }
                    }

                    var options = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(IntercomCachingTimeout)
                    };

                    MemoryCache.Set(nameof(allIntercomUsers), allIntercomUsers, options);
                }
            }

            return allIntercomUsers;
        }

        /// <summary>
        /// Creates a new or updates an existing <see cref="IIntercomUser"/> object via the Intercom API. 
        /// </summary>
        /// <param name="user">The dummy user object to create or update</param>
        /// <returns>The updated matching user, otherwise <see langword="null"/>.</returns>
        public async Task<IIntercomUser> CreateOrUpdate(IIntercomUser user)
        {
            if (user.custom_attributes != null && user.custom_attributes.Any())
            {
                if (user.custom_attributes.Count > 100)
                    throw new ArgumentException("Maximum of 100 fields.");

                foreach (var attr in user.custom_attributes)
                {
                    if (attr.Key.Contains(".") || attr.Key.Contains("$"))
                        throw new ArgumentException(String.Format("Field names must not contain Periods (.) or Dollar ($) characters. key: {0}", attr.Key));

                    if (attr.Key.Length > 190)
                        throw new ArgumentException(String.Format("Field names must be no longer than 190 characters. key: {0}", attr.Key));

                    if (attr.Value == null)
                        throw new ArgumentException(String.Format("'value' is null. key: {0}", attr.Key));
                }
            }

            return await Post(user);
        }

        #endregion

        #region "Private methods"

        private void AddProfileAttributeToUser(IIntercomUser user, string url)
        {
            if (user.custom_attributes == null)
            {
                user.custom_attributes = new Dictionary<string, object>();
            }

            user.custom_attributes.Add(IntercomProfileUrlPropertyName, url);
        }

        private async Task<string> GetProfileUrl(string email)
        {
            var logger = LoggerFactory?.CreateLogger<VanillaApiClient>();
            string slug;

            using (var vanillaClient = new VanillaApiClient(BaseUri, logger))
            {
                var user = await vanillaClient.GetUser(email: email);
                slug = user?.Profile?.Name;
            }

            return (slug != null) ? $"{BaseUri}profile/{slug}/" : null;
        }

        /// <summary>
        /// Gets a collection of users that match to a <paramref name="requestUri"/>.
        /// </summary>
        /// <param name="requestUri">Request URI</param>
        /// <param name="user">User to match by the <see cref="IIntercomUser.email"/>  if the full collection of all users needs to be iterated through.</param>
        /// <returns>One or more matching users, otherwise <see langword="null"/>.</returns>
        private async Task<IEnumerable<IIntercomUser>> GetUsers(string requestUri, IIntercomUser user)
        {
            HttpResponseMessage response;

            using (var httpClient = BuildClient())
            {
                response = await HttpGetAsync(httpClient, requestUri);
            }

            if (response?.StatusCode == HttpStatusCode.OK)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                var userList = new List<IIntercomUser>();
                userList.Add(JsonConvert.DeserializeObject<IntercomUser>(responseContent));

                return userList;
            }
            // If multiple Intercom users have the same e-mail address, the endpoint will return BadRequest. Thus we need to fetch all users and filter by the address on our side.
            else if (response?.StatusCode == HttpStatusCode.BadRequest)
            {
                var allUsers = await GetAllUsers();

                return allUsers.Where(u => u.email == user.email);
            }

            return null;
        }

        private async Task<IEnumerable<IIntercomUser>> GetUserListingPage(int pageNumber, HttpClient httpClient)
        {
            string requestUri = $"{IntercomUri}{USERS_ENDPOINT_URI}?per_page={IntercomPageSize}&page={pageNumber}";

            IntercomUserListingResponse listing = await GetUserListing(requestUri, httpClient);

            return listing?.users;
        }

        private async Task<IntercomUserListingResponse> GetUserListing(string requestUri, HttpClient httpClient)
        {
            var response = await HttpGetAsync(httpClient, requestUri);
            string responseContent = await response?.Content?.ReadAsStringAsync();

            return (responseContent != null) ? JsonConvert.DeserializeObject<IntercomUserListingResponse>(responseContent) : null;
        }

        /// <summary>
        /// Serializes a user object to a string.
        /// </summary>
        /// <param name="user">The Intercom user</param>
        /// <returns></returns>
        private String Transform(IIntercomUser user)
        {
            return JsonConvert.SerializeObject(user,
                           Formatting.None,
                           new JsonSerializerSettings
                           {
                               NullValueHandling = NullValueHandling.Ignore
                           });
        }

        /// <summary>
        /// Posts a user to the Intercom API endpoint.
        /// </summary>
        /// <param name="user">The Intercom user</param>
        /// <returns>The user, otherwise <see langword="null"/>.</returns>
        private async Task<IIntercomUser> Post(IIntercomUser user)
        {
            using (var httpClient = BuildClient())
            {
                try
                {
                    var content = new StringContent(Transform(user), Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync($"{IntercomUri}{USERS_ENDPOINT_URI}", content);
                    string responseContent = await response.Content.ReadAsStringAsync();

                    return JsonConvert.DeserializeObject<IntercomUser>(responseContent);
                }
                catch (Exception ex)
                {
                    LogExceptionToError(ex);

                    return null;
                }
            }
        }

        private HttpClient BuildClient()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IntercomAccessToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return httpClient;
        }

        private async Task<HttpResponseMessage> HttpGetAsync(HttpClient httpClient, string requestUri)
        {
            try
            {
                return await httpClient.GetAsync(requestUri);
            }
            catch (Exception ex)
            {
                LogExceptionToError(ex);

                return null;
            }
        }

        /// <summary>
        /// Adds results of a request burst to the <paramref name="allIntercomUsers"/> collection and returns it.
        /// </summary>
        /// <param name="allIntercomUsers">The collection of users to add to</param>
        /// <param name="span">Span of pages to burst-request</param>
        /// <param name="httpClient">HTTP client</param>
        /// <returns>The <paramref name="allIntercomUsers"/> collection with added users</returns>
        /// <remarks>It is not necessary for the method to return the updated collection (reference type). But it is more clear what the method does.</remarks>
        private async Task<List<IIntercomUser>> AddUsersBurst(List<IIntercomUser> allIntercomUsers, Tuple<int, int> span, HttpClient httpClient)
        {
            var burstResult = await GetUsersBurst(span, httpClient);

            if (burstResult != null)
            {
                allIntercomUsers.AddRange(burstResult);
            }

            return allIntercomUsers;
        }

        /// <summary>
        /// Gets flattened results of a burst of requests for users.
        /// </summary>
        /// <param name="span">Span of pages to burst-request</param>
        /// <param name="httpClient">HTTP client</param>
        /// <returns></returns>
        private async Task<IEnumerable<IIntercomUser>> GetUsersBurst(Tuple<int, int> span, HttpClient httpClient)
        {
            var tasks = new List<Task<IEnumerable<IIntercomUser>>>();
            var flatResults = new List<IIntercomUser>();

            for (int x = span.Item1; x <= span.Item2; x++)
            {
                tasks.Add(GetUserListingPage(x, httpClient));
            }

            try
            {
                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    flatResults.AddRange(result);
                }
            }
            catch (AggregateException ae)
            {
                Logger.LogError(new EventId(ae.HResult), ae, ae.Message);
                foreach (var ex in ae.InnerExceptions)
                {
                    LogExceptionToError(ex);
                }

                throw;
            }
            catch (Exception ex)
            {
                LogExceptionToError(ex);

                throw;
            }

            return flatResults;
        }

        private void LogExceptionToError(Exception exception)
        {
            if (exception != null)
            {
                Logger.LogError(new EventId(exception.HResult), exception, exception.Message);
            }
        }

        #endregion
    }
}
