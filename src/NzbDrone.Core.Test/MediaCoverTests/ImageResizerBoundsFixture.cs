using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Test.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class ImageResizerBoundsFixture : CoreTest<MediaCover.ImageResizer>
    {
        private string _source;
        private string _destination;

        [SetUp]
        public void Setup()
        {
            _source = Path.Combine(TempFolder, "generated-source.png");
            _destination = Path.Combine(TempFolder, "generated-thumbnail.jpg");
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
        }

        [TestCase("png")]
        [TestCase("jpeg")]
        [TestCase("webp")]
        public void should_resize_the_supported_formats_with_the_correct_aspect_ratio(string format)
        {
            using (var image = new Image<Rgba32>(120, 80))
            {
                switch (format)
                {
                    case "jpeg": image.SaveAsJpeg(_source); break;
                    case "webp": image.SaveAsWebp(_source); break;
                    default: image.SaveAsPng(_source); break;
                }
            }

            Subject.Resize(_source, _destination, 40);
            using var thumbnail = Image.Load(_destination);
            thumbnail.Width.Should().Be(60);
            thumbnail.Height.Should().Be(40);
            thumbnail.Frames.Count.Should().Be(1);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(16385)]
        public void should_reject_invalid_output_heights_and_remove_a_stale_destination(int height)
        {
            WritePng(120, 80);
            File.WriteAllText(_destination, "stale thumbnail");
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, height));
            File.Exists(_destination).Should().BeFalse();
            File.Exists(_source).Should().BeTrue();
        }

        [Test]
        public void should_reject_an_encoded_cover_over_twenty_mebibytes_before_decoding()
        {
            WritePng(120, 80);
            using (var file = new FileStream(_source, FileMode.Open, FileAccess.Write))
            {
                file.SetLength((20L * 1024 * 1024) + 1);
            }

            File.WriteAllText(_destination, "stale thumbnail");
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 40));
            File.Exists(_destination).Should().BeFalse();
            new FileInfo(_source).Length.Should().Be((20L * 1024 * 1024) + 1);
        }

        [Test]
        public void should_reject_extreme_output_aspect_ratio_amplification()
        {
            WritePng(400, 1);
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 500));
            File.Exists(_destination).Should().BeFalse();
        }

        [Test]
        public void should_reject_output_pixel_amplification_even_with_individually_valid_dimensions()
        {
            WritePng(10, 10);
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 5000));
            File.Exists(_destination).Should().BeFalse();
        }

        [Test]
        public void should_reject_a_source_dimension_over_the_limit()
        {
            WritePng(16385, 1);
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 1));
            File.Exists(_destination).Should().BeFalse();
        }

        [Test]
        public void should_reject_a_source_pixel_budget_over_the_limit()
        {
            WritePng(4097, 4096);
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 40));
            File.Exists(_destination).Should().BeFalse();
        }

        [Test]
        public void should_leave_unsupported_decoders_disabled_and_remove_a_stale_destination()
        {
            using (var image = new Image<Rgba32>(120, 80))
            {
                image.SaveAsGif(_source);
            }

            File.WriteAllText(_destination, "stale thumbnail");
            Assert.Throws<UnknownImageFormatException>(() => Subject.Resize(_source, _destination, 40));
            File.Exists(_destination).Should().BeFalse();
        }

        private void WritePng(int width, int height)
        {
            using var image = new Image<Rgba32>(width, height);
            image.SaveAsPng(_source);
        }
    }
}
