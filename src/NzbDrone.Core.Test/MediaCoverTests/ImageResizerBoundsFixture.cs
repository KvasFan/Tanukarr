using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Test.Framework;
using SkiaSharp;

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
            WriteImage(120, 80, format switch
            {
                "jpeg" => SKEncodedImageFormat.Jpeg,
                "webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png
            });

            Subject.Resize(_source, _destination, 40);
            using var thumbnail = SKCodec.Create(_destination);
            thumbnail.Info.Width.Should().Be(60);
            thumbnail.Info.Height.Should().Be(40);
            thumbnail.FrameCount.Should().BeLessThanOrEqualTo(1);
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
            File.WriteAllBytes(_source, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));

            File.WriteAllText(_destination, "stale thumbnail");
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 40));
            File.Exists(_destination).Should().BeFalse();
        }

        [TestCase(".jpg", SKEncodedImageFormat.Jpeg)]
        [TestCase(".JPEG", SKEncodedImageFormat.Jpeg)]
        [TestCase(".png", SKEncodedImageFormat.Png)]
        [TestCase(".WEBP", SKEncodedImageFormat.Webp)]
        public void should_choose_the_encoder_from_the_destination_extension(string extension, SKEncodedImageFormat expectedFormat)
        {
            WritePng(120, 80);
            _destination = Path.ChangeExtension(_destination, extension);
            Subject.Resize(_source, _destination, 40);

            using var thumbnail = SKCodec.Create(_destination);
            thumbnail.EncodedFormat.Should().Be(expectedFormat);
        }

        [Test]
        public void should_preserve_png_transparency()
        {
            using (var bitmap = new SKBitmap(120, 80))
            {
                bitmap.Erase(new SKColor(30, 80, 140, 64));
                WriteBitmap(bitmap, SKEncodedImageFormat.Png);
            }

            _destination = Path.ChangeExtension(_destination, ".png");
            Subject.Resize(_source, _destination, 40);
            using var thumbnail = SKBitmap.Decode(_destination);
            thumbnail.GetPixel(20, 20).Alpha.Should().Be(64);
        }

        [Test]
        public void should_strip_exif_without_rotating_the_stored_raster()
        {
            WriteImage(120, 80, SKEncodedImageFormat.Jpeg);
            var original = File.ReadAllBytes(_source);
            byte[] exif =
            {
                0xFF, 0xE1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
                1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0
            };
            using (var stream = File.Create(_source))
            {
                stream.Write(original, 0, 2);
                stream.Write(exif);
                stream.Write(original, 2, original.Length - 2);
            }

            using (var source = SKCodec.Create(_source))
            {
                source.EncodedOrigin.Should().Be(SKEncodedOrigin.RightTop);
            }

            Subject.Resize(_source, _destination, 40);
            using var thumbnail = SKCodec.Create(_destination);
            thumbnail.Info.Width.Should().Be(60);
            thumbnail.Info.Height.Should().Be(40);
            thumbnail.EncodedOrigin.Should().Be(SKEncodedOrigin.TopLeft);
            Encoding.Latin1.GetString(File.ReadAllBytes(_destination)).Should().NotContain("Exif\0\0");
        }

        [Test]
        public void should_decode_only_the_first_animated_webp_frame()
        {
            File.Copy(GetTestPath("Files/MediaCover/first-frame.webp"), _source);
            using (var source = SKCodec.Create(_source))
            {
                source.FrameCount.Should().Be(2);
            }

            _destination = Path.ChangeExtension(_destination, ".webp");
            Subject.Resize(_source, _destination, 40);
            using var thumbnail = SKCodec.Create(_destination);
            thumbnail.FrameCount.Should().BeLessThanOrEqualTo(1);
            thumbnail.Info.Height.Should().Be(40);
        }

        [Test]
        public void should_reject_a_truncated_image_and_remove_a_stale_destination()
        {
            WritePng(120, 80);
            var bytes = File.ReadAllBytes(_source);
            File.WriteAllBytes(_source, bytes[..(bytes.Length / 2)]);
            File.WriteAllText(_destination, "stale thumbnail");
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 40));
            File.Exists(_destination).Should().BeFalse();
        }

        [Test]
        public void should_reject_an_unsupported_output_format_and_remove_a_stale_destination()
        {
            WritePng(120, 80);
            _destination = Path.ChangeExtension(_destination, ".gif");
            File.WriteAllText(_destination, "stale thumbnail");
            Assert.Throws<InvalidDataException>(() => Subject.Resize(_source, _destination, 40));
            File.Exists(_destination).Should().BeFalse();
        }

        private void WritePng(int width, int height)
        {
            WriteImage(width, height, SKEncodedImageFormat.Png);
        }

        private void WriteImage(int width, int height, SKEncodedImageFormat format)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(SKColors.CornflowerBlue);
            WriteBitmap(bitmap, format);
        }

        private void WriteBitmap(SKBitmap bitmap, SKEncodedImageFormat format)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(format, 92);
            using var stream = File.Create(_source);
            encoded.SaveTo(stream);
        }
    }
}
