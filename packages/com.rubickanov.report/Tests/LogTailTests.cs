using System.IO;
using NUnit.Framework;

namespace Rubickanov.Report.Tests
{
    public class LogTailTests
    {
        [Test]
        public void Read_TailFromALineStart_PlayersFoldersScrubbed()
        {
            string path = Path.Combine(Path.GetTempPath(), $"logtail-{System.Guid.NewGuid():N}.log");
            File.WriteAllText(path, "first line is long enough to be cut\nC:\\Users\\alex\\AppData\\beach\\save.json\n/home/alex/x\n");
            try
            {
                string tail = LogTail.Read(path, 60, "C:/Users/alex", "/home/alex", "C:/Users/alex/AppData/beach");

                Assert.IsFalse(tail.Contains("first line"));
                Assert.AreEqual("~\\save.json\n~/x\n", tail);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void PathScrub_LogLineWithSaveFolder_UserNameGone()
        {
            var scrub = new PathScrub("/home/alex/.config/unity3d/rubickanov/Game/", "/home/alex", null, "/");

            Assert.AreEqual("[Report] On: agent, cache ~/report-cache",
                scrub.Apply("[Report] On: agent, cache /home/alex/.config/unity3d/rubickanov/Game/report-cache"));
            Assert.AreEqual("~\\notes.txt and ~/x", scrub.Apply("\\home\\alex\\notes.txt and /home/alex/x"));
            Assert.IsNull(scrub.Apply(null));
        }

        [Test]
        public void Read_NoFile_Null()
        {
            Assert.IsNull(LogTail.Read(Path.Combine(Path.GetTempPath(), "no-such.log"), 100));
        }
    }
}
