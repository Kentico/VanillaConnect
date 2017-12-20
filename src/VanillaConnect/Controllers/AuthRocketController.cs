using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using jsConnectNetCore.Controllers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using VanillaConnect.Gravatar;
using VanillaConnect.Models;
using VanillaConnect.Intercom;
using System.Linq;

namespace VanillaConnect.Controllers
{
    [Route("[controller]")]
    public class AuthRocketController : AbstractControllerBase<AuthRocketController>
    {
        #region "Constants"

        private const string CLAIM_TYPE_USERID = "uid";
        private const string CLAIM_TYPE_SESSIONID = "sessionid";
        private const string CLAIM_TYPE_AVATARURL = "AvatarUrl";

        #endregion

        #region "Configuration"

        private bool AuthRocketManaged => Configuration.GetValue("AuthRocket:Managed", false);
        private string AuthrocketApiKey => Configuration.GetValue("AuthRocket:ApiKey", string.Empty);
        private string JWTSecret => Configuration.GetValue("AuthRocket:JWTSecret", string.Empty);
        private string AuthrocketRealm => Configuration.GetValue("AuthRocket:Realm", string.Empty);
        private string LoginRedirectUrl => Configuration.GetValue("AuthRocket:LoginRedirectUrl", string.Empty);
        private string LogoutRedirectUrl => Configuration.GetValue("AuthRocket:LogoutRedirectUrl", string.Empty);
        private TimeSpan TicketExpires => Configuration.GetValue("AuthRocket:TicketExpires", new TimeSpan(7, 0, 0, 0));
        private bool EnhancedSSOEnabled => Configuration.GetValue("AuthRocket:EnhancedSSOEnabled", false);
        private bool ImportAvatar => Configuration.GetValue("AuthRocket:ImportAvatar", false);
        private bool UseIntercom => Configuration.GetValue("AuthRocket:UseIntercom", false);

        #endregion

        #region "JWT Validation"

