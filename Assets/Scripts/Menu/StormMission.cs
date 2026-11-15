using System;

internal static class StormMission
{
    public const int Budget = 8;
    public const int InitialCondition = 70;
    public const int FacilityCount = 4;
    private static readonly int[] StormDemand = { 2, 4, 2, 2 };
    private static readonly int[] RecoveryDemand = { 2, 2, 4, 2 };
    private static readonly int[] RecoveryAllocation = { 2, 1, 4, 1 };

    public static int Demand(int facility) => StormDemand[facility];

    public static Result Preview(int[] allocation)
    {
        if (allocation == null || allocation.Length != FacilityCount)
            throw new ArgumentException("Assign power to all four facilities.", nameof(allocation));

        int used = 0;
        foreach (int power in allocation)
        {
            if (power < 0 || power > Budget - used)
                throw new ArgumentOutOfRangeException(nameof(allocation), "Share up to eight units.");
            used += power;
        }

        var afterStorm = new int[FacilityCount];
        var afterRecovery = new int[FacilityCount];
        for (int i = 0; i < FacilityCount; i++)
        {
            afterStorm[i] = Apply(InitialCondition, allocation[i], StormDemand[i]);
            afterRecovery[i] = Apply(afterStorm[i], RecoveryAllocation[i], RecoveryDemand[i]);
        }
        return new Result(Budget - used, afterStorm, afterRecovery);
    }

    private static int Apply(int condition, int allocated, int demand)
    {
        return Math.Max(0, Math.Min(100, condition + (allocated - demand) * 10));
    }

    public sealed class Result
    {
        public int Available { get; }
        private readonly int[] storm, recovery;

        internal Result(int available, int[] storm, int[] recovery)
        {
            Available = available;
            this.storm = storm;
            this.recovery = recovery;
        }

        public int AfterStorm(int facility) => storm[facility];
        public int AfterRecovery(int facility) => recovery[facility];
    }
}

