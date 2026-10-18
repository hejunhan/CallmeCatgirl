using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    internal static class MemoryUpgradeTestActions
    {
        public static void Skip(MonoBehaviour owner)
        {
            Assert.That((bool)owner.GetType().GetProperty("IsMemoryInstallOpen").GetValue(owner), Is.True);
            var game = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(o => o != null && o.GetType().Name == "StandaloneMemoryInstall");
            game.GetType().GetMethod("SkipInstall").Invoke(game, null);
            Assert.That((bool)owner.GetType().GetProperty("IsMemoryInstallOpen").GetValue(owner), Is.False);
        }
    }
}
