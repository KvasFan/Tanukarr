using System;
using System.IO;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using SkiaSharp;

namespace NzbDrone.Core.MediaCover
{
    public interface IImageResizer
    {
        void Resize(string source, string destination, int height);
    }

    public class ImageResizer : IImageResizer
    {
        private const long MaxEncodedBytes = 20 * 1024 * 1024;
        private const long MaxPixels = 16 * 1024 * 1024;
        private const int MaxDimension = 16384;
        private readonly IDiskProvider _diskProvider;

        public ImageResizer(IDiskProvider diskProvider, IPlatformInfo platformInfo)
        {
            _diskProvider = diskProvider;
        }

        public void Resize(string source, string destination, int height)
        {
            try
            {
                if (height <= 0 || height > MaxDimension || new FileInfo(source).Length > MaxEncodedBytes)
                {
                    throw new InvalidDataException("Media cover exceeds the thumbnail size limit");
                }

                using var codec = SKCodec.Create(source);
                if (codec == null || !IsSupportedFormat(codec.EncodedFormat))
                {
                    throw new InvalidDataException("Unsupported or invalid media cover format");
                }

                var info = codec.Info;
                if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension ||
                    (long)info.Width * info.Height > MaxPixels)
                {
                    throw new InvalidDataException("Media cover exceeds the thumbnail pixel limit");
                }

                var outputWidth = (long)Math.Ceiling((double)info.Width * height / info.Height);
                if (outputWidth > MaxDimension || outputWidth * height > MaxPixels)
                {
                    throw new InvalidDataException("Media cover exceeds the thumbnail pixel limit");
                }

                // Decode only the first frame into a bounded raster. Re-encoding
                // that raster does not copy EXIF, ICC or animation metadata.
                using var image = new SKBitmap(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                if (codec.GetPixels(image.Info, image.GetPixels()) != SKCodecResult.Success)
                {
                    throw new InvalidDataException("Could not decode media cover");
                }

                var width = Math.Max(1, (int)Math.Round((double)info.Width * height / info.Height));
                using var thumbnail = image.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKCubicResampler.CatmullRom));
                if (thumbnail == null)
                {
                    throw new InvalidDataException("Could not resize media cover");
                }

                using var output = SKImage.FromBitmap(thumbnail);
                using var encoded = output.Encode(GetOutputFormat(destination), 92);
                if (encoded == null)
                {
                    throw new InvalidDataException("Could not encode media cover");
                }

                using var destinationStream = File.Create(destination);
                encoded.SaveTo(destinationStream);
            }
            catch
            {
                if (_diskProvider.FileExists(destination))
                {
                    _diskProvider.DeleteFile(destination);
                }

                throw;
            }
        }

        private static bool IsSupportedFormat(SKEncodedImageFormat format)
        {
            return format is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp;
        }

        private static SKEncodedImageFormat GetOutputFormat(string destination)
        {
            return Path.GetExtension(destination).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => throw new InvalidDataException("Unsupported media cover output format")
            };
        }
    }
}
