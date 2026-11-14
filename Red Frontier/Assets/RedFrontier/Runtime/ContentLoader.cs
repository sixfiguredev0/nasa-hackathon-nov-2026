using System;
using System.Collections.Generic;
using RedFrontier.Core;
using UnityEngine;

namespace RedFrontier.Runtime
{
    /// <summary>
    /// Reads mission and event JSON with JsonUtility. Resources.Load is synchronous and works in Web builds.
    /// Validation is left to GameSession (it throws ContentValidationException), so there is one rule set.
    /// </summary>
    public static class ContentLoader
    {
        /// <summary>Resources path of the mission file: Content/Resources/mission.json.</summary>
        public const string MissionPath = "mission";
        /// <summary>Resources folder holding one JSON file per event: Content/Resources/Events/&lt;id&gt;.json.</summary>
        public const string EventsFolder = "Events";

        public static void LoadFromResources(out MissionConfig mission, out List<EventDef> events)
        {
            var missionAsset = Resources.Load<TextAsset>(MissionPath);
            if (missionAsset == null) throw new InvalidOperationException("Missing Resources/" + MissionPath + ".json");
            mission = ParseMission(missionAsset.text, missionAsset.name);

            var files = new List<KeyValuePair<string, string>>();
            foreach (var a in Resources.LoadAll<TextAsset>(EventsFolder))
                files.Add(new KeyValuePair<string, string>(a.name, a.text));
            events = ParseEvents(files);
        }

        public static MissionConfig ParseMission(string json, string name)
        {
            return Parse<MissionConfig>(json, name);
        }

        /// <summary>
        /// Files are parsed in ordinal name order, because Resources.LoadAll order is not guaranteed.
        /// (GameSession also sorts events by id; this keeps error messages stable too.)
        /// </summary>
        public static List<EventDef> ParseEvents(IEnumerable<KeyValuePair<string, string>> files)
        {
            var sorted = new List<KeyValuePair<string, string>>(files);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var events = new List<EventDef>(sorted.Count);
            foreach (var f in sorted) events.Add(Parse<EventDef>(f.Value, f.Key));
            return events;
        }

        static T Parse<T>(string json, string name) where T : class
        {
            T value;
            try { value = JsonUtility.FromJson<T>(json); }
            catch (ArgumentException e) { throw new InvalidOperationException("Content file '" + name + "' is not valid JSON: " + e.Message, e); }
            if (value == null) throw new InvalidOperationException("Content file '" + name + "' is empty");
            return value;
        }
    }
}
