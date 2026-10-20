using System;
using System.Linq;

namespace CallmeCatgirl.Gameplay
{
    public sealed partial class ConversationStory
    {
        // Only content binding is added. The original sessions/rounds/choices engine remains the authority.
        private UserState BindUser(string id, string savedPoolEntryId, bool restoring, bool allowStageDraw = false)
        {
            var state = CreateUserState(id, savedPoolEntryId, restoring, allowStageDraw);
            users.Add(id, state);
            // Reserve only after the slot is published, so a failed add cannot consume a customer.
            if (stagePool && !string.IsNullOrEmpty(state.PoolEntryId)) usedPoolEntries.Add(state.PoolEntryId);
            return state;
        }

        // Builds the complete state before publishing it, so a failed stage draw leaves no partial slot.
        private UserState CreateUserState(string id, string savedPoolEntryId, bool restoring, bool allowStageDraw)
        {
            var configured = ConfiguredProfile(id);
            var profile = configured;
            string poolEntryId = null;
            bool scripted = configured?.Kind == StoryConfiguration.ConversationKind.Scripted;
            if (scripted && !string.IsNullOrEmpty(savedPoolEntryId))
                throw new ArgumentException("Scripted conversation cannot bind ordinary pool content.");
            if (!scripted && stagePool)
            {
                if (restoring && IsSlot(id))
                {
                    if (string.IsNullOrEmpty(savedPoolEntryId))
                        throw new ArgumentException("Saved ordinary pool binding is missing; content revision mismatch.");
                    profile = ordinaryPool.Users.SingleOrDefault(p => p != null && p.UserId == savedPoolEntryId
                        && OrdinaryStagePool.IsStageCustomer(p.UserId));
                    if (profile == null) throw new ArgumentException("Saved stage customer is missing.");
                    poolEntryId = profile.UserId;
                }
                else if (allowStageDraw && IsSlot(id))
                {
                    int stage = Math.Max(1, Math.Min(5, StageProvider?.Invoke() ?? 1));
                    var candidates = ordinaryPool.Users.Where(p => p != null && OrdinaryStagePool.IsStageCustomer(p.UserId)
                        && OrdinaryStagePool.StageOf(p.UserId) == stage && !usedPoolEntries.Contains(p.UserId)).ToArray();
                    if (candidates.Length == 0)
                        throw new StagePoolExhaustedException("No unused stage " + stage + " customer remains.");
                    if (shapeRandom == null) throw new ArgumentNullException("random", "Use the host's saved requestRandom stream.");
                    profile = candidates[shapeRandom.Next(candidates.Length)];
                    poolEntryId = profile.UserId;
                }
            }
            else if (!scripted && ordinaryPool != null)
            {
                if (restoring && (IsSlot(id) || !string.IsNullOrEmpty(savedPoolEntryId)))
                {
                    if (string.IsNullOrEmpty(savedPoolEntryId))
                        throw new ArgumentException("Saved ordinary pool binding is missing; content revision mismatch.");
                    profile = ordinaryPool.Users.SingleOrDefault(p => p.UserId == savedPoolEntryId);
                    if (profile == null) throw new ArgumentException("Saved ordinary pool entry is missing.");
                }
                else if (allowStageDraw && IsSlot(id))
                {
                    // Keep the opening A/B/C request family where possible; monthly users draw from all entries.
                    var matches = ordinaryPool.Users.Where(p => p.Sessions?.FirstOrDefault()?.RequestId == id).ToArray();
                    var candidates = matches.Length > 0 ? matches : ordinaryPool.Users;
                    if (shapeRandom == null) throw new ArgumentNullException("random", "Use the host's saved requestRandom stream.");
                    profile = candidates[shapeRandom.Next(candidates.Length)];
                }
                if (profile != null) poolEntryId = profile.UserId;
            }
            else if (!string.IsNullOrEmpty(savedPoolEntryId))
                throw new ArgumentException("Saved ordinary pool is not configured.");
            var state = new UserState { Profile = profile, PoolEntryId = poolEntryId, NodeId = profile?.FirstNodeId, Affinity = initialAffinity };
            if (!string.IsNullOrWhiteSpace(profile?.InitialHistory)) state.History.Add(profile.InitialHistory);
            return state;
        }

        /// <summary>Recycles a finished stage customer slot; legacy/scripted users stay finished.</summary>
        public bool PrepareSlot(string id)
        {
            if (!IsSlot(id)) return false;
            if (users.TryGetValue(id, out var state))
            {
                bool stageReplacement = stagePool && state.Profile?.Kind != StoryConfiguration.ConversationKind.Scripted
                    && !OrdinaryStagePool.IsStageCustomer(state.PoolEntryId);
                if (!stageReplacement)
                {
                    if (!Finished(id)) return true;
                    if (!stagePool || !OrdinaryStagePool.IsStageCustomer(state.PoolEntryId)) return false;
                }
                UserState replacement;
                try { replacement = CreateUserState(id, null, false, true); }
                catch (StagePoolExhaustedException) { return false; }
                users[id] = replacement;
                if (stagePool && !string.IsNullOrEmpty(replacement.PoolEntryId)) usedPoolEntries.Add(replacement.PoolEntryId);
                return !Finished(id);
            }
            try { BindUser(id, null, false, true); }
            catch (StagePoolExhaustedException) { return false; }
            return !Finished(id);
        }
    }
}
