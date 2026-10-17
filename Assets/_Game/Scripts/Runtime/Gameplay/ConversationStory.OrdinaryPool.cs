using System;
using System.Linq;

namespace CallmeCatgirl.Gameplay
{
    public sealed partial class ConversationStory
    {
        // Only content binding is added. The original sessions/rounds/choices engine remains the authority.
        private UserState BindUser(string id, string savedPoolEntryId, bool restoring)
        {
            var configured = configuration?.Users?.FirstOrDefault(p => p != null && p.UserId == id);
            var profile = configured;
            string poolEntryId = null;
            bool scripted = configured?.Kind == StoryConfiguration.ConversationKind.Scripted;
            if (scripted && !string.IsNullOrEmpty(savedPoolEntryId))
                throw new ArgumentException("Scripted conversation cannot bind ordinary pool content.");
            if (!scripted && ordinaryPool != null)
            {
                if (restoring)
                {
                    if (string.IsNullOrEmpty(savedPoolEntryId))
                        throw new ArgumentException("Saved ordinary pool binding is missing; content revision mismatch.");
                    profile = ordinaryPool.Users.SingleOrDefault(p => p.UserId == savedPoolEntryId);
                    if (profile == null) throw new ArgumentException("Saved ordinary pool entry is missing.");
                }
                else
                {
                    // Keep the opening A/B/C request family where possible; monthly users draw from all entries.
                    var matches = ordinaryPool.Users.Where(p => p.Sessions?.FirstOrDefault()?.RequestId == id).ToArray();
                    var candidates = matches.Length > 0 ? matches : ordinaryPool.Users;
                    if (shapeRandom == null) throw new ArgumentNullException("random", "Use the host's saved requestRandom stream.");
                    profile = candidates[shapeRandom.Next(candidates.Length)];
                }
                poolEntryId = profile.UserId;
            }
            else if (!string.IsNullOrEmpty(savedPoolEntryId))
                throw new ArgumentException("Saved ordinary pool is not configured.");
            var state = new UserState { Profile = profile, PoolEntryId = poolEntryId, NodeId = profile?.FirstNodeId, Affinity = initialAffinity };
            if (!string.IsNullOrWhiteSpace(profile?.InitialHistory)) state.History.Add(profile.InitialHistory);
            users.Add(id, state); return state;
        }
    }
}
