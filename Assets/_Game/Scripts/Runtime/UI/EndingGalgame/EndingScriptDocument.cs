using System;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>Serialized shape of EndingGalgame/ending-scripts.json. Unity owns the field names.</summary>
    [Serializable]
    public sealed class EndingScriptDocument
    {
        public int schemaVersion;
        public EndingScript[] endings;
    }

    [Serializable]
    public sealed class EndingScript
    {
        public string id;
        public string triggerNodeId;
        public string titleTextId;
        public string startSceneId;
        public EndingScene[] scenes;
    }

    [Serializable]
    public sealed class EndingScene
    {
        public string id;
        public string background;
        public string bgm;
        public EndingCharacters characters;
        public EndingLine[] lines;
        public EndingChoice[] choices;
    }

    [Serializable]
    public sealed class EndingCharacters
    {
        public EndingCharacter left;
        public EndingCharacter mid;
        public EndingCharacter right;
    }

    /// <summary>expression and headProfile are authored but intentionally ignored by the panel.</summary>
    [Serializable]
    public sealed class EndingCharacter
    {
        public string sprite;
        public string expression;
        public string headProfile;
    }

    [Serializable]
    public sealed class EndingLine
    {
        public string id;
        public string speakerTextId;
        public string textTextId;
        public string headProfile;
    }

    [Serializable]
    public sealed class EndingChoice
    {
        public string id;
        public string labelTextId;
        public string nextSceneId;
    }
}
