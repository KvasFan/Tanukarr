using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.LibraryTools;

namespace NzbDrone.Core.Test.LibraryTools
{
    [TestFixture]
    public class ArchivePathGuardFixture
    {
        [TestCase("../outside.mkv")]
        [TestCase("folder/../../outside.mkv")]
        [TestCase("")]
        public void rejects_paths_outside_the_configured_folder(string relative)
        {
            Assert.Throws<IOException>(() => ArchivePathGuard.Resolve(Path.Combine(Path.GetTempPath(), "archive-guard"), relative));
        }

        [Test]
        public void accepts_nested_relative_media_paths()
        {
            var root = Path.Combine(Path.GetTempPath(), "archive-guard-" + Guid.NewGuid().ToString("N"));
            ArchivePathGuard.Resolve(root, Path.Combine("Season 01", "episode.mkv")).Should().Be(Path.Combine(root, "Season 01", "episode.mkv"));
        }

        [Test]
        public void handles_a_filesystem_root_without_accepting_the_root_itself()
        {
            var root = Path.GetPathRoot(Path.GetTempPath());
            var relative = "archive-guard-" + Guid.NewGuid().ToString("N") + ".mkv";

            ArchivePathGuard.Resolve(root, relative).Should().Be(Path.Combine(root, relative));
            Assert.Throws<IOException>(() => ArchivePathGuard.Resolve(root, "."));
        }

        [Test]
        public void rejects_an_absolute_media_path()
        {
            var root = Path.Combine(Path.GetTempPath(), "archive-guard");
            Assert.Throws<IOException>(() => ArchivePathGuard.Resolve(root, Path.Combine(Path.GetTempPath(), "outside.mkv")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void rejects_symbolic_links_in_relative_restore_paths(bool directoryLink)
        {
            var fixture = Path.Combine(Path.GetTempPath(), "archive-guard-links-" + Guid.NewGuid().ToString("N"));
            var root = Path.Combine(fixture, "root");
            var outside = Path.Combine(fixture, "outside");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "episode.mkv"), "owned test fixture");

            try
            {
                var relative = directoryLink ? Path.Combine("linked", "episode.mkv") : "linked.mkv";
                try
                {
                    if (directoryLink)
                    {
                        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
                    }
                    else
                    {
                        File.CreateSymbolicLink(Path.Combine(root, relative), Path.Combine(outside, "episode.mkv"));
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException || (OperatingSystem.IsWindows() && ex is IOException))
                {
                    Assert.Ignore("This host does not permit symbolic-link creation: " + ex.Message);
                }

                Assert.Throws<IOException>(() => ArchivePathGuard.Resolve(root, relative));
                File.ReadAllText(Path.Combine(outside, "episode.mkv")).Should().Be("owned test fixture");
            }
            finally
            {
                Directory.Delete(fixture, true);
            }
        }
    }
}
