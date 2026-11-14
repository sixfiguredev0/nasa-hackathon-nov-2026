using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace RedFrontier.Core.Tests
{
    /// <summary>
    /// Balance benchmark on the bundled (illustrative) content: simulation evidence, not human playtesting.
    /// Both strategies use only what the panel shows: the snapshot, the forecast for each setting the player
    /// tries, and the visible choice effects. No future events, no seed-specific play.
    /// Targets (prototype): Sensible wins >= 90%, Neglect wins <= 50%, over 200 seeds.
    /// </summary>
    public class BalanceTests
    {
        const int Seeds = 200;
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        static (MissionConfig, List<EventDef>) Load()
        {
            string dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Content", "example");
            var mission = JsonSerializer.Deserialize<MissionConfig>(File.ReadAllText(Path.Combine(dir, "mission.json")), Json);
            var events = Directory.GetFiles(Path.Combine(dir, "Events"), "*.json")
                .Select(f => JsonSerializer.Deserialize<EventDef>(File.ReadAllText(f), Json)).ToList();
            return (mission, events);
        }

        /// <summary>Neglect: never changes a facility; always takes the fallback choice (no requirement, no resource cost).</summary>
        static void Neglect(GameSession s)
        {
            var p = s.PendingEvent;
            if (p != null) { s.ChooseEventOption(p.instanceId, p.choices.First(IsFallback).id); return; }
            s.AdvanceSol(s.Snapshot.sol);
        }

        static bool IsFallback(ChoiceView c)
        {
            return c.requires.Count == 0 && !c.effects.Any(e => e.type == "resource" && e.amount < 0 && e.delaySols == 0);
        }

        /// <summary>
        /// Sensible: before each Advance, try every facility setting and keep the one whose forecast is best:
        /// highest Crew Health after the sol, then the largest smallest stock (crew needs and power), then total stock.
        /// Events: take the choice with the smallest visible Crew Health cost (health effects + radiation units x damage,
        /// counting the shelter's shielding if it is affordable) as long as every resource it costs stays at or above
        /// one sol of crew need (4) afterwards; otherwise the fallback.
        /// </summary>
        static void Sensible(GameSession s)
        {
            var c = s.Config;
            var p = s.PendingEvent;
            if (p != null)
            {
                var snap = s.Snapshot;
                int shield = c.facilities.SelectMany(f => f.levels).Select(l => l.shielding).DefaultIfEmpty(0).Max();
                ChoiceView best = null; int bestCost = int.MaxValue;
                foreach (var ch in p.choices.Where(x => x.available))
                {
                    bool keepsReserve = ch.effects.Where(e => e.type == "resource" && e.amount < 0)
                        .All(e => snap.Resource(e.target).amount + e.amount >= 4);
                    if (!keepsReserve && !IsFallback(ch)) continue;
                    int cost = 0;
                    foreach (var e in ch.effects)
                    {
                        if (e.type == "health") cost -= e.amount;
                        if (e.type == "radiation") cost += System.Math.Max(0, e.amount + c.radiationPerSol - shield) * c.radiationDamagePerUnit;
                        if (e.type == "modifier" && e.percent < 0) cost += -e.percent * e.durationSols / 20;
                        if (e.type == "resource" && e.amount < 0) cost += -e.amount / 2;
                    }
                    if (cost < bestCost) { bestCost = cost; best = ch; }
                }
                s.ChooseEventOption(p.instanceId, best.id);
                return;
            }
            var levels = c.facilities.Select(f => Enumerable.Range(0, f.levels.Count).ToArray()).ToList();
            int[] bestSet = null; long bestScore = long.MinValue;
            foreach (var combo in Combos(levels))
            {
                for (int i = 0; i < combo.Length; i++) s.SetFacilityLevel(c.facilities[i].id, combo[i]);
                var f = s.Forecast;
                int minStock = f.resources.Min(r => r.end);
                long score = (long)f.healthAfter * 1000000 + minStock * 1000 + f.resources.Sum(r => r.end);
                if (score > bestScore) { bestScore = score; bestSet = combo; }
            }
            for (int i = 0; i < bestSet.Length; i++) s.SetFacilityLevel(c.facilities[i].id, bestSet[i]);
            s.AdvanceSol(s.Snapshot.sol);
        }

        static IEnumerable<int[]> Combos(List<int[]> levels, int i = 0, int[] acc = null)
        {
            acc = acc ?? new int[levels.Count];
            if (i == levels.Count) { yield return (int[])acc.Clone(); yield break; }
            foreach (var l in levels[i]) { acc[i] = l; foreach (var r in Combos(levels, i + 1, acc)) yield return r; }
        }

        static int Wins(System.Action<GameSession> strategy)
        {
            var (mission, events) = Load();
            int wins = 0;
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var s = new GameSession(mission, events, seed);
                for (int step = 0; step < 200 && s.Snapshot.phase != MissionPhase.Ended; step++) strategy(s);
                if (s.Snapshot.outcome == MissionOutcome.Success) wins++;
            }
            return wins;
        }

        [Test]
        public void Sensible_WinsAtLeast90Percent_Neglect_AtMost50Percent()
        {
            int sensible = Wins(Sensible), neglect = Wins(Neglect);
            TestContext.WriteLine("BALANCE sensible=" + sensible + "/" + Seeds + " neglect=" + neglect + "/" + Seeds);
            Assert.GreaterOrEqual(sensible, Seeds * 90 / 100, "sensible");
            Assert.LessOrEqual(neglect, Seeds * 50 / 100, "neglect");
        }
    }
}
