using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VanillaConnect.Gravatar;
using VanillaConnect.Intercom;

namespace VanillaConnect
{
	public class Startup
	{
		public IConfiguration Configuration { get; }

		/// <summary>
		/// This method gets called by the runtime.
		/// </summary>
		public Startup(IHostingEnvironment env)
		{
			var builder = new ConfigurationBuilder()
				.SetBasePath(env.ContentRootPath)
				.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
				.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
				.AddEnvironmentVariables();

			if (env.IsDevelopment())
			{
				// Override with user secrets (http://go.microsoft.com/fwlink/?LinkID=532709)
				builder.AddUserSecrets<Startup>();
			}
			Configuration = builder.Build();
		}

		/// <summary>
		/// This method gets called by the runtime. It's used to add services to the container.
		/// </summary>
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddMvc().AddJsonOptions(options =>
			{
				// Setup json serializer
				options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;

				// Duplicate the settings in JsonConvert
				JsonConvert.DefaultSettings = () => options.SerializerSettings;
			});

			services.AddSingleton(Configuration);
			services.AddTransient<HashAlgorithm>(h => SHA512.Create());
			services.AddTransient<IAvatarProvider, GravatarProvider>();
            services.AddMemoryCache();
            services.AddTransient<IIntercomUsersClient, IntercomUsersClient>();
		}

		/// <summary>
		/// This method gets called by the runtime. It's to configure the HTTP request pipeline.
		/// </summary>
		public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
		{
			loggerFactory.AddConsole(Configuration.GetSection("Logging"));
			loggerFactory.AddDebug();
			loggerFactory.AddFile(Configuration.GetSection("Logging"));

			// Implemented according to: https://docs.microsoft.com/en-us/aspnet/core/security/authentication/cookie
			app.UseCookieAuthentication(new CookieAuthenticationOptions
			{
				AuthenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme,
				AutomaticAuthenticate = true,
				AutomaticChallenge = false,
				CookieSecure = CookieSecurePolicy.None,
				CookieHttpOnly = true
			});

			app.UseStaticFiles();
			app.UseMvc();
		}
	}
}
