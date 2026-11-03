using System.Collections.Generic;

namespace CityWeaver
{
    internal sealed class HeliportNetCreationGate<TOwner, TNet> where TNet : class
    {
        private readonly Dictionary<TOwner, TNet> m_Pending = new Dictionary<TOwner, TNet>();
        public bool TrySubmit(TOwner owner, TNet net)
        {
            if (m_Pending.ContainsKey(owner)) return false;
            m_Pending.Add(owner, net);
            return true;
        }
        public void Observe(TOwner owner, TNet net)
        {
            if (m_Pending.TryGetValue(owner, out var pending) && ReferenceEquals(pending, net))
                m_Pending.Remove(owner);
        }
    }
}
