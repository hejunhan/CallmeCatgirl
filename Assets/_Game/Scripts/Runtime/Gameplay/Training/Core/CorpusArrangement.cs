using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>A draft only. Moving/filtering/cancelling never touches the training ledger.
    /// Its display cells are independent of a training job's actual compute shape.</summary>
    public sealed class CorpusArrangement
    {
        private readonly Dictionary<int,ConversationInvestment> slots=new Dictionary<int,ConversationInvestment>();
        public int Columns { get; private set; }
        public int Rows { get; private set; }
        public IReadOnlyDictionary<int,ConversationInvestment> Slots => slots.ToDictionary(p=>p.Key,
            p=>new ConversationInvestment{ConversationId=p.Value.ConversationId,Points=p.Value.Points});
        public IReadOnlyList<ConversationInvestment> Investments => slots.OrderBy(p=>p.Key).Select(p=>
            new ConversationInvestment{ConversationId=p.Value.ConversationId,Points=p.Value.Points}).ToArray();
        public CorpusArrangement(int columns=3,int rows=3){Configure(columns,rows);}
        public void Configure(int columns,int rows)
        {
            if(columns<1 || rows<1 || columns>12 || rows>12 || (long)columns*rows<slots.Count)
                throw new ArgumentOutOfRangeException(nameof(columns));
            var contents=Investments;Columns=columns;Rows=rows;slots.Clear();
            for(int i=0;i<contents.Count;i++)slots.Add(i,contents[i]);
        }
        public int Find(string conversationId)
            => slots.Where(p=>p.Value.ConversationId==conversationId).Select(p=>p.Key).DefaultIfEmpty(-1).First();
        public int FirstFree => Enumerable.Range(0,Columns*Rows).Where(i=>!slots.ContainsKey(i)).DefaultIfEmpty(-1).First();
        public bool CanPlace(string conversationId,int slot)
            => !string.IsNullOrWhiteSpace(conversationId) && slot>=0 && slot<Columns*Rows
                && (!slots.TryGetValue(slot,out var value) || value.ConversationId==conversationId);
        public bool TryPlace(string conversationId,int slot,double points)
        {
            if(!CanPlace(conversationId,slot) || double.IsNaN(points) || double.IsInfinity(points) || points<=0)return false;
            int previous=Find(conversationId);if(previous>=0)slots.Remove(previous);
            slots[slot]=new ConversationInvestment{ConversationId=conversationId,Points=points};return true;
        }
        public bool SetPoints(string conversationId,double points)
        {
            int slot=Find(conversationId);return slot>=0 && TryPlace(conversationId,slot,points);
        }
        public void Remove(string conversationId){int slot=Find(conversationId);if(slot>=0)slots.Remove(slot);}
        public void Clear()=>slots.Clear();
    }
}
