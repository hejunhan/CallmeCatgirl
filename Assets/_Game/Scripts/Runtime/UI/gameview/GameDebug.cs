using System;
using System.Linq;
using CallmeCatgirl.Debugging;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
namespace CallmeCatgirl.Gameplay { public sealed partial class CoreGameplayController {
// Add these members INSIDE the controller. This is a reviewed integration example, not an auto-imported script.
// Requires the task-12 training module, Lifecycle.patch, and the two Debug partial files.
// using CallmeCatgirl.Debugging;
// using System.Globalization;

public DebugCommandService DebugCommands { get; private set; }
private string debugSessionId;

private void BindDebugCommands()
{
    debugSessionId = Guid.NewGuid().ToString("N");
    // Bind once per controller, after Simulation/Training exist. Keep the same service through ResetSimulation
    // so local controls notice the new session ID and invalidate their selected object and drafts.
    if (DebugCommands == null)
        DebugCommands = new DebugCommandService(new DelegateDebugGameplayHost(CaptureDebugSnapshot, ApplyDebugCommand));
}

private DebugSnapshot CaptureDebugSnapshot()
{
    var session = TrainingEnabled ? training.Session : null;
    bool enabled = DebugBuild.Enabled && !GameEnded;
    var capabilities = DebugCapabilities.Calculation | DebugCapabilities.Quality;
    if (session != null) capabilities |= DebugCapabilities.Corpus | DebugCapabilities.Training;
    // Add Stage capability only after binding the actual phase owner; never substitute CurrentMonth or MemoryLevel.
    // if (phaseOwner != null) capabilities |= DebugCapabilities.Stage;
    var records = session == null ? Enumerable.Empty<DebugCorpusSnapshot>() : session.Corpus.Select(c =>
        new DebugCorpusSnapshot(c.Id, session.IsConsumed(c.Id), c.Scores.ToDictionary(p => p.Key, p => p.Value)));
    var requests = Simulation.Tasks.Values.Select(t => {
        bool isTraining = session != null && session.IsTrainingRequest(t.Id);
        bool completed = isTraining && session.TryGetJob(t.Id, out var job) && job.Completed;
        Quality? forced = null;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CATGIRL_DISABLE_DEBUG
        forced = Simulation.GetDebugQualityOverride(t.Id);
#endif
        return new DebugTaskSnapshot(t.Id, isTraining, completed, t.State, t.Progress, t.Definition.Calculation,
            t.Result?.Overall, forced);
    });
    return new DebugSnapshot(debugSessionId, enabled, capabilities, Simulation.Paused, Simulation.GameTime,
        records, requests, "progression", 1, 1);
    // Replace final three arguments with phaseOwner.StableId, phaseOwner.CurrentStage, phaseOwner.MaxStage.
}

private DebugMutation ApplyDebugCommand(DebugCommand command)
{
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CATGIRL_DISABLE_DEBUG
    // This remains the SAME event consumer. Flush preceding events before changing a Ready result.
    ReadEvents();
    if (GameEnded || DebugCommandService.Validate(CaptureDebugSnapshot(), command, out _) != DebugStatus.Applied)
        return DebugMutation.Reject("对象状态已变化");
    bool applied;
    switch (command.Action)
    {
        case DebugAction.SetCorpusScore:
        case DebugAction.AddCorpusScore:
            var record = training.Session.Corpus.First(c => c.Id == command.TargetId);
            double current = record.Scores.TryGetValue(command.Ability, out var score) ? score : 0;
            double next = command.Action == DebugAction.SetCorpusScore ? command.Amount : current + command.Amount;
            applied = training.Session.TryDebugSetCorpusScore(command.TargetId, command.Ability, next);
            break;
        case DebugAction.SetQualityOverride:
            applied = Simulation.TryDebugSetQualityOverride(command.TargetId, command.Quality);
            break;
        case DebugAction.FinishCalculation:
            applied = Simulation.TryDebugFinishCalculation(command.TargetId);
            break;
        case DebugAction.FinishTraining:
            // Check management policy BEFORE forcing calculation. A reject must not leave half a command applied.
            if (!training.CanManage) return DebugMutation.Reject("训练管理当前不可用");
            // Reuse the canonical completion command. It submits/closes once and registers the frozen model once.
            applied = training.Session.TryDebugCompleteTraining(command.TargetId) == TrainingResult.Success;
            break;
        case DebugAction.AdvanceStage:
            // Only after actual phase-owner binding. Reuse its ordinary transition path and record reason=Debug.
            // applied = phaseOwner.TryDebugAdvanceStage((int)command.Amount);
            return DebugMutation.Reject("阶段 API 尚未接入");
        default: return DebugMutation.Reject("未知命令");
    }
    ReadEvents(); // Training events go to existing training branch; normal requests settle only after normal Submit.
    canvasView?.Refresh();
    return applied ? DebugMutation.Success("当前会话调试") : DebugMutation.Reject("主系统拒绝操作");
#else
    return DebugMutation.Reject("发行环境不启用调试");
#endif
}


} }