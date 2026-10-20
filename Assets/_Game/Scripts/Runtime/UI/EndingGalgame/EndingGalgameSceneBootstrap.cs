using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>
    /// Scene entry point. Adds a listener only when none exists, yields input to the host's EventSystem,
    /// then previews an ending when no additive host called Show first.
    /// </summary>
    public sealed class EndingGalgameSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private string previewEndingNodeId = "ending.normal";

        private void Awake()
        {
            var eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            if (Array.Exists(eventSystems, system => system.gameObject.scene != gameObject.scene))
                foreach (var own in eventSystems)
                    if (own.gameObject.scene == gameObject.scene) own.enabled = false;

            if (FindAnyObjectByType<AudioListener>(FindObjectsInactive.Include) == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();
        }

        private IEnumerator Start()
        {
            yield return null;
            if (string.IsNullOrEmpty(previewEndingNodeId)) yield break;
            var panel = FindAnyObjectByType<EndingGalgamePanel>(FindObjectsInactive.Include);
            if (panel == null) throw new MissingReferenceException(name + " found no " + nameof(EndingGalgamePanel) + " in the scene.");
            if (!panel.IsShowing) panel.Show(previewEndingNodeId);
        }
    }
}
