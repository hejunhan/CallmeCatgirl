using UnityEditor;
namespace CallmeCatgirl.EditorTools
{
    public static class SceneUILayoutBaker
    {
        [MenuItem("CallmeCatgirl/Save Current UI Layout To Scene")]
        public static void Bake() => PersistentUIAuthoring.Bake();
    }
}
