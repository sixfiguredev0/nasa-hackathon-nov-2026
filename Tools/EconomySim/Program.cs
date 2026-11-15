using System;
using RedFrontier.Outpost;

// Usage: EconomySim [seeds] [E W F O M start stock]
static class Program
{
    static void Main(string[] a)
    {
        int seeds = a.Length > 0 ? int.Parse(a[0]) : 200;
        var start = (float[])ColonyEconomy.MissionStart.Clone();
        if (a.Length >= 6) for (int i = 0; i < 5; i++) start[i] = float.Parse(a[1 + i]);
        Console.Write(EconomySim.Report(seeds, start, "mission rules"));
        Console.WriteLine(EconomySim.Verdict(seeds, start));
    }
}
