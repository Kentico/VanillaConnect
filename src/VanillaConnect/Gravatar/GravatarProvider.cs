using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VanillaConnect.Models;

namespace VanillaConnect.Gravatar
{
    public class GravatarProvider : IAvatarProvider
    {
        public ILogger<GravatarProvider> Logger { get; }

        public GravatarProvider(ILogger<GravatarProvider> logger)
        {
            Logger = logger;
        }

        public async Task<string> GetAvatarUrlAsync(string email, int timeOutSeconds = 5)
        {
            var hash = GetGravatarHash(email);
            var profileUrl = $"https://www.gravatar.com/{hash}.json";

            using (HttpClient httpClient = new HttpClient())
            {
                HttpRequestMessage request = new HttpRequestMessage
                {
                    RequestUri = new Uri(profileUrl),
                    Method = HttpMethod.Get,
                    Headers = {
                        { "User-Agent", "csharp" },
                        { "Connection", "close" },

                        // Required for proper HTTPS-to-HTTPS redirects.
                        { "Accept-Language", "en" }
                    }
                };

                httpClient.Timeout = new TimeSpan(0, 0, 0, timeOutSeconds);

                string responseContent;
                try
                {
                    using (HttpResponseMessage response = await httpClient.SendAsync(request))
                    {
                        responseContent = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            var profile = JsonConvert.DeserializeObject<Profile>(responseContent);
                            return profile?.entry?.FirstOrDefault()?.thumbnailUrl;
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            // May happen often due to non-existent Gravatars, hence not logging.
                        }
                        else
                        {
                            throw new Exception($"Error sending request to '{request.RequestUri}'. Status: {response.StatusCode}, Reason phrase: {response.ReasonPhrase}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(new EventId(ex.HResult), ex, ex.Message);
                }

                return null;
            }
        }

        public string GetGravatarHash(string email)
        {
            MD5 md5Hash = MD5.Create();
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(email.Trim().ToLower()));
            return data.ToHexString();
        }
    }
}