        private JwtSecurityTokenHandler SecurityHandler => new JwtSecurityTokenHandler();
        private SecurityKey SecurityKey => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWTSecret));
        private TokenValidationParameters TokenValidationParameters => new TokenValidationParameters { IssuerSigningKey = SecurityKey, ValidateAudience = false, ValidateIssuer = false };

        #endregion

        protected readonly IAvatarProvider AvatarProvider;

        protected readonly IIntercomUsersClient IntercomUsersClient;

        public AuthRocketController(IConfiguration configuration, ILogger<AuthRocketController> logger, ILoggerFactory loggerFactory, IAvatarProvider avatarProvider, IIntercomUsersClient intercomUsersClient) : base(configuration, logger, loggerFactory)
        {
            AvatarProvider = avatarProvider;
            IntercomUsersClient = intercomUsersClient;
        }

        /// <summary>
        /// Signs in the user defined by the given token.
        /// </summary>
        /// <param name="token">JWT token containing information about the user to be signed in</param>
        /// <returns><see cref="RedirectResult"/> pointing to <see cref="LoginRedirectUrl"/></returns>
        [HttpGet("[action]")]
        public async Task<ActionResult> SignIn([FromQuery] string token)
        {
            var principal = await GetPrincipal(token, AuthRocketManaged);
            string email = principal?.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email).Value;

            if (UseIntercom && !string.IsNullOrEmpty(email))
            {
                // The process of handing over the profile URL to Intercom is independent from the authentication itself, hence running in a completely separate, non-blocking thread.
                // The 'var task' variable serves just to the purpose of suppressing the CS4014 compiler warning.
                var task = Task.Run(() => IntercomUsersClient.CreateOrUpdateWithProfileUrl(email));
            }

            if (principal != null)
            {
                await HttpContext.Authentication.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = true,
                    ExpiresUtc = DateTime.UtcNow.Add(TicketExpires)
                });
            }

            return new RedirectResult(LoginRedirectUrl);
        }

        /// <summary>
        /// Signs out the current user. Implemented in accordance with https://authrocket.com/docs/redirect_handling.
        /// </summary>
        /// <returns><see cref="RedirectResult"/> pointing to <see cref="LogoutRedirectUrl"/></returns>
        [HttpGet("[action]")]
        public async Task<ActionResult> SignOut()
        {
            try
            {
                if (AuthRocketManaged)
                {
                    // Delete session accoring to https://authrocket.com/docs/api/sessions#method-delete
                    var sessionId = HttpContext.User.FindFirst(CLAIM_TYPE_SESSIONID).Value;
                    var request = CreateAuthRocketRequest($"/v1/sessions/{sessionId}");
                    request.Method = HttpMethod.Delete;
                    using (HttpClient httpClient = new HttpClient())
                    {
                        HttpResponseMessage response = await httpClient.SendAsync(request);

                        if (response.StatusCode != HttpStatusCode.NoContent)
                        {
                            throw new Exception($"Session was not removed. Status code: {response.StatusCode} Content: {response.Content}");
                        }
                    }
                }
                else
                {
                    // If the realms is set to remember the session in cookie (Enhanced SSO enabled) then the user needs to be redirected to the logout URL
                    if (EnhancedSSOEnabled && (string.IsNullOrEmpty(LoginRedirectUrl) || !LoginRedirectUrl.EndsWith("e1.loginrocket.com/logout")))
                    {
                        throw new Exception("Set LoginRedirectUrl to https://<yourapp>.e1.loginrocket.com/logout");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(new EventId(ex.HResult), ex, ex.Message);
            }
            await HttpContext.Authentication.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new RedirectResult(LogoutRedirectUrl);
        }

        /// <summary>
        /// Creates a <see cref="ClaimsPrincipal"/> by querying AuthRocket API using the given JWT token
        /// </summary>
        /// <param name="token">JWT token containing user information</param>
        /// <param name="managed">Indicates whether to utilize /sessions/ API (managed sessions must be enabled in AuthRocket)</param>
        private async Task<ClaimsPrincipal> GetPrincipal(string token, bool managed)
        {
            if (managed)
            {
                // Get user from managed session by querying /sessions/ API
                Session session = await GetSession(token);
                ClaimsIdentity identity = await UserToIdentityAsync(session.user, session.id);
                if (identity != null)
                {
                    return new ClaimsPrincipal(identity);
                }
                return null;
            }
            else
            {
                // Get user from JWT token and retrieve necessary information from the /users/ API
                SecurityToken st;
                ClaimsPrincipal principal = SecurityHandler.ValidateToken(token, TokenValidationParameters, out st);

                // Get AuthRocket user identifier (https://authrocket.com/docs/jwt_login_tokens)
                string userId = principal.FindFirst(CLAIM_TYPE_USERID).Value;
                var user = await GetUserFromUser(userId);
                principal.AddIdentity(await UserToIdentityAsync(user));
                return principal;
            }
        }

        /// <summary>
        /// Converts <see cref="User"/> model to <see cref="ClaimsIdentity"/>.
        /// </summary>
        private async Task<ClaimsIdentity> UserToIdentityAsync(User user, string sessionId = null)
        {
            if (user.id != null)
            {
                List<Claim> claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.id),
                    new Claim(ClaimTypes.Name, user.name),
                    new Claim(ClaimTypes.Email, user.email)
                };
                if (sessionId != null)
                {
                    claims.Add(new Claim(CLAIM_TYPE_SESSIONID, sessionId));
                }
                if (ImportAvatar)
                {
                    var avatarUrl = await AvatarProvider.GetAvatarUrlAsync(user.email);
                    if (!string.IsNullOrEmpty(avatarUrl))
                    {
                        claims.Add(new Claim(CLAIM_TYPE_AVATARURL, avatarUrl));
                    }
                }
                return new ClaimsIdentity(claims, "Custom");
            }
            return null;
        }

        /// <summary>
        /// Uses the given token to do a roundtrip to AuthRocket and retrieve session data.
        /// </summary>
        /// <param name="token">Token used to identify session</param>
        private async Task<Session> GetSession(string token)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                var request = CreateAuthRocketRequest($"/v1/sessions/{token}");
                HttpResponseMessage response = await httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<Session>(responseContent);
            }
        }

        /// <summary>
        /// Uses the given token to do a roundtrip to AuthRocket and retrieve user's data.
        /// </summary>
        /// <param name="userId">User's identifier</param>
        private async Task<User> GetUserFromUser(string userId)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                var request = CreateAuthRocketRequest($"/v1/users/{userId}");
                HttpResponseMessage response = await httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<User>(responseContent);
            }
        }

        /// <summary>
        /// Prepares a <see cref="HttpRequestMessage"/> object for further AuthRocket API querying.
        /// </summary>
        /// <param name="endpoint">AuthRocket API endpoint. E.g. "/v1/users"</param>
        private HttpRequestMessage CreateAuthRocketRequest(string endpoint)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("https://api-e1.authrocket.com" + endpoint),
                Method = HttpMethod.Get
            };

            request.Headers.Add("X-Authrocket-Api-Key", AuthrocketApiKey);
            request.Headers.Add("X-Authrocket-Realm", AuthrocketRealm);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }
    }
}
