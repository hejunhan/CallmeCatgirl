using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Opening Balance")]
    public sealed class OpeningBalanceConfiguration : ScriptableObject
    {
        [TextArea] public string Notice = "暂定开局曲线；覆盖门槛与最低操作时限，不修改原文、模型属性、语料规则或收入。";
        public string[] Users = { "A", "B", "C" };
        public Step[] Sessions = {
            new Step { Minimum = Vector3.one, Excellent = Vector3.one * 2 },
            new Step { Minimum = new Vector3(2,1,1), Excellent = new Vector3(3,2,2) },
            new Step { Minimum = new Vector3(3,2,2), Excellent = new Vector3(5,3,3) }
        };
        [Serializable] public sealed class Step
        {
            public Vector3 Minimum, Excellent;
            [Min(1)] public float MinimumPatience = 100;
        }
        public TaskDefinition Apply(string user, int session, TaskDefinition original)
        {
            if (original == null || Users == null || Array.IndexOf(Users,user) < 0 || Sessions == null || session < 1 || session > Sessions.Length) return original;
            var step = Sessions[session-1];
            if (step == null || step.MinimumPatience <= 0) throw new InvalidOperationException("Invalid opening step.");
            var requirements = new List<AbilityRequirement>();
            foreach (var item in original.Requirements)
            {
                int index = item.Kind == AbilityKind.Writing ? 0 : item.Kind == AbilityKind.Code ? 1 : item.Kind == AbilityKind.Reasoning ? 2 : -1;
                requirements.Add(index < 0 ? item : new AbilityRequirement(item.Kind,step.Minimum[index],step.Excellent[index]));
            }
            return new TaskDefinition(original.Id,original.Title,original.Shape,original.Calculation,original.Speed,
                Math.Max(original.Patience,step.MinimumPatience),original.Tokens,requirements);
        }
    }
}
