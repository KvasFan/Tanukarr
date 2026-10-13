using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Http.Test.REST
{
    [TestFixture]
    public class RestControllerNullResourceFixture : TestBase
    {
        [TestCase("POST", "null-only")]
        [TestCase("PUT", "null-only")]
        [TestCase("POST", "valid-first")]
        [TestCase("PUT", "valid-first")]
        [TestCase("POST", "null-first")]
        [TestCase("PUT", "null-first")]
        public async Task should_reject_null_bulk_members_with_bad_request_before_any_action_or_write(string verb, string order)
        {
            var controller = new FixtureController();
            var valid = new FixtureResource { Id = 9 };
            var resources = order switch
            {
                "valid-first" => new List<FixtureResource> { valid, null },
                "null-first" => new List<FixtureResource> { null, valid },
                _ => new List<FixtureResource> { null }
            };
            var actionName = verb == "POST" ? nameof(FixtureController.CreateMany) : nameof(FixtureController.ReplaceMany);
            var context = Context(controller, actionName, verb, new Dictionary<string, object> { { "resources", resources } });
            Func<Task> run = () => Invoke(controller, context, () =>
            {
                if (verb == "POST")
                {
                    controller.CreateMany(resources);
                }
                else
                {
                    controller.ReplaceMany(resources);
                }
            });

            var exception = (await run.Should().ThrowAsync<BadRequestException>()).Which;

            exception.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            controller.ActionCalls.Should().Be(0);
            controller.Persisted.Should().BeEmpty();
            valid.Id.Should().Be(9);
        }

        [TestCase("POST")]
        [TestCase("PUT")]
        public async Task should_execute_valid_bulk_requests_and_preserve_their_resource_ids(string verb)
        {
            var controller = new FixtureController();
            var resources = new List<FixtureResource> { new FixtureResource { Id = 3 }, new FixtureResource { Id = 8 } };
            var actionName = verb == "POST" ? nameof(FixtureController.CreateMany) : nameof(FixtureController.ReplaceMany);
            var context = Context(controller, actionName, verb, new Dictionary<string, object> { { "resources", resources } });

            await Invoke(controller, context, () =>
            {
                if (verb == "POST")
                {
                    controller.CreateMany(resources);
                }
                else
                {
                    controller.ReplaceMany(resources);
                }
            });

            controller.ActionCalls.Should().Be(1);
            controller.Persisted.Should().Equal(resources);
            resources[0].Id.Should().Be(3);
            resources[1].Id.Should().Be(8);
        }

        [TestCase(0, 37)]
        [TestCase(91, 91)]
        public async Task should_map_the_route_id_for_an_omitted_body_id_and_preserve_an_explicit_body_id(int bodyId, int expected)
        {
            var controller = new FixtureController();
            var resource = new FixtureResource { Id = bodyId };
            var context = Context(
                controller,
                nameof(FixtureController.ReplaceOne),
                "PUT",
                new Dictionary<string, object> { { "resource", resource }, { "id", 37 } });
            context.RouteData.Values["id"] = "37";

            await Invoke(controller, context, () => controller.ReplaceOne(resource, 37));

            controller.ActionCalls.Should().Be(1);
            controller.Persisted.Should().ContainSingle().Which.Should().BeSameAs(resource);
            resource.Id.Should().Be(expected);
        }

        private static ActionExecutingContext Context(FixtureController controller, string methodName, string verb, Dictionary<string, object> arguments)
        {
            var http = new DefaultHttpContext();
            http.Request.Method = verb;
            http.Request.Path = "/api/fixture";
            var descriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = controller.GetType().GetTypeInfo(),
                MethodInfo = controller.GetType().GetMethod(methodName),
                ActionName = methodName
            };
            var action = new ActionContext(http, new RouteData(), descriptor);
            controller.ControllerContext = new ControllerContext(action);
            return new ActionExecutingContext(action, new List<IFilterMetadata>(), arguments, controller);
        }

        private static Task Invoke(FixtureController controller, ActionExecutingContext context, Action action)
        {
            // Run MVC's inherited async filter pipeline. If the actual synchronous
            // filter rejects a body, MVC never invokes this downstream action.
            return controller.OnActionExecutionAsync(context, () =>
            {
                action();
                return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller));
            });
        }

        private class FixtureResource : RestResource
        {
            public FixtureResource()
            {
            }
        }

        private class FixtureController : RestController<FixtureResource>
        {
            public int ActionCalls { get; private set; }
            public List<FixtureResource> Persisted { get; } = new List<FixtureResource>();

            [HttpPost]
            public void CreateMany(List<FixtureResource> resources)
            {
                ActionCalls++;
                Persisted.AddRange(resources);
            }

            [HttpPut]
            public void ReplaceMany(List<FixtureResource> resources)
            {
                ActionCalls++;
                Persisted.AddRange(resources);
            }

            [RestPutById]
            public void ReplaceOne(FixtureResource resource, int id)
            {
                ActionCalls++;
                Persisted.Add(resource);
            }
        }
    }
}
