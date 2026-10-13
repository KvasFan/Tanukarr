using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Test.Common;
using Sonarr.Api.V3.Config;
using Sonarr.Api.V5.Settings;

namespace NzbDrone.Api.Test.Settings
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class GeneralSettingsInstanceNameFixture : TestBase
    {
        private readonly bool _useV3;

        public GeneralSettingsInstanceNameFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [TestCase("Tanukarr")]
        [TestCase("Tanukarr - Anime")]
        [TestCase("Anime Tanukarr")]
        [TestCase("Sonarr")]
        [TestCase("SonarrWeeb")]
        [TestCase("Sonarr Pro")]
        [TestCase("Sonarr Office")]
        [TestCase("Office Sonarr")]
        public void should_accept_product_and_legacy_instance_names_when_saving_general_settings(string instanceName)
        {
            var (controller, context) = CreatePutAction(instanceName);

            Action validate = () => controller.OnActionExecuting(context);

            validate.Should().NotThrow();
        }

        [TestCase("Anime Library")]
        [TestCase("My Tanukarr Library")]
        [TestCase("My Sonarr Library")]
        [TestCase("")]
        [TestCase(null)]
        public void should_preserve_the_existing_instance_name_prefix_or_suffix_policy(string instanceName)
        {
            var (controller, context) = CreatePutAction(instanceName);

            Action validate = () => controller.OnActionExecuting(context);

            validate.Should().Throw<ValidationException>()
                    .Which.Errors.Should().Contain(error => error.PropertyName == "InstanceName");
        }

        private (Controller Controller, ActionExecutingContext Context) CreatePutAction(string instanceName)
        {
            var general = new GeneralSettingsResource
            {
                Id = 1,
                BindAddress = "*",
                Port = 8989,
                AuthenticationMethod = AuthenticationType.Forms,
                AuthenticationRequired = AuthenticationRequiredType.Enabled,
                AllowedHosts = string.Empty,
                Username = "fixture-user",
                Password = "fixture-password",
                PasswordConfirmation = "fixture-password",
                UrlBase = string.Empty,
                TrustedNetworks = string.Empty,
                InstanceName = instanceName,
                LogSizeLimit = 1,
                Branch = "main",
                BackupFolder = "Backups",
                BackupInterval = 7,
                BackupRetention = 28
            };

            Controller controller;
            object resource;
            string actionName;
            if (_useV3)
            {
                controller = Mocker.Resolve<HostConfigController>();
                var host = new HostConfigResource();
                foreach (var property in typeof(GeneralSettingsResource).GetProperties())
                {
                    var hostProperty = typeof(HostConfigResource).GetProperty(property.Name);
                    if (hostProperty?.CanWrite == true)
                    {
                        hostProperty.SetValue(host, property.GetValue(general));
                    }
                }

                resource = host;
                actionName = nameof(HostConfigController.SaveHostConfig);
            }
            else
            {
                controller = Mocker.Resolve<GeneralSettingsController>();
                resource = general;
                actionName = nameof(GeneralSettingsController.SaveSettings);
            }

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "PUT";
            var descriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = controller.GetType().GetTypeInfo(),
                MethodInfo = controller.GetType().GetMethod(actionName)
            };
            var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
            controller.ControllerContext = new ControllerContext(actionContext);

            var context = new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", resource } },
                controller);

            return (controller, context);
        }
    }
}
