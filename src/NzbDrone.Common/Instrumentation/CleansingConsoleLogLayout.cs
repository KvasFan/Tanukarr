using System.Text;
using NLog;
using NLog.Layouts;

namespace NzbDrone.Common.Instrumentation;

public class CleansingConsoleLogLayout(string format)
    : SimpleLayout(format)
{
    protected override void RenderFormattedMessage(LogEventInfo logEvent, StringBuilder target)
    {
        // Tanukarr cleanses every build, not only production (raw-log privacy).
        var result = CleanseLogMessage.Cleanse(logEvent, RenderUncleansed);
        target.Append(result);
    }

    private string RenderUncleansed(LogEventInfo logEvent)
    {
        var builder = new StringBuilder();
        base.RenderFormattedMessage(logEvent, builder);

        return builder.ToString();
    }
}
