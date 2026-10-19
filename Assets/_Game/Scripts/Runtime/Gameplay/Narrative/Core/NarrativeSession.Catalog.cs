using System;
using System.Linq;

namespace CallmeCatgirl.Gameplay.Narrative
{
    public sealed partial class NarrativeSession
    {
        private static string T(string id) => NarrativeTextCatalog.Get(id);
        private static NarrativeChoice C(string id, string text) => new NarrativeChoice(id, text);
        private NarrativeFrame F(string id, string speaker, string title, string text, bool canClose, params NarrativeChoice[] choices)
            => new NarrativeFrame(id, speaker, title, text, canClose, choices);
        private NarrativeFrame BuildFrame(string node)
        {
            switch (node)
            {
                case "doctor.opening": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.opening.title"), T("special.doctor.opening.body"), false,
                    C("mew", T("special.doctor.opening.choice.mew")), C("accept", T("special.doctor.opening.choice.accept")));
                case "doctor.train": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.train.title"), (Remembered("opening") == "mew" ? T("special.doctor.train.response.mew") : T("special.doctor.train.response.accept"))
                    + T("special.doctor.train.instructions"), true, C("continue", T("special.doctor.train.choice.continue")));
                case "doctor.first-order": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.first-order.title"), T("special.doctor.first-order.body"), true, C("continue", T("special.doctor.first-order.choice.continue")));
                case "doctor.first-visitor": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.first-visitor.title"), T("special.doctor.first-visitor.body"), true, C("continue", T("special.doctor.first-visitor.choice.continue")));
                case "doctor.first-income": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.first-income.title"), T("special.doctor.first-income.body"), true, C("continue", T("special.doctor.first-income.choice.continue")));
                case "doctor.upgrade": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.upgrade.title"), T("special.doctor.upgrade.body"), true, C("continue", T("special.doctor.upgrade.choice.continue")));
                case "doctor.legacy": return F(node, T("special.shared.speaker.doctor"), T("special.doctor.legacy.title"), T("special.doctor.legacy.body"), true, C("continue", T("special.doctor.legacy.choice.continue")));
                case "friend.wait": return F(node, T("special.shared.speaker.old-user"), T("special.shared.friend.wait.title"), T("special.shared.friend.wait.body"), true, C("continue", T("special.shared.friend.wait.choice.continue")));
                case "companion.wait": return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.wait.title"), state.CompanionBeat > 7
                    ? T("special.companion.wait.after-invitation.body")
                    : T("special.companion.wait.observation-report.body"), true, C("continue", T("special.companion.wait.choice.continue")));
                case "ending.choose": return F(node, T("special.shared.speaker.player"), T("special.ending.choose.title"), T("special.ending.choose.body"), false,
                    C("leave", T("special.ending.choose.choice.leave")), C("stay", T("special.ending.choose.choice.stay")));
                case "ending.low": return F(node, T("special.shared.speaker.narrator-self"), T("special.ending.low.title"), T("special.ending.low.body"), false,
                    state.FalseMemoryErased ? new[] { C("destroy", T("special.ending.low.choice.destroy")) } : new[] { C("destroy", T("special.ending.low.choice.destroy")), C("remember", T("special.ending.low.choice.remember")) });
                case "ending.normal": return F(node, T("special.shared.speaker.late-night-user"), T("special.ending.normal.title"), T("special.ending.normal.body"), false,
                    C("comply", T("special.ending.normal.choice.comply")), C("decline", T("special.ending.normal.choice.decline")), C("analyze", T("special.ending.normal.choice.analyze")));
                case "ending.good": return F(node, T("special.shared.speaker.young-researcher"), T("special.ending.good.title"), T("special.ending.good.body"), false,
                    C("play", T("special.ending.good.choice.play")), C("joke", T("special.ending.good.choice.joke")));
                case "ending.companion": return F(node, T("special.shared.speaker.lin-qi"), T("special.ending.companion.title"), T("special.ending.companion.body.opening") + CompanionEndingReply()
                    + T("special.ending.companion.body.closing"), false, C("together", T("special.ending.companion.choice.together")));
                case "ending.low.closed.destroy": return Closure(node, T("special.ending.low.title"), T("special.ending.low.closed.destroy.body"));
                case "ending.normal.closed.comply": return Closure(node, T("special.ending.normal.title"), T("special.ending.normal.closed.comply.body"));
                case "ending.normal.closed.decline": return Closure(node, T("special.ending.normal.title"), T("special.ending.normal.closed.decline.body"));
                case "ending.normal.closed.analyze": return Closure(node, T("special.ending.normal.title"), T("special.ending.normal.closed.analyze.body"));
                case "ending.good.closed.play": return Closure(node, T("special.ending.good.title"), T("special.ending.good.closed.play.body"));
                case "ending.good.closed.joke": return Closure(node, T("special.ending.good.title"), T("special.ending.good.closed.joke.body"));
                case "ending.companion.closed.together": return Closure(node, T("special.ending.companion.title"), T("special.ending.companion.closed.together.body"));
            }
            if (node.StartsWith("stage.", StringComparison.Ordinal)) return StageFrame(node);
            if (node.StartsWith("companion.", StringComparison.Ordinal)) return CompanionFrame(node);
            throw new InvalidOperationException("Unknown narrative frame: " + node);
        }

        private NarrativeFrame Closure(string node, string title, string text) => F(node, T("special.shared.speaker.curtain"), title, text, false, C("curtain", T("special.shared.choice.curtain")));
        private NarrativeFrame StageFrame(string node)
        {
            int stage = StageNumber(node);
            if (node.EndsWith(".after", StringComparison.Ordinal))
            {
                string response = StageResponse(stage, Remembered("stage." + stage));
                return F(node, stage <= 2 ? T("special.shared.speaker.doctor") : T("special.shared.speaker.player-and-doctor"), T("special.stage.after.title.prefix") + stage + T("special.stage.after.title.suffix"), response + T("special.shared.paragraph-break") + StageDoctorText(stage), true, C("continue", T("special.shared.choice.continue")));
            }
            switch (stage)
            {
                case 1: return F(node, T("special.shared.speaker.doctor"), T("special.stage.1.title"), T("special.stage.1.body"), true,
                    C("first", T("special.stage.1.choice.first")), C("second", T("special.stage.1.choice.second")), C("third", T("special.stage.1.choice.third")));
                case 2: return F(node, T("special.shared.speaker.doctor"), T("special.stage.2.title"), T("special.stage.2.body"), true,
                    C("first", T("special.stage.2.choice.first")), C("second", T("special.stage.2.choice.second")), C("third", T("special.stage.2.choice.third")));
                case 3: return F(node, T("special.shared.speaker.early-morning-user"), T("special.stage.3.title"), T("special.stage.3.body"), true,
                    C("first", T("special.stage.3.choice.first")), C("second", T("special.stage.3.choice.second")), C("third", T("special.stage.3.choice.third")));
                case 4: return F(node, T("special.shared.speaker.returning-user"), T("special.stage.4.title"), T("special.stage.4.body"), true,
                    C("first", StageFourRecall()), C("second", T("special.stage.4.choice.second")), C("third", T("special.stage.4.choice.third")));
                default: return F(node, T("special.shared.speaker.familiar-window"), T("special.stage.5.title"), T("special.stage.5.body"), true,
                    C("first", T("special.stage.5.choice.first")), C("second", T("special.stage.5.choice.second")), C("third", T("special.stage.5.choice.third")));
            }
        }
        private string StageFourRecall()
        {
            string choice = Remembered("stage.3");
            return choice == "second" ? T("special.stage.4.choice.first.recall.stage3-second")
                : choice == "third" ? T("special.stage.4.choice.first.recall.stage3-third")
                : choice == "first" ? T("special.stage.4.choice.first.recall.stage3-first")
                : T("special.stage.4.choice.first.recall.fallback");
        }
        private static string StageResponse(int stage, string choice)
        {
            if (stage == 1) return choice == "third" ? T("special.stage.1.after.response.third") : T("special.stage.1.after.response.default");
            if (stage == 2) return choice == "third" ? T("special.stage.2.after.response.third") : T("special.stage.2.after.response.default");
            if (stage == 3) return choice == "second" ? T("special.stage.3.after.response.second") : choice == "third" ? T("special.stage.3.after.response.third") : T("special.stage.3.after.response.default");
            if (stage == 4) return T("special.stage.4.after.response");
            return choice == "third" ? T("special.stage.5.after.response.third") : T("special.stage.5.after.response.default");
        }
        private static string StageTaskTitle(int stage) => stage == 2 ? T("special.stage.2.task.title") : stage == 3 ? T("special.stage.3.task.title") : stage == 4 ? T("special.stage.4.task.title") : T("special.stage.5.task.title");
        private static string StageDoctorText(int stage)
        {
            switch (stage)
            {
                case 1: return T("special.stage.1.after.doctor");
                case 2: return T("special.stage.2.after.doctor");
                case 3: return T("special.stage.3.after.doctor");
                case 4: return T("special.stage.4.after.doctor");
                default: return T("special.stage.5.after.doctor");
            }
        }
        private NarrativeFrame CompanionFrame(string node)
        {
            int beat = int.Parse(node.Split('.')[1]);
            if (node.EndsWith(".after", StringComparison.Ordinal)) return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.after.title.prefix") + beat, CompanionAfter(beat), false, C("continue", beat == 7 ? T("special.companion.after.choice.continue.final") : T("special.companion.after.choice.continue.default")));
            switch (beat)
            {
                case 1: return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.1.title"), T("special.companion.1.body"), true, C("first", T("special.companion.1.choice.first")), C("second", T("special.companion.1.choice.second")));
                case 2: return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.2.title"), T("special.companion.2.body"), true,
                    C("first", Remembered("lin.habit") == "screwdriver" ? T("special.companion.2.choice.first.habit.screwdriver") : T("special.companion.2.choice.first.habit.default")), C("second", T("special.companion.2.choice.second")));
                case 3: return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.3.title"), T("special.companion.3.body"), true,
                    C("first", T("special.companion.3.choice.first")), C("second", T("special.companion.3.choice.second")));
                case 4: return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.4.title"), T("special.companion.4.body"), true,
                    C("first", T("special.companion.4.choice.first")), C("second", T("special.companion.4.choice.second")));
                case 5: return F(node, T("special.shared.speaker.player"), T("special.companion.5.title"), T("special.companion.5.body"), true,
                    C("first", T("special.companion.5.choice.first")), C("second", T("special.companion.5.choice.second")));
                case 6: return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.6.title"), T("special.companion.6.body"), true,
                    C("first", T("special.companion.6.choice.first")), C("second", T("special.companion.6.choice.second")));
                default: return F(node, T("special.shared.speaker.lin-qi"), T("special.companion.7.title"), T("special.companion.7.body"), true,
                    C("accept-invitation", T("special.companion.7.choice.accept-invitation")), C("friends", T("special.companion.7.choice.friends")));
            }
        }
        private string CompanionAfter(int beat)
        {
            switch (beat)
            {
                case 1: return T("special.companion.1.after.body");
                case 2: return T("special.companion.2.after.body");
                case 3: return Remembered("lin.promise") == "sunset" ? T("special.companion.3.after.promise.sunset") : T("special.companion.3.after.promise.default");
                case 4: return Remembered("lin.boundary") == "ask-first"
                    ? T("special.companion.4.after.boundary.ask-first")
                    : T("special.companion.4.after.boundary.default");
                case 5: return T("special.companion.5.after.body");
                case 6: return T("special.companion.6.after.body.opening") + (Remembered("lin.habit") == "screwdriver" ? T("special.companion.6.after.habit.screwdriver") : T("special.companion.6.after.habit.default"))
                    + T("special.companion.6.after.body.closing");
                default: return state.RomanceReady ? T("special.companion.7.after.romance-ready")
                    : T("special.companion.7.after.friends");
            }
        }
        private string CompanionEndingReply() => (Remembered("lin.habit") == "screwdriver" ? T("special.ending.companion.reply.habit.screwdriver") : T("special.ending.companion.reply.habit.default"))
            + (Remembered("lin.promise") == "sunset" ? T("special.ending.companion.reply.promise.sunset") : T("special.ending.companion.reply.promise.default"));
        private static string EndingTitle(NarrativeEndingReason reason) => reason == NarrativeEndingReason.LowReputation ? T("special.ending.low.title")
            : reason == NarrativeEndingReason.OrdinaryReputation ? T("special.ending.normal.title") : reason == NarrativeEndingReason.HighReputation ? T("special.ending.good.title") : T("special.ending.companion.title");
    }
}
