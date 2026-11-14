using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RedFrontier.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace RedFrontier.Runtime.Tests
{
    /// <summary>
    /// SimulationHost and DebugHud lifecycle: one session, no duplicate subscriptions across disable/enable and
    /// restart, a second host refused, and a clean re-create after destroy. A real script reload cannot run
    /// inside a test (it tears down the runner); that path was checked by hand (see HANDOFF).
    /// </summary>
    public class HostLifecycleTests
    {
        GameObject go;

        [TearDown]
        public void TearDown()
        {
            foreach (var h in UnityEngine.Object.FindObjectsOfType<SimulationHost>(true)) UnityEngine.Object.Destroy(h.gameObject);
        }

        static GameObject CreateHostWithHud()
        {
            var g = new GameObject("TestHost");
            g.SetActive(false);
            g.AddComponent<SimulationHost>();
            g.AddComponent<DebugHud>();
            g.SetActive(true); // scene-like activation: execution order runs the host first
            return g;
        }

        static int Subscribers(GameSession s)
        {
            var field = typeof(GameSession).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic);
            var d = (Delegate)field.GetValue(s);
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        [UnityTest]
        public IEnumerator Host_OwnsOneSession_HudRefreshesOncePerAcceptedCommand()
        {
            go = CreateHostWithHud();
            yield return null;
            var host = go.GetComponent<SimulationHost>();
            var hud = go.GetComponent<DebugHud>();
            Assert.IsNotNull(host.Session, host.LoadError);
            Assert.AreEqual("", host.LoadError);
            Assert.AreEqual(2, Subscribers(host.Session), "host + HUD");

            int before = hud.RefreshCount;
            Assert.IsFalse(host.Session.SetFacilityLevel("life_support", 99).ok);
            Assert.AreEqual(before, hud.RefreshCount, "rejected command: no refresh");
            Assert.IsTrue(host.Session.SetFacilityLevel("life_support", 1).ok);
            Assert.AreEqual(before + 1, hud.RefreshCount);
        }

        [UnityTest]
        public IEnumerator DisableEnable_HostAndHud_KeepsSessionAndSubscriptionCount()
        {
            go = CreateHostWithHud();
            yield return null;
            var host = go.GetComponent<SimulationHost>();
            var hud = go.GetComponent<DebugHud>();
            var session = host.Session;
            Assert.IsTrue(session.AdvanceSol(1).ok);
            int sol = session.Snapshot.sol;

            for (int i = 0; i < 3; i++)
            {
                hud.enabled = false;
                host.enabled = false;
                yield return null;
                host.enabled = true;
                hud.enabled = true;
                yield return null;
                go.SetActive(false);
                go.SetActive(true);
                yield return null;
            }
            Assert.AreSame(session, host.Session, "no new session on re-enable");
            Assert.AreEqual(sol, host.Session.Snapshot.sol, "progress kept");
            Assert.AreEqual(2, Subscribers(session));

            int before = hud.RefreshCount;
            Assert.IsTrue(session.Restart(5).ok);
            Assert.IsTrue(session.Restart().ok);
            Assert.AreEqual(before + 2, hud.RefreshCount);
            Assert.AreEqual(2, Subscribers(session), "restart keeps the same subscriptions");
            Assert.AreEqual(5, session.Seed);
        }

        [UnityTest]
        public IEnumerator SecondHost_IsRefusedAndDisabled()
        {
            go = CreateHostWithHud();
            yield return null;
            var first = go.GetComponent<SimulationHost>();
            LogAssert.Expect(LogType.Error, new Regex("second SimulationHost"));
            var other = new GameObject("SecondHost").AddComponent<SimulationHost>();
            yield return null;
            Assert.IsFalse(other.enabled);
            Assert.IsNull(other.Session);
            Assert.IsNotNull(first.Session);
            Assert.AreEqual(2, Subscribers(first.Session));
        }

        [UnityTest]
        public IEnumerator DestroyThenCreate_NewHostGetsAFreshSession()
        {
            go = CreateHostWithHud();
            yield return null;
            var oldSession = go.GetComponent<SimulationHost>().Session;
            UnityEngine.Object.Destroy(go);
            yield return null;
            Assert.AreEqual(0, Subscribers(oldSession), "destroyed host and HUD unsubscribed");

            go = CreateHostWithHud();
            yield return null;
            var host = go.GetComponent<SimulationHost>();
            Assert.IsTrue(host.enabled, "the static owner slot was released");
            Assert.IsNotNull(host.Session);
            Assert.AreNotSame(oldSession, host.Session);
            Assert.AreEqual(2, Subscribers(host.Session));
        }
    }
}
