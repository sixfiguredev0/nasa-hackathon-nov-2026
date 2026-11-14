using System.Collections.Generic;
using RedFrontier.Audio;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// Reads colony state (never writes it) and turns state changes into restrained audio:
    /// shortage / recovery alerts, one grouped cue for newly stalled buildings, a low-fitness warning,
    /// the colony machinery bed, and the life-support signal the music controller uses for tension.
    /// Every alert fires on an edge with debounce and cooldowns, never per frame.
    /// </summary>
    [RequireComponent(typeof(Colony))]
    internal sealed class OutpostAudio : MonoBehaviour
    {
        const float ShortageRepeat = 30f, RecoveryMinShort = 1.5f;
        const float StallConfirm = 1f, StallClear = 3f, StallAfterShortage = 4f;
        const float FitnessWarn = 0.2f, FitnessRearm = 0.3f;

        Colony colony;
        bool begun, wasShort, fitnessArmed = true;
        float shortSince, nextShortageRepeat, lastShortageAlert = -999f;
        readonly Dictionary<BuildingState, float> stalledSince = new Dictionary<BuildingState, float>();
        readonly Dictionary<BuildingState, float> clearSince = new Dictionary<BuildingState, float>();
        readonly HashSet<BuildingState> announced = new HashSet<BuildingState>();

        void Start()
        {
            colony = GetComponent<Colony>();
            GameAudio.EnterOutpost();
        }

        void Update()
        {
            if (colony == null || !colony.Ready) return;
            var eco = colony.economy;
            float now = Time.time;

            if (!begun)
            {
                begun = true;
                GameAudio.BeginGameplayMusic();
            }

            // Life support
            bool shortNow = eco.lifeSupportShort;
            GameAudio.SetShortage(shortNow);
            if (shortNow && !wasShort)
            {
                shortSince = now;
                Alert(Sfx.AlertShortage, now);
                nextShortageRepeat = now + ShortageRepeat;
            }
            else if (shortNow && now >= nextShortageRepeat)
            {
                Alert(Sfx.AlertShortage, now);
                nextShortageRepeat = now + ShortageRepeat;
            }
            else if (!shortNow && wasShort && now - shortSince >= RecoveryMinShort)
                GameAudio.Play(Sfx.AlertRecovered);
            wasShort = shortNow;

            // Stalls: a building counts once it has been stalled for a second; it re-arms after 3 s running.
            bool newStall = false;
            foreach (var b in eco.buildings)
            {
                if (b.stalled)
                {
                    clearSince.Remove(b);
                    if (!stalledSince.TryGetValue(b, out var since)) stalledSince[b] = now;
                    else if (now - since >= StallConfirm && announced.Add(b)) newStall = true;
                }
                else
                {
                    stalledSince.Remove(b);
                    if (!announced.Contains(b)) continue;
                    if (!clearSince.TryGetValue(b, out var clear)) clearSince[b] = now;
                    else if (now - clear >= StallClear) { announced.Remove(b); clearSince.Remove(b); }
                }
            }
            if (newStall && now - lastShortageAlert >= StallAfterShortage) GameAudio.Play(Sfx.AlertStall);

            // Fitness: warn once on entering the lowest gauge segment.
            if (fitnessArmed && eco.fitness < FitnessWarn)
            {
                fitnessArmed = false;
                GameAudio.Play(Sfx.AlertFitness);
            }
            else if (eco.fitness > FitnessRearm) fitnessArmed = true;

            // Machinery bed grows with the commissioned outpost (the Rocket alone stays quiet).
            GameAudio.SetAmbience(Sfx.AmbColonyHum, Mathf.Clamp01((eco.buildings.Count - 1) / 6f));
        }

        void Alert(string id, float now)
        {
            lastShortageAlert = now;
            GameAudio.Play(id);
        }
    }
}
