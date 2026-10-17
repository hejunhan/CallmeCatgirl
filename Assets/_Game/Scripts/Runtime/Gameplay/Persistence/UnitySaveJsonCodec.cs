using UnityEngine;

namespace CallmeCatgirl.Persistence
{
    // Place outside Core asmdef (Assembly-CSharp). No Newtonsoft/package dependency.
    public sealed class UnitySaveJsonCodec : ISaveJsonCodec
    {
        public string Encode<T>(T value) => JsonUtility.ToJson(value);
        public T Decode<T>(string json) => JsonUtility.FromJson<T>(json);
    }
}
