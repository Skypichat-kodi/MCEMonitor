using System;

public class WakeInfo
{
    public DateTime WakeTime { get; set; }
    public DateTime SleepTime { get; set; }
    public TimeSpan SleepDuration { get; set; }
    public string Cause { get; set; } = "Inconnue";
}

