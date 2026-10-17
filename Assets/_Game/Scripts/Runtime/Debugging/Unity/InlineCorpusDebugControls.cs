using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Debugging
{
    /// <summary>Attach to a dedicated layout slot directly under ONE existing corpus record.</summary>
    public sealed class InlineCorpusDebugControls : InlineDebugControls
    {
        private readonly InputField[] inputs = new InputField[4];
        private readonly Text[] values = new Text[4];
        private readonly Button[] buttons = new Button[12];
        private Text header;
        public static InlineCorpusDebugControls Attach(RectTransform slot, DebugCommandService service, string corpusId, Font font = null)
        {
            if (!DebugBuild.Enabled) return null;
            var view = Child(slot, "CorpusDebug_" + corpusId).gameObject.AddComponent<InlineCorpusDebugControls>();
            view.Bind(service, corpusId, font); return view;
        }
        protected override void Build()
        {
            header = Label(transform, "[调试] 语料");
            for (int i = 0; i < 4; i++)
            {
                var ability = (AbilityKind)i; var row = Row();
                values[i] = Label(row, ability.ToString(), width: 110); inputs[i] = NumberField(row);
                int index = i;
                buttons[i * 3] = Button(row, "设为输入", () => Change(index, false, 1), 66);
                buttons[i * 3 + 1] = Button(row, "+输入", () => Change(index, true, 1), 53);
                buttons[i * 3 + 2] = Button(row, "−输入", () => Change(index, true, -1), 53);
            }
        }
        private void Change(int index, bool add, int sign)
        {
            if (!TryNumber(inputs[index], out var amount)) { InvalidNumber(); return; }
            Run(add ? DebugCommand.AddCorpus(TargetId, (AbilityKind)index, amount * sign)
                : DebugCommand.SetCorpus(TargetId, (AbilityKind)index, amount));
        }
        protected override void ResetDraft()
        { foreach (var input in inputs) if (input != null) input.SetTextWithoutNotify("1"); }
        protected override void Refresh(DebugSnapshot snapshot, bool enabled)
        {
            bool exists = TargetId != null && snapshot.Corpus.TryGetValue(TargetId, out _);
            var corpus = exists ? snapshot.Corpus[TargetId] : null;
            header.text = $"[调试] 语料 {TargetId ?? "未选择"} · {(corpus == null ? "不存在" : corpus.Consumed ? "已用于训练（冻结）" : "修改仅影响后续训练")}";
            bool editable = enabled && exists && !corpus.Consumed && (snapshot.Capabilities & DebugCapabilities.Corpus) != 0;
            for (int i = 0; i < 4; i++)
            {
                values[i].text = $"{new[] { "写作", "代码", "推理", "检索" }[i]} = {(corpus == null ? "—" : corpus.Score((AbilityKind)i).ToString("0.###"))}";
                inputs[i].interactable = editable;
            }
            foreach (var button in buttons) button.interactable = editable;
        }
    }
}
