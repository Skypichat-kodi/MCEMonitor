using System;
using System.Linq;
using Microsoft.Win32.TaskScheduler;

public static class WakeTimerDetector
{
    public static bool HasActiveWakeTimer()
    {
        try
        {
            using (TaskService ts = new TaskService())
            {
                return ts.AllTasks.Any(t =>
                    t.Definition?.Settings?.WakeToRun == true &&
                    t.NextRunTime > DateTime.Now &&
                    t.Enabled
                );
            }
        }
        catch
        {
            return false;
        }
    }
}

