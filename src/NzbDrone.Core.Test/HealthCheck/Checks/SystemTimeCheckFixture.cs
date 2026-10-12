using System;
using System.Text;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class SystemTimeCheckFixture : CoreTest<SystemTimeCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ISonarrCloudRequestBuilder>()
                .Setup(s => s.Services)
                .Returns(new HttpRequestBuilder("https://clock.example.test/").CreateFactory());

            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                .Returns("Some Warning Message");
        }

        private void GivenServerTime(DateTime dateTime)
        {
            var json = new ServiceTimeResponse { DateTimeUtc = dateTime }.ToJson();

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), Encoding.ASCII.GetBytes(json)));
        }

        [Test]
        public void should_not_return_error_when_system_time_is_close_to_server_time()
        {
            GivenServerTime(DateTime.UtcNow);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_error_when_system_time_is_more_than_one_day_from_server_time()
        {
            GivenServerTime(DateTime.UtcNow.AddDays(2));

            Subject.Check().ShouldBeError();
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_contact_disabled_sonarr_clock_service()
        {
            Mocker.GetMock<ISonarrCloudRequestBuilder>()
                .Setup(s => s.Services)
                .Returns(new SonarrCloudRequestBuilder().Services);

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<IHttpClient>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
