using System.Text;
using NLog;
using NLog.Layouts;

namespace NzbDrone.Common.Instrumentation;

public class CleansingConsoleLogLayout(string format)
    : SimpleLayout(format)
{
    protected override void RenderFormattedMessage(LogEventInfo logEvent, StringBuilder target)
    {
        base.RenderFormattedMessage(logEvent, target);

        var result = CleanseLogMessage.Cleanse(target.ToString());
        target.Clear();
        target.Append(result);
    }
}
