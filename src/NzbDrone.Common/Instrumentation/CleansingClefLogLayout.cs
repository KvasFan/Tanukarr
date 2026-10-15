using System.Text;
using NLog;
using NLog.Layouts.ClefJsonLayout;

namespace NzbDrone.Common.Instrumentation;

public class CleansingClefLogLayout : CompactJsonLayout
{
    protected override void RenderFormattedMessage(LogEventInfo logEvent, StringBuilder target)
    {
        base.RenderFormattedMessage(logEvent, target);

        var result = CleanseLogMessage.Cleanse(target.ToString());
        target.Clear();
        target.Append(result);
    }
}
