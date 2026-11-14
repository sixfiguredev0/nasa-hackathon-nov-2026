using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RedFrontier.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace RedFrontier.Runtime.Tests
{
    /// <summary>DebugHud panel mode (integration scene): hiding keeps the session; events and outcomes reopen it.</summary>
    public class PanelModeTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var h in UnityEngine.Object.FindObjectsOfType<SimulationHost>(true)) UnityEngine.Object.Destroy(h.gameObject);
        }

        static int Subscribers(GameSession s)
        {
            var d = (Delegate)typeof(GameSession).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        [UnityTest]
        public IEnumerator PanelToggle_PreservesSession_AndReopensForEventsAndOutcome()
        {
            var g = new GameObject("PanelHost");
            g.SetActive(false);
            var host = g.AddComponent<SimulationHost>();
            var hud = g.AddComponent<DebugHud>();
            hud.PanelMode = true;
            g.SetActive(true);
            yield return null;

            var s = host.Session;
            Assert.IsTrue(hud.PanelOpen, "opens initially");
            Assert.IsTrue(hud.BlocksMapInput);
            for (int i = 0; i < 4; i++) { hud.SetPanelOpen(false); yield return null; hud.SetPanelOpen(true); yield return null; }
            Assert.AreSame(s, host.Session);
            Assert.AreEqual(2, Subscribers(s), "toggling never re-subscribes");

            hud.SetPanelOpen(false);
            Assert.IsFalse(hud.BlocksMapInput);
            int guard = 0;
            while (s.Snapshot.phase != MissionPhase.Ended && guard++ < 100)
            {
                if (s.PendingEvent != null)
                {
                    Assert.IsTrue(hud.PanelOpen, "pending event reopens the panel");
                    hud.SetPanelOpen(false); // the player may still close it while the event is pending
                    Assert.IsFalse(hud.PanelOpen);
                    var p = s.PendingEvent;
                    foreach (var c in p.choices) if (c.available) { Assert.IsTrue(s.ChooseEventOption(p.instanceId, c.id).ok); break; }
                }
                else Assert.IsTrue(s.AdvanceSol(s.Snapshot.sol).ok);
                if (s.Snapshot.phase != MissionPhase.Ended && s.PendingEvent == null) hud.SetPanelOpen(false);
            }
            Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
            Assert.IsTrue(hud.PanelOpen, "outcome reopens the panel");
            Assert.IsTrue(s.Restart().ok);
            Assert.AreEqual(2, Subscribers(s));
        }
    }
}
