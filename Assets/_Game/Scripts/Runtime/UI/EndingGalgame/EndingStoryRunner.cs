using System;
using System.Collections.Generic;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>Line/choice cursor over one ending. Holds no text, sprites or input; the view reads it.</summary>
    public sealed class EndingStoryRunner
    {
        private readonly Dictionary<string, EndingScene> scenesById;

        public EndingStoryRunner(EndingScriptDocument document, string endingId)
        {
            Ending = EndingScriptLibrary.FindEnding(document, endingId);
            scenesById = new Dictionary<string, EndingScene>(StringComparer.Ordinal);
            foreach (var scene in Ending.scenes)
                if (!scenesById.TryAdd(scene.id, scene))
                    throw new InvalidOperationException("Ending " + Ending.id + " repeats scene " + scene.id + ".");
            Scene = scenesById.TryGetValue(Ending.startSceneId, out var first)
                ? first : throw new InvalidOperationException("Ending " + Ending.id + " has no start scene " + Ending.startSceneId + ".");
        }

        public EndingScript Ending { get; }
        public EndingScene Scene { get; private set; }
        public int LineIndex { get; private set; }
        public bool IsComplete { get; private set; }

        public EndingLine CurrentLine
            => IsComplete || LineIndex >= Scene.lines.Length ? null : Scene.lines[LineIndex];

        public bool IsAtChoices => !IsComplete && LineIndex >= Scene.lines.Length;

        /// <summary>Advances one line, or settles on the choices after the last line. Never leaves a scene by itself.</summary>
        public bool Advance()
        {
            if (IsComplete || IsAtChoices) return false;
            LineIndex++;
            return true;
        }

        /// <summary>An empty nextSceneId ends the ending; the caller reports the run end exactly once.</summary>
        public bool Choose(int choiceIndex)
        {
            if (!IsAtChoices || choiceIndex < 0 || choiceIndex >= Scene.choices.Length) return false;
            string nextSceneId = Scene.choices[choiceIndex].nextSceneId;
            if (string.IsNullOrEmpty(nextSceneId)) { IsComplete = true; return true; }
            Scene = scenesById[nextSceneId];
            LineIndex = 0;
            return true;
        }
    }
}
