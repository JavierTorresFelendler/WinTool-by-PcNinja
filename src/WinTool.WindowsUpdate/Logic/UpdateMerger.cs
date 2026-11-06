using System.Collections.Generic;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Logic
{
    /// <summary>WU-03/WU-04: merges the three searches, keeping first-seen order and every source.</summary>
    public static class UpdateMerger
    {
        public static List<UpdateItem> Merge(IEnumerable<KeyValuePair<string, IReadOnlyList<UpdateItem>>> searches)
        {
            var byKey = new Dictionary<string, UpdateItem>();
            var order = new List<UpdateItem>();
            foreach (var search in searches)
            {
                if (search.Value == null) continue;
                foreach (var update in search.Value)
                {
                    UpdateItem existing;
                    if (!byKey.TryGetValue(update.Key, out existing))
                    {
                        if (!update.Sources.Contains(search.Key)) update.Sources.Add(search.Key);
                        byKey[update.Key] = update;
                        order.Add(update);
                    }
                    else if (!existing.Sources.Contains(search.Key))
                    {
                        existing.Sources.Add(search.Key);
                    }
                }
            }

            return order;
        }
    }
}
