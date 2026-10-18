using System.Collections.Generic;
using NUnit.Framework;
using CallmeCatgirl.UI.SystemMenu;

namespace CallmeCatgirl.Tests.SystemMenu
{
    public sealed class SystemMenuSessionTests
    {
        // Given an input/modal cancellation at the top of the game stack,
        // when Escape is pressed, then that action consumes Escape before the pause menu can open.
        [Test] public void HostInputAndModalConsumeEscapeBeforeTheDrawerOpens()
        {
            int calls = 0; bool consume = true; var claims = new List<bool>();
            var menu = new SystemMenuSession(true, claims.Add, () => { calls++; return consume; });
            Assert.That(menu.HandleEscape(), Is.True); Assert.That(menu.IsPauseOpen, Is.False); Assert.That(claims, Is.Empty);
            consume = false; menu.HandleEscape(); Assert.That(menu.IsPauseOpen, Is.True); Assert.That(calls, Is.EqualTo(2)); CollectionAssert.AreEqual(new[] { true }, claims);
        }

        [Test] public void SettingsAndDrawerShareOneClaimAndClosingOnlyReleasesThatClaim()
        {
            var claims = new List<bool>();
            var menu = new SystemMenuSession(true, claims.Add); menu.OpenPause(); menu.OpenSettings(); menu.HandleEscape();
            Assert.That(menu.IsPauseOpen, Is.True); Assert.That(menu.IsSettingsOpen, Is.False); CollectionAssert.AreEqual(new[] { true }, claims);
            menu.HandleEscape(); menu.CloseAll(); CollectionAssert.AreEqual(new[] { true, false }, claims);
        }

        [Test] public void SaveFailureRetainsPauseAndMessageWhileSuccessClosesItExactlyOnce()
        {
            int attempts = 0; bool success = false; var claims = new List<bool>();
            var menu = new SystemMenuSession(true, claims.Add, null, () => { attempts++; return success ? MenuSaveResult.Success : MenuSaveResult.Failure("磁盘空间不足，存档仍留在当前会话。"); });
            menu.OpenPause(); Assert.That(menu.SaveAndReturn(), Is.False); Assert.That(menu.SaveError, Does.Contain("磁盘空间不足")); Assert.That(menu.IsPauseClaimed, Is.True);
            success = true; Assert.That(menu.SaveAndReturn(), Is.True); Assert.That(menu.SaveAndReturn(), Is.False); Assert.That(attempts, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { true, false }, claims);
        }

        [Test] public void MainMenuUsesTheSameSettingsWithoutClaimingGameplayPause()
        {
            var claims = new List<bool>(); var menu = new SystemMenuSession(false, claims.Add);
            Assert.That(menu.HandleEscape(), Is.False); menu.OpenSettings(); Assert.That(menu.IsBlockingInput, Is.True); Assert.That(menu.IsPauseClaimed, Is.False);
            menu.HandleEscape(); Assert.That(menu.IsBlockingInput, Is.False); Assert.That(claims, Is.Empty);
        }
    }
}
