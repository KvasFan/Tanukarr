using System;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Sonarr.Http.Authentication;

namespace Sonarr.Http.Frontend.Mappers
{
    public class LoginHtmlMapper : HtmlMapperBase
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAuthenticationService _authenticationService;
        private readonly IAntiforgery _antiforgery;

        public LoginHtmlMapper(IAppFolderInfo appFolderInfo,
                               IDiskProvider diskProvider,
                               Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                               IConfigFileProvider configFileProvider,
                               IAuthenticationService authenticationService,
                               IAntiforgery antiforgery,
                               Logger logger)
            : base(diskProvider, configFileProvider, cacheBreakProviderFactory, logger)
        {
            _appFolderInfo = appFolderInfo;
            _configFileProvider = configFileProvider;
            _authenticationService = authenticationService;
            _antiforgery = antiforgery;
        }

        protected override string FolderPath => _appFolderInfo.GetUiFolder();
        protected override string HtmlPath => Path.Combine(FolderPath, "login.html");

        protected override string MapPath(string resourceUrl)
        {
            return HtmlPath;
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/login");
        }

        protected override string GetHtmlText(HttpContext context)
        {
            var html = base.GetHtmlText(context);
            var theme = _configFileProvider.Theme;
            var authMethod = _configFileProvider.AuthenticationMethod.ToString().ToLowerInvariant();

            if (_configFileProvider.AuthenticationMethod == AuthenticationType.Oidc &&
                !_configFileProvider.IsOidcConfigured())
            {
                authMethod += " oidc-misconfigured";
            }

            html = html.Replace("_THEME_", theme);
            html = html.Replace("_AUTH_METHOD_", authMethod);
            var setupRequired = _authenticationService.IsInitialSetupRequired;
            html = html.Replace("_SETUP_CLASS_", setupRequired ? "setup-required" : string.Empty);
            html = html.Replace("_SETUP_TOKEN_", setupRequired
                ? HtmlEncoder.Default.Encode(_antiforgery.GetAndStoreTokens(context).RequestToken)
                : string.Empty);

            return html;
        }
    }
}
