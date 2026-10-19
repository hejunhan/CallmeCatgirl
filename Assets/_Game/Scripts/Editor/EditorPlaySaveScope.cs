using System.IO;
using CallmeCatgirl.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CallmeCatgirl.EditorTools
{
    [InitializeOnLoad]
    public static class EditorPlaySaveScope
    {
        private const string Key="CallmeCatgirl.IsolatedAuthoringSaves";
        private const string Menu="CallmeCatgirl/Use Isolated Saves In This Editor";
        static EditorPlaySaveScope(){Apply();EditorApplication.playModeStateChanged+=_=>Apply();}
        [MenuItem(Menu)] public static void Toggle(){SessionState.SetBool(Key,!SessionState.GetBool(Key,false));Apply();}
        [MenuItem(Menu,true)] private static bool Validate(){UnityEditor.Menu.SetChecked(Menu,SessionState.GetBool(Key,false));return !EditorApplication.isPlaying;}
        public static void Enable(){SessionState.SetBool(Key,true);Apply();}
        private static void Apply()
        {
            CoreGameplayController.EditorSaveRoot=SessionState.GetBool(Key,false)
                ? Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/AuthoringPlaySaves")):null;
        }
    }
}
