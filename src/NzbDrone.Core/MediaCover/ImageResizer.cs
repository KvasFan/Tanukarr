using System;
using System.IO;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;

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
        private readonly DecoderOptions _decoderOptions;
        private readonly bool _enabled;

        public ImageResizer(IDiskProvider diskProvider, IPlatformInfo platformInfo)
        {
            _diskProvider = diskProvider;

            _enabled = true;

            var configuration = new SixLabors.ImageSharp.Configuration(new JpegConfigurationModule(), new PngConfigurationModule(), new WebpConfigurationModule())
            {
                // More conservative memory allocation
                MemoryAllocator = new SimpleGcMemoryAllocator()
            };

            // Thumbnails don't need super high quality
            configuration.ImageFormatsManager.SetEncoder(JpegFormat.Instance, new JpegEncoder
            {
                Quality = 92
            });

            _decoderOptions = new DecoderOptions
            {
                Configuration = configuration,
                SkipMetadata = true,
                MaxFrames = 1
            };
        }

        public void Resize(string source, string destination, int height)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                if (height <= 0 || height > MaxDimension || new FileInfo(source).Length > MaxEncodedBytes)
                {
                    throw new InvalidDataException("Media cover exceeds the thumbnail size limit");
                }

                var info = Image.Identify(_decoderOptions, source);
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

                using var image = Image.Load(_decoderOptions, source);
                image.Mutate(x => x.Resize(0, height));
                image.Save(destination);
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
    }
}
