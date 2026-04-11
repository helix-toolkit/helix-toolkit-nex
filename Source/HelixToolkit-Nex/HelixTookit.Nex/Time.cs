namespace HelixToolkit.Nex;

public static class Time
{
    private static readonly long _baseTimestamp = Stopwatch.GetTimestamp();

    public static ulong GetMonoTimeMs()
    {
        return (ulong)(
            (double)(Stopwatch.GetTimestamp() - _baseTimestamp) / Stopwatch.Frequency * 1000u
        );
    }
}
