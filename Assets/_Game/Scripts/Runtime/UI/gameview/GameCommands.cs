using System;
using System.Linq;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    [Serializable] public sealed class GameCommand
    {
        public string kind, id, recipe, architecture, displayName;
        public string[] corpusIds;
        public string conversationId;
        public double corpusPoints;
        public CallmeCatgirl.Gameplay.Training.ConversationInvestment[] investments;
        public int x, y, rotation, choice, storyRevision = -1;
        public long expectedVersion = -1;
        public double seconds;
        public bool paused;
    }
    [Serializable] public sealed class GameCommandResult
    {
        public bool ok;
        public string code, message;
        public GameSnapshot snapshot;
    }
    [Serializable] public sealed class GameSnapshot
    {
        public long version;
        public double gameTime, tokenPerSecond, monthRemaining, dayRemaining, secondsPerDay;
        public bool paused, modal, ended;
        public int funds, month, day, daysPerMonth, monthlyMaintenanceFee, stage, completed, storyRevision;
        public string selectedId, activeModelId, storyRequestId;
        public string[] choices, corpusIds;
        public int boardWidth,boardHeight;
        public GameCellSnapshot[] openCells;
        public string[] recipes, architectures;
        public GameRecipeSnapshot[] recipeDetails;
        public GameCorpusSnapshot[] corpus;
        public GameArchitectureSnapshot[] architectureDetails;
        public GameTaskSnapshot[] tasks;
        public GameModelSnapshot[] models;
    }
    [Serializable] public sealed class GameTaskSnapshot
    {
        public string id, state, quality, conversationId, architectureId;
        public double architectureSpeedMultiplier, corpusMultiplier;
        public int x, y, rotation;
        public double progress, calculation, tokens, patience, remaining, deadline;
        public GameCellSnapshot[] shape;
        public GameAbilitySnapshot[] requirements,produced;
        public bool training, readyInCandidate;
    }
    [Serializable] public sealed class GameModelSnapshot { public string id, name, architecture; public double interval,tokenPerSecond; public GameAbilitySnapshot[] abilities; }
    [Serializable] public sealed class GameArchitectureSnapshot { public string id,title,outline; public int unlockStage; public double speedMultiplier,corpusMultiplier; public bool contentReady; }
    [Serializable] public sealed class GameCellSnapshot { public int x,y,compartment; }
    [Serializable] public sealed class GameAbilitySnapshot { public string kind; public double value,excellent; }
    [Serializable] public sealed class GameRecipeSnapshot { public string id; public int cost,minimumRecords; public double workTokens,pointsPerExtraCell,maximumCorpusPoints,workGrowthCoefficient,workGrowthExponent; public GameCellSnapshot[] shape; }
    [Serializable] public sealed class GameCorpusSnapshot { public string id,conversationId,requestId,roundId,nodeId,choiceId; public bool consumed; public double remainingPoints; public GameAbilitySnapshot[] scores; }

    public sealed partial class CoreGameplayController
    {
        // Only observation metadata is retained here. Simulation/Economy/Story/Training remain the state owners.
        private long commandVersion;
        private string observedState;
        public bool ManualCommandClock { get; private set; }
        public GameSnapshot QueryGame()
        {
            var s = new GameSnapshot {
                gameTime = Simulation.GameTime, paused = Simulation.Paused, modal = IsModalOpen, ended = GameEnded,
                funds = Funds, month = CurrentMonth, stage = MemoryLevel, completed = CompletedTasks,
                monthRemaining = MonthTimeRemaining, day=CurrentDay, daysPerMonth=DaysPerMonth, dayRemaining=DayTimeRemaining, secondsPerDay=SecondsPerDay, monthlyMaintenanceFee=MonthlyMaintenanceFee, tokenPerSecond = Simulation.TokenThroughput, selectedId = SelectedId,
                storyRevision = Story.Revision, storyRequestId = Story.RequestId,
                choices = Story.Choices.Select(c => c.Label).ToArray(),
                activeModelId = TrainingEnabled ? training.Session.ActiveModelId : Simulation.Model.Id,
                boardWidth=Simulation.Width,boardHeight=Simulation.Height,
                openCells=Enumerable.Range(0,Simulation.Width*Simulation.Height).Select(i=>new Cell(i%Simulation.Width,i/Simulation.Width))
                    .Where(Simulation.IsOpen).Select(cell=>new GameCellSnapshot {x=cell.X,y=cell.Y,compartment=Simulation.CompartmentAt(cell)}).ToArray(),
                recipes=TrainingEnabled?training.Session.Recipes.Select(r=>r.Id).ToArray():Array.Empty<string>(),
                architectures=TrainingEnabled?training.Session.Architectures.Where(a=>a.UnlockStage<=MemoryLevel && a.ContentReady).Select(a=>a.Id).ToArray():Array.Empty<string>(),
                architectureDetails=TrainingEnabled?training.Session.Architectures.Select(a=>new GameArchitectureSnapshot{id=a.Id,title=a.Title,outline=a.Outline.ToString(),unlockStage=a.UnlockStage,speedMultiplier=a.SpeedMultiplier,corpusMultiplier=a.CorpusMultiplier,contentReady=a.ContentReady}).ToArray():Array.Empty<GameArchitectureSnapshot>(),
                recipeDetails=TrainingEnabled?training.Session.Recipes.Select(r=>new GameRecipeSnapshot {id=r.Id,cost=r.Cost,minimumRecords=r.MinimumRecords,workTokens=r.WorkTokens,pointsPerExtraCell=r.PointsPerExtraCell,maximumCorpusPoints=r.MaximumCorpusPoints,workGrowthCoefficient=r.WorkGrowthCoefficient,workGrowthExponent=r.WorkGrowthExponent,shape=r.Shape.Select(cell=>new GameCellSnapshot{x=cell.X,y=cell.Y}).ToArray()}).ToArray():Array.Empty<GameRecipeSnapshot>(),
                corpus=TrainingEnabled?training.Session.Corpus.Select(r=>new GameCorpusSnapshot {id=r.Id,conversationId=r.ConversationId,requestId=r.RequestId,roundId=r.RoundId,nodeId=r.NodeId,choiceId=r.ChoiceId,consumed=training.Session.IsConsumed(r.Id),remainingPoints=training.Session.RemainingPoints(r.Id),scores=r.Scores.Select(p=>new GameAbilitySnapshot{kind=p.Key.ToString(),value=p.Value}).ToArray()}).ToArray():Array.Empty<GameCorpusSnapshot>(),
                corpusIds = TrainingEnabled ? training.Session.Corpus.Where(c => !training.Session.IsConsumed(c.Id)).Select(c => c.Id).ToArray() : Array.Empty<string>(),
                models = TrainingEnabled ? training.Session.Models.Select(m => new GameModelSnapshot {id=m.Id,name=m.Name,architecture=m.ArchitectureId,
                    interval=m.Definition.Interval,tokenPerSecond=training.Session.PreviewTokensPerSecond(m.Id),abilities=m.Definition.Points.Select(p=>new GameAbilitySnapshot{kind=p.Key.ToString(),value=p.Value}).ToArray()}).ToArray() : Array.Empty<GameModelSnapshot>(),
                tasks = Simulation.Tasks.Values.Select(t => new GameTaskSnapshot {id=t.Id,conversationId=t.ConversationId,state=t.State.ToString(),architectureId=t.Architecture?.Id,architectureSpeedMultiplier=(t.Architecture??Simulation.Architecture).SpeedMultiplier,corpusMultiplier=(t.Architecture??Simulation.Architecture).CorpusMultiplier,
                    x=t.Origin.X,y=t.Origin.Y,rotation=t.Rotation,progress=t.Progress,calculation=t.Definition.Calculation,
                    tokens=t.Definition.Tokens,patience=t.Definition.Patience,quality=t.Result?.Overall.ToString(),
                    remaining=t.Remaining(Simulation.GameTime),deadline=t.Deadline,
                    shape=t.Definition.Shape.Select(cell=>new GameCellSnapshot{x=cell.X,y=cell.Y}).ToArray(),
                    requirements=t.Definition.Requirements.Select(r=>new GameAbilitySnapshot{kind=r.Kind.ToString(),value=r.Minimum,excellent=r.Excellent}).ToArray(),
                    produced=t.Abilities.Select(p=>new GameAbilitySnapshot{kind=p.Key.ToString(),value=p.Value}).ToArray(),
                    readyInCandidate=t.ReadyInCandidate,training=TrainingEnabled && training.IsTrainingRequest(t.Id)}).ToArray()
            };
            // Continuous clock/progress changes are returned but do not invalidate discrete commands every frame.
            string fingerprint = s.funds+"|"+s.month+"|"+s.stage+"|"+s.paused+"|"+s.modal+"|"+s.ended+"|"+s.storyRevision+"|"+s.activeModelId+"|"+s.tokenPerSecond
                +string.Join(";",s.tasks.Select(t=>t.id+":"+t.state+":"+t.x+":"+t.y+":"+t.rotation+":"+t.readyInCandidate+":"+t.quality))
                +string.Join(";",s.models.Select(m=>m.id+":"+m.name))+string.Join(";",s.corpus.Select(r=>r.id+":"+r.remainingPoints.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            if (fingerprint != observedState) { observedState = fingerprint; commandVersion++; }
            s.version = commandVersion; return s;
        }
        public string QueryGameJson() => JsonUtility.ToJson(QueryGame());
        public string PreviewPlacement(string id,int x,int y,int rotation)
            => Simulation.Preview(id,new Cell(x,y),rotation).ToString();
        public string ExecuteCommandJson(string json)
        {
            GameCommand command;
            try { command = JsonUtility.FromJson<GameCommand>(json); }
            catch (ArgumentException) { return JsonUtility.ToJson(CommandResult(false,"InvalidInput")); }
            return JsonUtility.ToJson(ExecuteCommand(command));
        }
        private GameCommandResult CommandResult(bool ok, string code)
            => new GameCommandResult {ok=ok,code=code,message=Message,snapshot=QueryGame()};
        public bool TryPlaceRequest(string id, Cell origin, int rotation, out PlacementFailure failure)
        {
            failure = PlacementFailure.InvalidState;
            if (GameEnded || IsModalOpen) return false;
            bool ok = Simulation.TryPlace(id,origin,rotation,out failure);
            ReadEvents(); canvasView?.Refresh(); return ok;
        }
        public GameCommandResult ExecuteCommand(GameCommand c)
        {
            if (c == null || string.IsNullOrEmpty(c.kind)) return CommandResult(false,"InvalidInput");
            var before = QueryGame();
            if (c.expectedVersion >= 0 && c.expectedVersion != before.version) return CommandResult(false,"StaleVersion");
            if (c.kind == "query") return CommandResult(true,"Success");
            bool savingCommand=c.kind=="save" || c.kind=="restore";
            if (GameEnded && !savingCommand) return CommandResult(false,"GameEnded");
            bool storyCommand = c.kind == "reply" || c.kind == "closeHistory";
            bool trainingCommand=c.kind=="train" || c.kind=="selectModel" || c.kind=="renameModel" || c.kind=="upgradeCompute"
                || (c.kind=="submit" && TrainingEnabled && training.IsTrainingRequest(c.id));
            if (IsModalOpen && !storyCommand && !savingCommand && !(trainingCommand && TrainingEnabled && training.CanManage)) return CommandResult(false,"ModalOpen");
            TaskInstance task = null;
            bool taskCommand = c.kind == "place" || c.kind == "rotate" || c.kind == "withdraw" || c.kind == "submit" || c.kind == "nextRound";
            if (taskCommand && (string.IsNullOrEmpty(c.id) || !Simulation.Tasks.TryGetValue(c.id,out task))) return CommandResult(false,"RequestNotFound");
            switch (c.kind)
            {
                case "clock": ManualCommandClock=c.paused; return CommandResult(true,"Success");
                case "pause":
                    if (Simulation.Paused != c.paused) TogglePause(); return CommandResult(true,"Success");
                case "advance":
                    if (double.IsNaN(c.seconds) || double.IsInfinity(c.seconds) || c.seconds < 0 || c.seconds > 3600) return CommandResult(false,"InvalidInput");
                    if (Simulation.Paused) return CommandResult(false,"Paused");
                    AdvanceGame(c.seconds); return CommandResult(true,"Success");
                case "place":
                    bool placed = TryPlaceRequest(c.id,new Cell(c.x,c.y),c.rotation,out var failure);
                    return CommandResult(placed,placed ? "Success" : "Placement."+failure);
                case "rotate":
                    SelectRequest(c.id); int rotation=task.Rotation; RotateSelected();
                    return CommandResult(task.Rotation != rotation,task.Rotation != rotation ? "Success" : "InvalidState");
                case "withdraw":
                    SelectRequest(c.id); var state=task.State; WithdrawSelected();
                    return CommandResult(task.State != state,task.State != state ? "Success" : "InvalidState");
                case "submit":
                    if (task.State != RequestState.Ready) return CommandResult(false,"InvalidState");
                    SelectRequest(c.id); SubmitSelected();
                    return CommandResult(task.State != RequestState.Ready,task.State != RequestState.Ready ? "Success" : "InvalidState");
                case "reply":
                    if (c.storyRevision != Story.Revision) return CommandResult(false,"StaleStoryRevision");
                    if (c.choice < 0 || c.choice >= Story.Choices.Count) return CommandResult(false,"InvalidChoice");
                    int revision=Story.Revision; ChooseReplyAtRevision(c.choice,c.storyRevision);
                    return CommandResult(Story.Revision != revision,Story.Revision != revision ? "Success" : "ChoiceRejected");
                case "nextRound":
                    SelectRequest(c.id); int count=Simulation.Tasks.Count; NextRound();
                    return CommandResult(Simulation.Tasks.Count != count,Simulation.Tasks.Count != count ? "Success" : "InvalidState");
                case "closeHistory": CloseConversationHistory(); return CommandResult(true,"Success");
                case "train":
                    if (!TrainingEnabled) return CommandResult(false,"Unavailable");
                    var trainingResult=c.investments!=null
                        ? ((!string.IsNullOrEmpty(c.conversationId)||(c.corpusIds?.Length??0)>0||c.corpusPoints!=0)
                            ? CallmeCatgirl.Gameplay.Training.TrainingResult.InvalidInput
                            : training.StartConversationsTraining(c.recipe,c.architecture,c.displayName,c.investments))
                        : !string.IsNullOrEmpty(c.conversationId)
                        ? ((c.corpusIds?.Length??0)>0 ? CallmeCatgirl.Gameplay.Training.TrainingResult.InvalidInput : training.StartConversationTraining(c.recipe,c.architecture,c.displayName,c.conversationId,c.corpusPoints))
                        : training.StartTraining(c.recipe,c.architecture,c.displayName,c.corpusIds ?? Array.Empty<string>());
                    return CommandResult(trainingResult.ToString()=="Success",trainingResult.ToString());
                case "selectModel":
                    if (!TrainingEnabled) return CommandResult(false,"Unavailable");
                    var select=training.SelectModel(c.id); return CommandResult(select.ToString()=="Success",select.ToString());
                case "upgradeCompute":
                    if (!TrainingEnabled) return CommandResult(false,"Unavailable");
                    var upgrade=training.UpgradeCompute(); return CommandResult(upgrade.ToString()=="Success",upgrade.ToString());
                case "renameModel":
                    if (!TrainingEnabled) return CommandResult(false,"Unavailable");
                    var renamed=training.RenameModel(c.id,c.displayName); return CommandResult(renamed.ToString()=="Success",renamed.ToString());
                case "save": bool saved=SaveRun(); return CommandResult(saved,saved?"Success":"SaveRejected");
                case "restore": bool loaded=ContinueRun(); return CommandResult(loaded,loaded?"Success":"RestoreRejected");
                default: return CommandResult(false,"UnknownCommand");
            }
        }
    }
}
